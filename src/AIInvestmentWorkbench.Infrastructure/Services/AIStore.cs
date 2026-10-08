using System.Text.Json;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class AIStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IAIStore, IProviderCatalogStore
{
    public Task InitializeAsync(CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        var existing = await db.PromptTemplates.Where(x => x.IsBuiltIn).Select(x => x.AnalysisType).ToListAsync(ct);
        foreach (var type in Enum.GetValues<AnalysisType>().Except(existing)) db.PromptTemplates.Add(new(PromptCatalog.Name(type), type, PromptCatalog.Body(type), true));
        foreach (var item in await db.AIAnalyses.Where(x => x.Status == AIAnalysisStatus.Running).ToListAsync(ct)) item.Finish(AIAnalysisStatus.Interrupted, "", "", "上次会话结束时请求未完成；不会自动重试收费请求。");
        foreach (var report in await db.AIResearchReports.Where(x => x.Status == AIAnalysisStatus.Running).ToListAsync(ct)) report.Update(report.SectionIdsJson, report.ReportMarkdown + "\n会话中断，报告不完整。", report.CompletedSections, AIAnalysisStatus.Interrupted);
        return true;
    }, ct);
    public async Task<AISettings> ReadSettingsAsync(CancellationToken ct = default)
    { if (await ReadCatalogAsync(ct) is { } catalog) return catalog.ActiveSettings; await using var db = await factory.CreateDbContextAsync(ct); var json = await db.AppSettings.Where(x => x.Key == "AI.Settings").Select(x => x.Value).SingleOrDefaultAsync(ct); return json is null ? new() : JsonSerializer.Deserialize<AISettings>(json) ?? new(); }
    public async Task<ProviderCatalog?> ReadCatalogAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AppSettings.AsNoTracking().Where(x => x.Key == "AI.ProviderCatalog" || x.Key.StartsWith("AI.ProviderSource.") || x.Key.StartsWith("AI.ProviderModel.")).ToListAsync(ct);
        var header = rows.SingleOrDefault(x => x.Key == "AI.ProviderCatalog");
        if (header is null) return null;
        var catalog = new ProviderCatalog(rows.Where(x => x.Key.StartsWith("AI.ProviderSource.", StringComparison.Ordinal)).Select(x => JsonSerializer.Deserialize<ProviderSource>(x.Value)!).ToArray(),
            rows.Where(x => x.Key.StartsWith("AI.ProviderModel.", StringComparison.Ordinal)).Select(x => JsonSerializer.Deserialize<ProviderModel>(x.Value)!).ToArray(), JsonSerializer.Deserialize<Guid?>(header.Value));
        catalog.Validate(); return catalog;
    }
    public Task SaveCatalogAsync(ProviderCatalog catalog, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        catalog.Validate();
        // One row per entity respects the existing 4,000-character preference limit.
        // The writer commits header, sources and models in the same transaction.
        var values = new Dictionary<string, string> { ["AI.ProviderCatalog"] = JsonSerializer.Serialize(catalog.ActiveModelId) };
        foreach (var source in catalog.Sources) values[$"AI.ProviderSource.{source.Id:N}"] = JsonSerializer.Serialize(source);
        foreach (var model in catalog.Models) values[$"AI.ProviderModel.{model.Id:N}"] = JsonSerializer.Serialize(model);
        var existing = await db.AppSettings.Where(x => x.Key == "AI.ProviderCatalog" || x.Key.StartsWith("AI.ProviderSource.") || x.Key.StartsWith("AI.ProviderModel.")).ToListAsync(ct);
        foreach (var item in existing)
        { if (values.Remove(item.Key, out var value)) item.SetValue(value); else db.AppSettings.Remove(item); }
        foreach (var pair in values) db.AppSettings.Add(new(pair.Key, pair.Value));
        return true;
    }, ct);
    public Task SaveSettingsAsync(AISettings settings, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        settings.Validate(); var item = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == "AI.Settings", ct); var json = JsonSerializer.Serialize(settings);
        if (item is null) db.AppSettings.Add(new("AI.Settings", json)); else item.SetValue(json); return true;
    }, ct);
    public async Task<IReadOnlyList<PromptTemplate>> TemplatesAsync(CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.PromptTemplates.AsNoTracking().OrderBy(x => x.AnalysisType).ThenByDescending(x => x.IsBuiltIn).ToListAsync(ct); }
    public Task EditTemplateAsync(Guid id, int revision, string name, string body, CancellationToken ct = default) => writer.ExecuteAsync(async db => { (await db.PromptTemplates.SingleAsync(x => x.Id == id, ct)).Edit(name, body, revision); return true; }, ct);
    public Task<Guid> CopyTemplateAsync(Guid id, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    { var original = await db.PromptTemplates.SingleAsync(x => x.Id == id, ct); var copy = new PromptTemplate(original.Name[..Math.Min(original.Name.Length, 140)] + " · 副本", original.AnalysisType, original.Body); db.PromptTemplates.Add(copy); return copy.Id; }, ct);
    public Task RestoreTemplateAsync(Guid id, int revision, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    { var template = await db.PromptTemplates.SingleAsync(x => x.Id == id, ct); template.Edit(template.Name, PromptCatalog.Body(template.AnalysisType), revision); return true; }, ct);
    public Task<Guid> BeginAsync(AIAnalysis analysis, CancellationToken ct = default) => writer.ExecuteAsync(db => { db.AIAnalyses.Add(analysis); return Task.FromResult(analysis.Id); }, ct);
    public Task FinishAsync(Guid id, AIAnalysisStatus status, string raw, string structured, string error, AIProviderResult? response = null, CancellationToken ct = default)
        => writer.ExecuteAsync(async db => { (await db.AIAnalyses.SingleAsync(x => x.Id == id, ct)).Finish(status, raw, structured, error, response?.Envelope ?? "", response?.ResponseId ?? "", response?.Model ?? ""); return true; }, ct);
    public async Task<IReadOnlyList<AIAnalysis>> AnalysesAsync(Guid? securityId, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); var rows = await db.AIAnalyses.AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct); return rows.OrderByDescending(x => x.CreatedAt).ToArray(); }
    public Task<Guid> BeginReportAsync(Guid securityId, string title, CancellationToken ct = default) => writer.ExecuteAsync(db => { var report = new AIResearchReport(securityId, title); db.AIResearchReports.Add(report); return Task.FromResult(report.Id); }, ct);
    public Task UpdateReportAsync(Guid id, IReadOnlyList<Guid> analyses, string markdown, int completed, AIAnalysisStatus status, CancellationToken ct = default)
        => writer.ExecuteAsync(async db => { (await db.AIResearchReports.SingleAsync(x => x.Id == id, ct)).Update(JsonSerializer.Serialize(analyses), markdown, completed, status); return true; }, ct);
    public async Task<IReadOnlyList<AIResearchReport>> ReportsAsync(Guid securityId, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); return (await db.AIResearchReports.AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).ToArray(); }
}
