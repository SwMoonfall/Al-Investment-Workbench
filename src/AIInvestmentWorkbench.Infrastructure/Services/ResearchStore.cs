using System.Text.Json;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class ResearchStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IResearchStore
{
    private const string ThresholdKey = "Research.RiskThresholds";
    public async Task<ResearchSnapshot> ReadAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var read = await db.Database.BeginTransactionAsync(ct);
        await RequireSecurity(db, securityId, ct);
        var research = await db.CompanyResearches.AsNoTracking().SingleOrDefaultAsync(x => x.SecurityId == securityId, ct);
        var metrics = await db.FinancialMetrics.AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct);
        var sources = await db.ResearchSources.AsNoTracking().Where(x => x.SecurityId == securityId).OrderBy(x => x.Title).ToListAsync(ct);
        var scores = await db.ResearchScores.AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct);
        foreach (var dimension in Enum.GetValues<ScoreDimension>()) if (scores.All(x => x.Dimension != dimension)) scores.Add(new ResearchScore(securityId, dimension));
        return new(research, metrics, sources, scores.OrderBy(x => x.Dimension).ToList());
    }
    private static async Task RequireSecurity(InvestmentDbContext db, Guid id, CancellationToken ct)
    { if (!await db.Securities.AnyAsync(x => x.Id == id, ct)) throw new BusinessException("请选择仍然存在的证券。"); }
    public async Task SaveResearchAsync(Guid securityId, ResearchContent content, int revision, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            await RequireSecurity(db, securityId, ct);
            var entity = await db.CompanyResearches.SingleOrDefaultAsync(x => x.SecurityId == securityId, ct);
            if (entity is null) { entity = new(securityId); db.CompanyResearches.Add(entity); }
            entity.Update(content, revision); return true;
        }, ct);
    }
    public async Task SaveMetricAsync(MetricDraft draft, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db => { await PutMetric(db, draft, ct); return true; }, ct);
    private static async Task PutMetric(InvestmentDbContext db, MetricDraft d, CancellationToken ct)
    {
        await RequireSecurity(db, d.SecurityId, ct);
        var validated = new FinancialMetric(d.SecurityId, d.Period, d.PeriodType, d.MetricType, d.Value, d.Currency, d.SourceId, d.IsEstimated, d.Notes);
        if (d.SourceId.HasValue && !await db.ResearchSources.AnyAsync(x => x.Id == d.SourceId && x.SecurityId == d.SecurityId, ct)) throw new BusinessException("来源不存在或属于另一只证券。");
        if (await db.FinancialMetrics.AnyAsync(x => x.Id != d.Id && x.SecurityId == d.SecurityId && x.Period == validated.Period && x.PeriodType == d.PeriodType && x.MetricType == d.MetricType && x.Currency == validated.Currency, ct)
            || db.FinancialMetrics.Local.Any(x => x.Id != d.Id && x.SecurityId == d.SecurityId && x.Period == validated.Period && x.PeriodType == d.PeriodType && x.MetricType == d.MetricType && x.Currency == validated.Currency))
            throw new BusinessException("同证券、期间、指标、币种已有记录，请编辑现有记录；导入不会覆盖。");
        if (d.Id is null) db.FinancialMetrics.Add(validated);
        else
        {
            var current = await db.FinancialMetrics.SingleOrDefaultAsync(x => x.Id == d.Id && x.SecurityId == d.SecurityId, ct) ?? throw new BusinessException("指标已不存在。");
            current.Edit(d.Period, d.PeriodType, d.MetricType, d.Value, d.Currency, d.SourceId, d.IsEstimated, d.Notes);
        }
    }
    public async Task DeleteMetricAsync(Guid id, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db => { db.FinancialMetrics.Remove(await db.FinancialMetrics.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("指标已不存在。")); return true; }, ct);
    public async Task<ImportOutcome> ImportMetricsAsync(IReadOnlyList<MetricDraft> drafts, bool validateOnly, CancellationToken ct = default)
    {
        if (drafts.Count is < 1 or > 10000) return new(false, 0, ["导入需要 1 至 10000 行。"]);
        try
        {
            return await writer.ExecuteAsync(async db =>
            {
                for (var i = 0; i < drafts.Count; i++)
                {
                    try
                    {
                        if (drafts[i].Id.HasValue) throw new BusinessException("CSV 只支持新增指标。");
                        await PutMetric(db, drafts[i], ct);
                    }
                    catch (Exception ex) when (ex is BusinessException or ArgumentException)
                    { throw new BusinessException($"第 {i + 2} 行：{ex.Message} 整批未写入。"); }
                }
                return new ImportOutcome(true, drafts.Count, []);
            }, ct, validateOnly);
        }
        catch (BusinessException ex) { return new(false, 0, [ex.Message]); }
    }
    public async Task<Guid> SaveSourceAsync(SourceDraft d, CancellationToken ct = default)
    {
        return await writer.ExecuteAsync(async db =>
        {
            await RequireSecurity(db, d.SecurityId, ct);
            var entity = d.Id.HasValue ? await db.ResearchSources.SingleOrDefaultAsync(x => x.Id == d.Id && x.SecurityId == d.SecurityId, ct) ?? throw new BusinessException("来源已不存在。") : null;
            if (entity is null) { entity = new(d.SecurityId, d.Title, d.Publisher, d.PublishedDate, d.Url, d.LocalFilePath, d.SourceType, d.ReliabilityLevel, d.Notes, d.ExtractedText); db.ResearchSources.Add(entity); }
            else entity.Edit(d.Title, d.Publisher, d.PublishedDate, d.Url, d.LocalFilePath, d.SourceType, d.ReliabilityLevel, d.Notes, d.ExtractedText);
            return entity.Id;
        }, ct);
    }
    public async Task DeleteSourceAsync(Guid id, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            if (await db.FinancialMetrics.AnyAsync(x => x.SourceId == id, ct)) throw new BusinessException("来源仍被财务指标引用，请先更换或清除指标来源。");
            db.ResearchSources.Remove(await db.ResearchSources.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("来源已不存在。")); return true;
        }, ct);
    }
    public async Task SaveScoresAsync(Guid securityId, IReadOnlyList<ScoreDraft> drafts, CancellationToken ct = default)
    {
        if (drafts.Count != 7 || drafts.Select(x => x.Dimension).Distinct().Count() != 7 || drafts.Sum(x => x.Weight) != 100)
            throw new BusinessException("请提供全部 7 个评分维度，权重合计必须为 100%。");
        await writer.ExecuteAsync(async db =>
        {
            await RequireSecurity(db, securityId, ct);
            var scores = await db.ResearchScores.Where(x => x.SecurityId == securityId).ToListAsync(ct);
            foreach (var d in drafts)
            {
                var entity = scores.SingleOrDefault(x => x.Dimension == d.Dimension);
                if (entity is null) { entity = new(securityId, d.Dimension); db.ResearchScores.Add(entity); }
                entity.Edit(d.Weight, d.UserScore, d.Reason, d.Evidence);
            }
            return true;
        }, ct);
    }
    public async Task<RiskThresholds> ReadThresholdsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = await db.AppSettings.Where(x => x.Key == ThresholdKey).Select(x => x.Value).SingleOrDefaultAsync(ct);
        var value = json is null ? new() : JsonSerializer.Deserialize<RiskThresholds>(json) ?? new RiskThresholds();
        value.Validate(); return value;
    }
    public async Task SaveThresholdsAsync(RiskThresholds thresholds, CancellationToken ct = default)
    {
        thresholds.Validate();
        await writer.ExecuteAsync(async db =>
        {
            var value = JsonSerializer.Serialize(thresholds);
            var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == ThresholdKey, ct);
            if (setting is null) db.AppSettings.Add(new(ThresholdKey, value)); else setting.SetValue(value);
            return true;
        }, ct);
    }
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length < 2) throw new BusinessException("请输入至少两个字符。");
        await using var db = await factory.CreateDbContextAsync(ct);
        var securities = await db.Securities.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var hits = new List<SearchHit>();
        void Add(string kind, Guid id, string title, string content)
        {
            var security = securities[id]; var label = $"{security.Symbol} · {security.Name}";
            if ((title + " " + content + " " + label).Contains(query, StringComparison.OrdinalIgnoreCase)) hits.Add(new(kind, id, label, title, content));
        }
        foreach (var s in securities.Values) Add("Security / Notes", s.Id, s.Name, string.Join("\n", s.Symbol, s.Exchange, s.Sector, s.Industry, s.Country, s.ISIN, s.Notes));
        foreach (var r in await db.CompanyResearches.AsNoTracking().ToListAsync(ct))
            foreach (var p in typeof(ResearchContent).GetProperties()) Add("Research / Notes", r.SecurityId, p.Name, (string)p.GetValue(r.Content)!);
        foreach (var r in await db.ResearchSources.AsNoTracking().ToListAsync(ct)) Add("ResearchSource", r.SecurityId, r.Title, string.Join("\n", r.Publisher, r.Url, r.LocalFilePath, r.Notes, r.ExtractedText));
        foreach (var r in await db.WatchlistItems.AsNoTracking().ToListAsync(ct)) Add("Watchlist", r.SecurityId, r.Stage.ToString(), string.Join("\n", r.Reason, r.NextAction, r.Note));
        foreach (var r in await db.FinancialMetrics.AsNoTracking().Where(x => x.Notes != "").ToListAsync(ct)) Add("Financial Notes", r.SecurityId, $"{r.Period} {r.MetricType}", r.Notes);
        foreach (var r in await db.ResearchScores.AsNoTracking().ToListAsync(ct)) Add("Score / Notes", r.SecurityId, r.Dimension.ToString(), r.Reason + "\n" + r.Evidence);
        return hits;
    }
}
