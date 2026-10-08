using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIInvestmentWorkbench.Application.AI;

namespace AIInvestmentWorkbench.AI;

// Protocol dispatcher. New provider protocols can be added here without changing the catalog/UI contract.
public sealed class NativeAIProvider : IAIProvider, IModelDiscovery, IDisposable
{
    private readonly ISecretStore _secrets;
    private readonly HttpClient _http;
    private readonly OpenAIProvider _openAI;
    public NativeAIProvider(ISecretStore secrets) : this(secrets, new HttpClientHandler { AllowAutoRedirect = false }) { }
    public NativeAIProvider(ISecretStore secrets, HttpMessageHandler handler)
    { _secrets = secrets; _http = new(handler, false) { Timeout = Timeout.InfiniteTimeSpan }; _openAI = new(secrets, handler); }
    public string Name => "Native";
    public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => Complete(request with { JsonSchema = null }, cancellationToken);
    public Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => Complete(request with { JsonSchema = request.JsonSchema ?? StructuredAnalysis.Schema }, cancellationToken);
    private async Task<AIProviderResult> Complete(AIRequest request, CancellationToken ct)
    {
        var s = request.Settings; s.Validate(true);
        if (s.Provider == "OpenAI" || s.Provider == "OpenAICompatible" && s.CompatibilityApi == "Responses")
            return request.JsonSchema is null ? await _openAI.CompleteAsync(request, ct) : await _openAI.StructuredCompleteAsync(request, ct);
        object body; string path;
        var schema = request.JsonSchema is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(request.JsonSchema);
        if (s.Provider == "GoogleGenAI")
        {
            path = "models/" + Uri.EscapeDataString(ProviderTypes.NormalizeModel(s.Provider, s.Model)) + ":generateContent";
            var config = new Dictionary<string, object> { ["maxOutputTokens"] = s.MaxOutputTokens };
            if (schema is { } json) { config["responseMimeType"] = "application/json"; config["responseJsonSchema"] = json; }
            body = new { systemInstruction = new { parts = new[] { new { text = request.Instructions } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = request.ContextJson } } } }, generationConfig = config };
        }
        else if (s.Provider == "Anthropic")
        {
            path = "messages";
            var payload = new Dictionary<string, object> { ["model"] = s.Model, ["max_tokens"] = s.MaxOutputTokens,
                ["system"] = request.Instructions, ["messages"] = new[] { new { role = "user", content = request.ContextJson } } };
            if (schema is { } json) payload["output_config"] = new { format = new { type = "json_schema", schema = json } };
            body = payload;
        }
        else
        {
            path = "chat/completions";
            var payload = new Dictionary<string, object> { ["model"] = s.Model, ["max_tokens"] = s.MaxOutputTokens,
                ["messages"] = new[] { new { role = "system", content = request.Instructions }, new { role = "user", content = request.ContextJson } } };
            if (schema is { } json) payload["response_format"] = new { type = "json_schema", json_schema = new { name = "investment_research", strict = true, schema = json } };
            body = payload;
        }
        using var doc = await Send(s, path, body, ct);
        try
        {
            var root = doc.RootElement; string output; bool complete;
            if (s.Provider == "GoogleGenAI")
            {
                var candidate = root.GetProperty("candidates")[0];
                output = string.Concat(candidate.GetProperty("content").GetProperty("parts").EnumerateArray()
                    .Where(x => !x.TryGetProperty("thought", out var thought) || !thought.GetBoolean()).Select(x => Text(x, "text")));
                complete = Text(candidate, "finishReason") == "STOP";
            }
            else if (s.Provider == "Anthropic")
            { output = string.Concat(root.GetProperty("content").EnumerateArray().Where(x => Text(x, "type") == "text").Select(x => Text(x, "text"))); complete = Text(root, "stop_reason") == "end_turn"; }
            else
            { var choice = root.GetProperty("choices")[0]; output = Text(choice.GetProperty("message"), "content"); complete = Text(choice, "finish_reason") == "stop"; }
            return new(output, root.GetRawText(), Text(root, "id"), Text(root, s.Provider == "GoogleGenAI" ? "modelVersion" : "model"), complete && !string.IsNullOrWhiteSpace(output));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { throw new AIProviderException(AIErrorKind.Provider); }
    }
    public async Task<IReadOnlyList<RemoteModel>> DiscoverAsync(ProviderSource source, CancellationToken ct = default)
    {
        source.Validate(); var s = source.Settings(); var result = new Dictionary<string, RemoteModel>(StringComparer.Ordinal);
        string? cursor = null; var seen = new HashSet<string>();
        using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        totalTimeout.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
        try
        {
            do
            {
                var path = "models" + (cursor is null ? "" : s.Provider == "GoogleGenAI" ? "?pageToken=" + Uri.EscapeDataString(cursor) : "?after_id=" + Uri.EscapeDataString(cursor));
                using var doc = await Send(s, path, null, totalTimeout.Token);
                var root = doc.RootElement;
                // Google's protobuf JSON can omit an empty repeated field.
                var items = s.Provider == "GoogleGenAI" && !root.TryGetProperty("models", out _) ? []
                    : root.GetProperty(s.Provider == "GoogleGenAI" ? "models" : "data").EnumerateArray().ToArray();
                foreach (var item in items)
                {
                    if (s.Provider == "GoogleGenAI" && (!item.TryGetProperty("supportedGenerationMethods", out var methods) || !methods.EnumerateArray().Any(x => x.GetString() == "generateContent"))) continue;
                    var id = ProviderTypes.NormalizeModel(s.Provider, Text(item, s.Provider == "GoogleGenAI" ? "name" : "id"));
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    result[id] = new(id, Text(item, s.Provider == "GoogleGenAI" ? "displayName" : "display_name") is { Length: > 0 } name ? name : id);
                }
                cursor = s.Provider == "GoogleGenAI" ? Text(root, "nextPageToken") : root.TryGetProperty("has_more", out var more) && more.GetBoolean() ? Text(root, "last_id") : null;
                if (cursor == "" && s.Provider != "GoogleGenAI") throw new AIProviderException(AIErrorKind.Provider);
                if (!string.IsNullOrEmpty(cursor) && !seen.Add(cursor)) throw new AIProviderException(AIErrorKind.Provider);
            } while (!string.IsNullOrEmpty(cursor));
            return result.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AIProviderException(AIErrorKind.Timeout); }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException) { throw new AIProviderException(AIErrorKind.Provider); }
    }
    private async Task<JsonDocument> Send(AISettings s, string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(s.TimeoutSeconds));
        try
        {
            var key = await _secrets.ReadAsync(s.SecretScope, timeout.Token);
            if (string.IsNullOrWhiteSpace(key)) throw new AIProviderException(AIErrorKind.Configuration);
            using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(s.Endpoint, path));
            if (s.Provider == "GoogleGenAI") request.Headers.Add("x-goog-api-key", key);
            else if (s.Provider == "Anthropic") { request.Headers.Add("x-api-key", key); request.Headers.Add("anthropic-version", s.ApiVersion); }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                if (!string.IsNullOrWhiteSpace(s.Organization)) request.Headers.Add("OpenAI-Organization", s.Organization);
                if (!string.IsNullOrWhiteSpace(s.Project)) request.Headers.Add("OpenAI-Project", s.Project);
            }
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new AIProviderException((int)response.StatusCode switch
            { 401 or 403 => AIErrorKind.Authentication, 402 => AIErrorKind.Quota, 429 => AIErrorKind.RateLimit, _ => AIErrorKind.Provider });
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AIProviderException(AIErrorKind.Timeout); }
        catch (HttpRequestException) { throw new AIProviderException(AIErrorKind.Network); }
        catch (JsonException) { throw new AIProviderException(AIErrorKind.Provider); }
    }
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public void Dispose() { _http.Dispose(); _openAI.Dispose(); }
}
