using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Application.AI;

public sealed record AISettings(string Provider = "Disabled", string Model = "", int TimeoutSeconds = 90, int MaxOutputTokens = 4096, string BaseUrl = "", Guid? SourceId = null, string ApiVersion = "2023-06-01", string Organization = "", string Project = "", string CompatibilityApi = "ChatCompletions")
{
    public Uri Endpoint => new(string.IsNullOrWhiteSpace(BaseUrl) ? ProviderTypes.DefaultUrl(Provider) : BaseUrl.TrimEnd('/') + "/");
    public string SecretScope => SourceId is { } id ? $"ai-source:{id:N}:{Provider}:{Endpoint.AbsoluteUri}" : Endpoint.AbsoluteUri;
    public void Validate(bool forRequest = false)
    {
        if (Provider != "Disabled" && !ProviderTypes.All.Contains(Provider)) throw new BusinessException("请选择支持的 Provider。");
        if (TimeoutSeconds is < 1 or > 600 || MaxOutputTokens is < 128 or > 65536) throw new BusinessException("Timeout 应为 1–600 秒，MaxOutputTokens 为 128–65536。");
        if (Model.Length > 150 || Model.Any(char.IsControl)) throw new BusinessException("模型名称无效。");
        if (!Uri.TryCreate(string.IsNullOrWhiteSpace(BaseUrl) ? ProviderTypes.DefaultUrl(Provider) : BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new BusinessException("BaseUrl 必须是无用户名、密码、查询参数或片段的 HTTPS API 根地址。");
        if (Provider == "OpenAICompatible" && string.IsNullOrWhiteSpace(BaseUrl)) throw new BusinessException("兼容服务必须填写 Base URL。");
        if (new[] { ApiVersion, Organization, Project }.Any(x => x.Length > 200 || x.Any(char.IsControl))) throw new BusinessException("请求配置字段无效。");
        if (Provider == "Anthropic" && !DateOnly.TryParseExact(ApiVersion, "yyyy-MM-dd", out _)) throw new BusinessException("Anthropic API Version 应为 yyyy-MM-dd。");
        if (CompatibilityApi is not ("ChatCompletions" or "Responses")) throw new BusinessException("请选择兼容 API 协议。");
        if (forRequest && (Provider == "Disabled" || string.IsNullOrWhiteSpace(Model))) throw new BusinessException("请先在 AI服务页面添加模型并设为当前模型。");
    }
}
public interface ISecretStore
{
    Task<string?> ReadAsync(string scope, CancellationToken ct = default);
    Task WriteAsync(string scope, string secret, CancellationToken ct = default);
    Task DeleteAsync(string scope, CancellationToken ct = default);
}
public enum AIErrorKind { Configuration, Authentication, Quota, RateLimit, Network, Timeout, Cancelled, Provider, Incomplete }
public sealed class AIProviderException(AIErrorKind kind) : Exception(AIErrorMessages.For(kind)) { public AIErrorKind Kind { get; } = kind; }
public static class AIErrorMessages
{
    public static string For(AIErrorKind kind) => kind switch
    {
        AIErrorKind.Configuration => "AI 未配置或该地址尚未保存 API Key。", AIErrorKind.Authentication => "AI 身份验证失败，请检查该地址的 API Key 与权限。",
        AIErrorKind.Quota => "AI 配额或余额不足，请检查服务账户。", AIErrorKind.RateLimit => "AI 请求受限（429），请稍后重试。",
        AIErrorKind.Network => "无法连接 AI 服务，请检查网络。", AIErrorKind.Timeout => "AI 请求超时，已停止等待。", AIErrorKind.Cancelled => "AI 请求已取消。",
        AIErrorKind.Incomplete => "AI 返回未完成或拒绝的响应，已保留原文。", _ => "AI 服务请求失败。已保留本地数据，可稍后重试。"
    };
}
public sealed record AIRequest(AISettings Settings, string Instructions, string ContextJson, string? JsonSchema = null);
public sealed record AIProviderResult(string RawOutput, string Envelope = "", string ResponseId = "", string Model = "", bool Completed = true);
public interface IAIProvider
{
    string Name { get; }
    Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default);
    Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default);
}
public sealed record AISourceReference(string Id, string Kind, string Title, string Date);
public sealed record AIContextRequest(Guid? SecurityId, Guid? AccountId, AnalysisType AnalysisType, string UserNotes = "", DateOnly? JournalFrom = null, DateOnly? JournalTo = null, Guid? ReviewId = null);
public sealed record AIContext(string Json, IReadOnlyList<AISourceReference> Sources);
public interface IAIContextBuilder { Task<AIContext> BuildAsync(AIContextRequest request, CancellationToken ct = default); }
public interface IAIStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<AISettings> ReadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(AISettings settings, CancellationToken ct = default);
    Task<IReadOnlyList<PromptTemplate>> TemplatesAsync(CancellationToken ct = default);
    Task EditTemplateAsync(Guid id, int revision, string name, string body, CancellationToken ct = default);
    Task<Guid> CopyTemplateAsync(Guid id, CancellationToken ct = default);
    Task RestoreTemplateAsync(Guid id, int revision, CancellationToken ct = default);
    Task<Guid> BeginAsync(AIAnalysis analysis, CancellationToken ct = default);
    Task FinishAsync(Guid id, AIAnalysisStatus status, string raw, string structured, string error, AIProviderResult? response = null, CancellationToken ct = default);
    Task<IReadOnlyList<AIAnalysis>> AnalysesAsync(Guid? securityId, CancellationToken ct = default);
    Task<Guid> BeginReportAsync(Guid securityId, string title, CancellationToken ct = default);
    Task UpdateReportAsync(Guid id, IReadOnlyList<Guid> analyses, string markdown, int completed, AIAnalysisStatus status, CancellationToken ct = default);
    Task<IReadOnlyList<AIResearchReport>> ReportsAsync(Guid securityId, CancellationToken ct = default);
}
