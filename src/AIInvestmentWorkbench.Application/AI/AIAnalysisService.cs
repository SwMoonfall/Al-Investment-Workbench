using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Application.AI;

public sealed record AIProgress(int Step, int Total, string Message);
public sealed record AnalysisOutcome(Guid Id, AIAnalysisStatus Status, string Structured, string Error, IReadOnlyList<AISourceReference> Sources);
public sealed class AIAnalysisService(IAIStore store, IAIContextBuilder contexts, IAIProvider provider) : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    public async Task<AnalysisOutcome> RunAsync(Guid templateId, AIContextRequest contextRequest, CancellationToken ct = default)
    {
        var template = (await store.TemplatesAsync(ct)).SingleOrDefault(x => x.Id == templateId) ?? throw new BusinessException("模板不存在。");
        return await Run(template, contextRequest with { AnalysisType = template.AnalysisType }, await store.ReadSettingsAsync(ct), ct);
    }
    private async Task<AnalysisOutcome> Run(PromptTemplate template, AIContextRequest request, AISettings settings, CancellationToken ct)
    {
        var context = await contexts.BuildAsync(request, ct); var instructions = PromptCatalog.Assemble(template);
        var id = await store.BeginAsync(new(request.SecurityId, template.AnalysisType, template.Id, instructions, settings.Model, settings.Provider, JsonSerializer.Serialize(context.Sources), context.Json, JsonSerializer.Serialize(settings), request.AccountId), ct);
        AIProviderResult? response = null; var status = AIAnalysisStatus.Failed; var error = ""; var structured = "";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        try
        {
            settings.Validate(true); timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            var task = provider.StructuredCompleteAsync(new(settings, instructions, context.Json, StructuredAnalysis.Schema), timeout.Token);
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            response = await task.WaitAsync(timeout.Token);
            if (!response.Completed) { status = AIAnalysisStatus.Failed; error = AIErrorMessages.For(AIErrorKind.Incomplete); }
            else if (!StructuredAnalysis.TryParse(response.RawOutput, context.Sources, out structured, out error)) status = AIAnalysisStatus.InvalidOutput;
            else status = AIAnalysisStatus.Completed;
        }
        catch (OperationCanceledException) { status = linked.IsCancellationRequested ? AIAnalysisStatus.Cancelled : AIAnalysisStatus.TimedOut; error = AIErrorMessages.For(status == AIAnalysisStatus.Cancelled ? AIErrorKind.Cancelled : AIErrorKind.Timeout); }
        catch (AIProviderException e) { status = e.Kind == AIErrorKind.Timeout ? AIAnalysisStatus.TimedOut : e.Kind == AIErrorKind.Cancelled ? AIAnalysisStatus.Cancelled : AIAnalysisStatus.Failed; error = AIErrorMessages.For(e.Kind); }
        catch (BusinessException e) { error = e.Message; }
        catch (Exception) { error = AIErrorMessages.For(AIErrorKind.Provider); }
        // Never use the cancelled network token for durable audit writes.
        await store.FinishAsync(id, status, response?.RawOutput ?? "", structured, error, response, CancellationToken.None);
        return new(id, status, structured, error, context.Sources);
    }
    public async Task<Guid> FullCompanyAsync(Guid securityId, Guid? accountId, string notes, IProgress<AIProgress>? progress = null, CancellationToken ct = default)
    {
        var templates = await store.TemplatesAsync(ct); var settings = await store.ReadSettingsAsync(ct);
        var reportId = await store.BeginReportAsync(securityId, "Full Company Research · " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm"), ct);
        var ids = new List<Guid>(); var text = new StringBuilder("# Full Company Research\n\nAI 研究建议，未经用户核实；不构成买卖指令。精确输入、模型响应和时间见本地对应分析 ID 的审计。\n\n"); var count = 0;
        text.AppendLine($"请求服务：{settings.Provider} / 模型：{settings.Model} / 开始：{DateTimeOffset.UtcNow:O}");
        var final = AIAnalysisStatus.Completed;
        try
        {
            foreach (var type in PromptCatalog.FullCompanySteps)
            {
                ct.ThrowIfCancellationRequested(); _shutdown.Token.ThrowIfCancellationRequested(); progress?.Report(new(count + 1, 10, PromptCatalog.Name(type)));
                var template = templates.Single(x => x.IsBuiltIn && x.AnalysisType == type);
                var result = await Run(template, new(securityId, accountId, type, notes), settings, ct); ids.Add(result.Id);
                text.AppendLine("## " + PromptCatalog.Name(type)).AppendLine($"分析 ID：{result.Id} / {result.Status}").AppendLine(result.Status == AIAnalysisStatus.Completed ? StructuredAnalysis.Render(result.Structured) : result.Error);
                if (result.Status == AIAnalysisStatus.Completed)
                {
                    using var parsed = JsonDocument.Parse(result.Structured);
                    var cited = parsed.RootElement.EnumerateObject().SelectMany(x => x.Value.EnumerateArray()).SelectMany(x => x.GetProperty("source_ids").EnumerateArray()).Select(x => x.GetString()).ToHashSet();
                    text.AppendLine("本节引用（来源内容与 AI 论断均需核实）：");
                    foreach (var source in result.Sources.Where(x => cited.Contains(x.Id))) text.AppendLine($"- {source.Id} · {source.Title} · {source.Kind} · {source.Date}");
                }
                if (result.Status == AIAnalysisStatus.Completed) count++; else final = result.Status;
                await store.UpdateReportAsync(reportId, ids, text.ToString(), count, AIAnalysisStatus.Running);
                if (result.Status != AIAnalysisStatus.Completed) break;
            }
        }
        catch (OperationCanceledException) { final = AIAnalysisStatus.Cancelled; }
        catch (Exception) { final = AIAnalysisStatus.Failed; text.AppendLine("流程未完成；已保存章节保留，请查看单项审计状态。"); }
        if (count < 10) text.AppendLine($"\n未完成报告：仅 {count}/10 章节成功；其余未生成，不能作为完整研究。");
        await store.UpdateReportAsync(reportId, ids, text.ToString(), count, final); progress?.Report(new(count, 10, final.ToString())); return reportId;
    }
    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await store.ReadSettingsAsync(ct); settings.Validate(true);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token); linked.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            var task = provider.CompleteAsync(new(settings with { MaxOutputTokens = Math.Min(settings.MaxOutputTokens, 256) }, "Reply only OK. Do not include any other information.", "{}"), linked.Token);
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var result = await task.WaitAsync(linked.Token); return result.Completed ? "连接成功。测试仅发送简短连通性请求。" : AIErrorMessages.For(AIErrorKind.Incomplete);
        }
        catch (OperationCanceledException) { return AIErrorMessages.For(ct.IsCancellationRequested ? AIErrorKind.Cancelled : AIErrorKind.Timeout); }
        catch (AIProviderException e) { return AIErrorMessages.For(e.Kind); }
        catch (Exception) { return AIErrorMessages.For(AIErrorKind.Provider); }
    }
    public void Dispose() { _shutdown.Cancel(); _shutdown.Dispose(); }
}
