using System.Net;
using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.AI;
using AIInvestmentWorkbench.Application.AI;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class NativeAIProviderTests
{
    private static ProviderSource Source(string type) => new(Guid.NewGuid(), "test", type, "https://unit.example/api", 1, 1024);
    [Theory]
    [InlineData("OpenAI", "Authorization", "Bearer test-key")]
    [InlineData("OpenAICompatible", "Authorization", "Bearer test-key")]
    [InlineData("GoogleGenAI", "x-goog-api-key", "test-key")]
    [InlineData("Anthropic", "x-api-key", "test-key")]
    public async Task DiscoveryUsesNativeAuthenticationAndDeduplicates(string type, string header, string auth)
    {
        var source = Source(type); var secret = new MemorySecrets(); await secret.WriteAsync(source.Settings().SecretScope, "test-key");
        var handler = new Handler((request, _) =>
        {
            Assert.Equal("https://unit.example/api/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal(auth, request.Headers.GetValues(header).Single());
            Assert.DoesNotContain("test-key", request.RequestUri.AbsoluteUri);
            if (type == "Anthropic") Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
            return Task.FromResult(Json(type == "GoogleGenAI" ? """{"models":[{"name":"models/a","supportedGenerationMethods":["generateContent"]},{"name":"models/a","supportedGenerationMethods":["generateContent"]},{"name":"models/embed","supportedGenerationMethods":["embedContent"]}]}""" : """{"data":[{"id":"a"},{"id":"a"}]}"""));
        });
        using var provider = new NativeAIProvider(secret, handler);
        Assert.Equal("a", Assert.Single(await provider.DiscoverAsync(source)).Id);
    }
    [Theory][InlineData("GoogleGenAI")][InlineData("Anthropic")]
    public async Task DiscoveryConsumesAllPages(string type)
    {
        var source = Source(type); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key"); var calls = 0;
        using var provider = new NativeAIProvider(secrets, new Handler((r, _) =>
        {
            calls++;
            if (calls == 2) Assert.Contains(type == "GoogleGenAI" ? "pageToken=next%2Fpage" : "after_id=next%2Fpage", r.RequestUri!.AbsoluteUri);
            return Task.FromResult(Json(type == "GoogleGenAI"
                ? calls == 1 ? """{"models":[{"name":"models/a","supportedGenerationMethods":["generateContent"]}],"nextPageToken":"next/page"}""" : """{"models":[{"name":"models/b","supportedGenerationMethods":["generateContent"]}]}"""
                : calls == 1 ? """{"data":[{"id":"a"}],"has_more":true,"last_id":"next/page"}""" : """{"data":[{"id":"b"}],"has_more":false}"""));
        }));
        Assert.Equal(2, (await provider.DiscoverAsync(source)).Count); Assert.Equal(2, calls);
    }
    [Theory][InlineData("GoogleGenAI")][InlineData("Anthropic")][InlineData("OpenAICompatible")]
    public async Task NativeCompletionSendsCorrectPayloadAndReadsResponse(string type)
    {
        var source = Source(type); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key");
        using var provider = new NativeAIProvider(secrets, new Handler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var root = body.RootElement;
            if (type == "GoogleGenAI")
            {
                Assert.EndsWith("/models/test-model:generateContent", request.RequestUri!.AbsoluteUri);
                Assert.Equal("application/json", root.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
                Assert.Equal("object", root.GetProperty("generationConfig").GetProperty("responseJsonSchema").GetProperty("type").GetString());
                return Json("""{"modelVersion":"returned","candidates":[{"content":{"parts":[{"text":"private reasoning","thought":true},{"text":"OK"}]},"finishReason":"STOP"}]}""");
            }
            Assert.Equal("test-model", root.GetProperty("model").GetString());
            if (type == "Anthropic")
            {
                Assert.EndsWith("/messages", request.RequestUri!.AbsoluteUri);
                Assert.Equal("json_schema", root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString());
                return Json("""{"id":"message-id","model":"returned","content":[{"type":"text","text":"OK"}],"stop_reason":"end_turn"}""");
            }
            Assert.EndsWith("/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("json_schema", root.GetProperty("response_format").GetProperty("type").GetString());
            return Json("""{"id":"chat-id","model":"returned","choices":[{"message":{"content":"OK"},"finish_reason":"stop"}]}""");
        }));
        var result = await provider.StructuredCompleteAsync(new(source.Settings("test-model"), "system", "user", """{"type":"object","properties":{}}"""));
        Assert.True(result.Completed); Assert.Equal("OK", result.RawOutput); Assert.Equal("returned", result.Model);
    }
    [Theory][InlineData(401, AIErrorKind.Authentication)][InlineData(403, AIErrorKind.Authentication)][InlineData(429, AIErrorKind.RateLimit)][InlineData(500, AIErrorKind.Provider)]
    public async Task ErrorsAreSafeAndNotRetried(int status, AIErrorKind kind)
    {
        var source = Source("Anthropic"); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key"); var calls = 0;
        using var provider = new NativeAIProvider(secrets, new Handler((_, _) => { calls++; return Task.FromResult(Json("secret echo", (HttpStatusCode)status)); }));
        var error = await Assert.ThrowsAsync<AIProviderException>(() => provider.DiscoverAsync(source));
        Assert.Equal(kind, error.Kind); Assert.DoesNotContain("secret echo", error.ToString()); Assert.Equal(1, calls);
    }
    [Fact] public async Task MissingKeySendsNoRequest()
    {
        using var provider = new NativeAIProvider(new MemorySecrets(), new Handler((_, _) => throw new Xunit.Sdk.XunitException("Network must not run")));
        Assert.Equal(AIErrorKind.Configuration, (await Assert.ThrowsAsync<AIProviderException>(() => provider.DiscoverAsync(Source("OpenAI")))).Kind);
    }
    [Theory][InlineData("{}")] [InlineData("not-json")] [InlineData("{\"data\":null}")]
    public async Task MalformedDiscoveryIsSafe(string response)
    {
        var source = Source("OpenAI"); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key");
        using var provider = new NativeAIProvider(secrets, new Handler((_, _) => Task.FromResult(Json(response))));
        Assert.Equal(AIErrorKind.Provider, (await Assert.ThrowsAsync<AIProviderException>(() => provider.DiscoverAsync(source))).Kind);
    }
    [Fact] public async Task TimeoutAndExplicitCancellationAreDistinct()
    {
        var source = Source("GoogleGenAI"); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key");
        using var provider = new NativeAIProvider(secrets, new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json("{}"); }));
        Assert.Equal(AIErrorKind.Timeout, (await Assert.ThrowsAsync<AIProviderException>(() => provider.DiscoverAsync(source))).Kind);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DiscoverAsync(source, cancel.Token));
    }
    [Fact] public async Task PaginationLoopFailsInsteadOfHanging()
    {
        var source = Source("Anthropic"); var secrets = new MemorySecrets(); await secrets.WriteAsync(source.Settings().SecretScope, "test-key");
        using var provider = new NativeAIProvider(secrets, new Handler((_, _) => Task.FromResult(Json("""{"data":[],"has_more":true,"last_id":"same"}"""))));
        Assert.Equal(AIErrorKind.Provider, (await Assert.ThrowsAsync<AIProviderException>(() => provider.DiscoverAsync(source))).Kind);
    }
    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
}
public sealed class MemorySecrets : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];
    public Task<string?> ReadAsync(string scope, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(scope));
    public Task WriteAsync(string scope, string secret, CancellationToken ct = default) { Values[scope] = secret; return Task.CompletedTask; }
    public Task DeleteAsync(string scope, CancellationToken ct = default) { Values.Remove(scope); return Task.CompletedTask; }
}
