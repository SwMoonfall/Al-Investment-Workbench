using System.Net;
using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.AI;
using AIInvestmentWorkbench.Application.AI;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class OpenAIProviderTests
{
    private static AIRequest Request() => new(new("OpenAI", "configured-model", 2, 1000, "https://unit-test.example/v1"), "research only", "{\"selected\":true}", StructuredAnalysis.Schema);
    private static string Response(string text) => JsonSerializer.Serialize(new { id = "resp_offline", @object = "response", created_at = 1700000000, status = "completed", model = "returned-model", output = new[] { new { type = "message", id = "msg_offline", status = "completed", role = "assistant", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } } } });
    [Fact] public async Task OfficialSdkSendsResponsesStrictSchemaNoStoreAndConfiguredModel()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Response(FakeAIProvider.ValidJson())))); var secrets = new Secrets(); using var provider = new OpenAIProvider(secrets, handler);
        var result = await provider.StructuredCompleteAsync(Request()); Assert.True(result.Completed); Assert.Equal("resp_offline", result.ResponseId); Assert.Equal("returned-model", result.Model); Assert.Contains("response", result.Envelope);
        Assert.Equal("https://unit-test.example/v1/responses", handler.Url); Assert.Equal("https://unit-test.example/v1/", secrets.Scope); Assert.Equal("Bearer fake-secret-for-test", handler.Auth);
        using var body = JsonDocument.Parse(handler.Body!); var r = body.RootElement; Assert.Equal("configured-model", r.GetProperty("model").GetString()); Assert.False(r.GetProperty("store").GetBoolean()); Assert.Equal(1000, r.GetProperty("max_output_tokens").GetInt32()); Assert.Equal("research only", r.GetProperty("instructions").GetString()); Assert.True(r.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean()); Assert.DoesNotContain("fake-secret", handler.Body!);
    }
    [Theory] [InlineData(401, AIErrorKind.Authentication)] [InlineData(403, AIErrorKind.Authentication)] [InlineData(402, AIErrorKind.Quota)] [InlineData(429, AIErrorKind.RateLimit)] [InlineData(500, AIErrorKind.Provider)]
    public async Task ApiErrorsAreSafeAndNotAutomaticallyRetried(int status, AIErrorKind expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Json((HttpStatusCode)status, "{\"error\":{\"message\":\"secret echo\",\"code\":\"failure\"}}"))); using var provider = new OpenAIProvider(new Secrets(), handler);
        var error = await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request())); Assert.Equal(expected, error.Kind); Assert.DoesNotContain("secret echo", error.ToString()); Assert.Equal(1, handler.Calls);
    }
    [Fact] public async Task InsufficientQuota429IsDistinguished()
    {
        using var provider = new OpenAIProvider(new Secrets(), new Handler((_, _) => Task.FromResult(Json(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":\"insufficient_quota\",\"message\":\"no balance\"}}"))));
        Assert.Equal(AIErrorKind.Quota, (await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request()))).Kind);
    }
    [Fact] public async Task MissingKeyNeverSendsHttpRequest()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException()); using var provider = new OpenAIProvider(new Secrets { Value = null }, handler);
        Assert.Equal(AIErrorKind.Configuration, (await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request()))).Kind); Assert.Equal(0, handler.Calls);
    }
    [Fact] public async Task HttpCancellationPropagatesToCaller()
    {
        var started = new TaskCompletionSource(); var handler = new Handler(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Json(HttpStatusCode.OK, "{}"); }); using var provider = new OpenAIProvider(new Secrets(), handler); using var cancellation = new CancellationTokenSource();
        var task = provider.CompleteAsync(Request(), cancellation.Token); await started.Task; cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public async Task HttpTimeoutHasExplicitClassification()
    {
        using var provider = new OpenAIProvider(new Secrets(), new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Json(HttpStatusCode.OK, "{}"); }));
        Assert.Equal(AIErrorKind.Timeout, (await Assert.ThrowsAsync<AIProviderException>(() => provider.CompleteAsync(Request() with { Settings = Request().Settings with { TimeoutSeconds = 1 } }))).Kind);
    }
    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Calls; public string? Body, Url, Auth;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; Url = request.RequestUri!.AbsoluteUri; Auth = request.Headers.Authorization?.ToString(); Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken); return await response(request, cancellationToken); }
    }
    private sealed class Secrets : ISecretStore
    {
        public string? Value = "fake-secret-for-test", Scope;
        public Task<string?> ReadAsync(string scope, CancellationToken ct = default) { Scope = scope; return Task.FromResult(Value); }
        public Task WriteAsync(string scope, string secret, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string scope, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
