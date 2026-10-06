using System.ClientModel;
using System.ClientModel.Primitives;
using AIInvestmentWorkbench.Application.AI;
using OpenAI.Responses;
namespace AIInvestmentWorkbench.AI;

// Responses types in the pinned official 2.14.0 SDK carry OPENAI001.
#pragma warning disable OPENAI001
public sealed class OpenAIProvider : IAIProvider, IDisposable
{
    private readonly ISecretStore _secrets; private readonly HttpClient _http;
    public OpenAIProvider(ISecretStore secrets) : this(secrets, new HttpClientHandler { AllowAutoRedirect = false }) { }
    public OpenAIProvider(ISecretStore secrets, HttpMessageHandler handler) { _secrets = secrets; _http = new(handler) { Timeout = Timeout.InfiniteTimeSpan }; }
    public string Name => "OpenAI";
    public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => Send(request with { JsonSchema = null }, cancellationToken);
    public Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default)
        => Send(request with { JsonSchema = request.JsonSchema ?? StructuredAnalysis.Schema }, cancellationToken);
    private async Task<AIProviderResult> Send(AIRequest request, CancellationToken ct)
    {
        request.Settings.Validate(true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(request.Settings.TimeoutSeconds));
        try
        {
            var key = await _secrets.ReadAsync(request.Settings.SecretScope, timeout.Token);
            if (string.IsNullOrWhiteSpace(key)) throw new AIProviderException(AIErrorKind.Configuration);
            var options = new ResponsesClientOptions { Endpoint = request.Settings.Endpoint, NetworkTimeout = TimeSpan.FromSeconds(request.Settings.TimeoutSeconds), Transport = new HttpClientPipelineTransport(_http), RetryPolicy = new ClientRetryPolicy(0), EnableDistributedTracing = false,
                ClientLoggingOptions = new ClientLoggingOptions { EnableLogging = false, EnableMessageContentLogging = false } };
            var client = new ResponsesClient(new ApiKeyCredential(key), options);
            var input = new CreateResponseOptions { Model = request.Settings.Model.Trim(), Instructions = request.Instructions, MaxOutputTokenCount = request.Settings.MaxOutputTokens, StoredOutputEnabled = false };
            input.InputItems.Add(ResponseItem.CreateUserMessageItem(request.ContextJson));
            if (request.JsonSchema is { } schema) input.TextOptions = new ResponseTextOptions { TextFormat = ResponseTextFormat.CreateJsonSchemaFormat("investment_research", BinaryData.FromString(schema), jsonSchemaIsStrict: true) };
            var response = await client.CreateResponseAsync(input, timeout.Token);
            var result = response.Value;
            return new(result.GetOutputText(), response.GetRawResponse().Content.ToString(), result.Id, result.Model, result.Status?.ToString().Equals("completed", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (OperationCanceledException) { if (ct.IsCancellationRequested) throw; throw new AIProviderException(AIErrorKind.Timeout); }
        catch (ClientResultException e)
        {
            var kind = e.Status switch { 0 => AIErrorKind.Network, 401 or 403 => AIErrorKind.Authentication, 402 => AIErrorKind.Quota, 429 => AIErrorKind.RateLimit, _ => AIErrorKind.Provider };
            // Inspect only the machine-readable error code. Never propagate/log provider messages.
            if (e.Status == 429)
            {
                try { using var json = System.Text.Json.JsonDocument.Parse(e.GetRawResponse()!.Content.ToString()); if (json.RootElement.GetProperty("error").GetProperty("code").GetString() == "insufficient_quota") kind = AIErrorKind.Quota; }
                catch (Exception) { /* Keep safe rate-limit classification if error body is nonstandard. */ }
            }
            throw new AIProviderException(kind);
        }
        catch (HttpRequestException) { throw new AIProviderException(AIErrorKind.Network); }
    }
    public void Dispose() => _http.Dispose();
}
#pragma warning restore OPENAI001
