using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class PortfolioRiskStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IPortfolioRiskStore
{
    public Task InitializeDefaultsAsync(CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        var defaults = new List<RiskLabel>();
        defaults.AddRange(Enum.GetNames<Market>().Select(x => new RiskLabel(RiskTagCategory.Market, x)));
        defaults.AddRange(new[] { "CNY", "HKD", "USD" }.Select(x => new RiskLabel(RiskTagCategory.Currency, x)));
        defaults.Add(new(RiskTagCategory.Sector, "Technology"));
        defaults.AddRange(new[] { "Growth", "Value", "Cyclical", "Defensive", "Semiconductor", "AI Capex", "InterestRateSensitive", "Commodity", "HighValuation" }.Select(x => new RiskLabel(RiskTagCategory.Theme, x)));
        var existing = await db.RiskTags.ToListAsync(ct);
        foreach (var tag in defaults.Where(x => !existing.Any(t => t.Category == x.Category && t.NormalizedName == PortfolioRiskRules.Normalize(x.Name)))) db.RiskTags.Add(new(tag.Name, tag.Category, true));
        return true;
    }, ct);
    private static RiskTagDto Map(RiskTag x) => new(x.Id, x.Name, x.Category, x.IsBuiltIn);
    public async Task<IReadOnlyList<RiskTagDto>> TagsAsync(CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); return (await db.RiskTags.OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync(ct)).Select(Map).ToArray(); }
    public async Task<IReadOnlyList<RiskTagDto>> SecurityTagsAsync(Guid securityId, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); return (await db.SecurityRiskTags.Include(x => x.RiskTag).Where(x => x.SecurityId == securityId).ToListAsync(ct)).Select(x => Map(x.RiskTag)).OrderBy(x => x.Category).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray(); }
    public Task<Guid> CreateTagAsync(string name, RiskTagCategory category, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        var tag = new RiskTag(name, category);
        if (await db.RiskTags.AnyAsync(x => x.Category == category && x.NormalizedName == tag.NormalizedName, ct)) throw new BusinessException("同一分类已存在该标签（忽略大小写）。");
        db.RiskTags.Add(tag); return tag.Id;
    }, ct);
    public Task SetTagAsync(Guid securityId, Guid tagId, bool assigned, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        if (!await db.Securities.AnyAsync(x => x.Id == securityId, ct) || !await db.RiskTags.AnyAsync(x => x.Id == tagId, ct)) throw new BusinessException("证券或标签已不存在。");
        var link = await db.SecurityRiskTags.SingleOrDefaultAsync(x => x.SecurityId == securityId && x.RiskTagId == tagId, ct);
        if (assigned && link is null) db.SecurityRiskTags.Add(new(securityId, tagId)); else if (!assigned && link is not null) db.SecurityRiskTags.Remove(link);
        return true;
    }, ct);
    public Task SetNearMaxRatioAsync(decimal value, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        PortfolioRiskRules.CheckMaximum(0, 1, value); var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.Key == "Risk.NearMaxRatio", ct);
        var text = JsonSerializer.Serialize(value); if (setting is null) db.AppSettings.Add(new("Risk.NearMaxRatio", text)); else setting.SetValue(text); return true;
    }, ct);
    public async Task<PortfolioRiskSnapshot> ReadAsync(Guid accountId, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); await using var read = await db.Database.BeginTransactionAsync(ct); return await Read(db, accountId, ct); }
    public async Task<DecisionPreview> PreviewAsync(Guid accountId, Guid securityId, CancellationToken ct = default)
    { await using var db = await factory.CreateDbContextAsync(ct); await using var read = await db.Database.BeginTransactionAsync(ct); var context = await Context(db, accountId, securityId, ct); return new(context, Fingerprint(context)); }
    public Task<Guid> SaveDecisionAsync(DecisionDraft draft, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        DecisionRules.Validate(draft.Kind, draft.Choice, draft.Answers, draft.Reason);
        var context = await Context(db, draft.AccountId, draft.SecurityId, ct);
        if (Fingerprint(context) != draft.ExpectedFingerprint) throw new BusinessException("持仓、风险或逻辑已变化。本次未保存，请关闭清单、刷新后重新核对。");
        if (draft.Sizing is { } sizing) PortfolioRiskRules.Size(sizing, context.MaxWeight, context.Portfolio.NearMaxRatio);
        var record = new DecisionRecord(draft.AccountId, draft.SecurityId, draft.Kind, draft.Choice, draft.Answers, draft.Reason, JsonSerializer.Serialize(context), draft.Sizing);
        db.DecisionRecords.Add(record); return record.Id;
    }, ct);
    public async Task<IReadOnlyList<DecisionRecordDto>> DecisionsAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var records = await db.DecisionRecords.AsNoTracking().Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct);
        return records.OrderByDescending(x => x.CreatedAt).Select(x => new DecisionRecordDto(x.Id, x.CreatedAt, x.Kind, x.Choice, x.Reason,
            JsonSerializer.Deserialize<DecisionAnswer[]>(x.AnswersJson)!, ReadContext(x.ContextJson), x.SizingJson is null ? null : JsonSerializer.Deserialize<SizingInputs>(x.SizingJson))).ToArray();
    }
    private static DecisionContext ReadContext(string json)
    {
        var context = JsonSerializer.Deserialize<DecisionContext>(json);
        return context is { SchemaVersion: 1 } ? context : throw new BusinessException("决策快照版本不受支持，请使用兼容版本读取。");
    }
    private static string Fingerprint(DecisionContext context) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context))));
    private static async Task<PortfolioRiskSnapshot> Read(InvestmentDbContext db, Guid accountId, CancellationToken ct)
    {
        var account = await db.PortfolioAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId, ct) ?? throw new BusinessException("请先创建并选择账户。");
        var positions = (await db.Positions.AsNoTracking().Include(x => x.Security).Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct)).Where(x => x.Quantity > 0).ToArray();
        if (positions.Any(x => x.Security.Currency != account.Currency)) throw new BusinessException("组合存在异币种仓位，当前不支持无汇率的跨币种汇总。");
        var ids = positions.Select(x => x.SecurityId).ToArray();
        var tags = await db.SecurityRiskTags.AsNoTracking().Include(x => x.RiskTag).Where(x => ids.Contains(x.SecurityId)).ToListAsync(ct);
        var total = account.CurrentCash + positions.Sum(x => x.Quantity * (x.Security.LatestPrice ?? x.AverageCost));
        var near = await db.AppSettings.Where(x => x.Key == "Risk.NearMaxRatio").Select(x => x.Value).SingleOrDefaultAsync(ct);
        var nearRatio = near is null ? .9m : JsonSerializer.Deserialize<decimal>(near); PortfolioRiskRules.CheckMaximum(0, 1, nearRatio);
        var rows = positions.Select(p =>
        {
            var labels = EffectiveLabels(p.Security, tags.Where(x => x.SecurityId == p.SecurityId));
            var value = p.Quantity * (p.Security.LatestPrice ?? p.AverageCost);
            return new RiskPosition(p.SecurityId, $"{p.Security.Symbol} · {p.Security.Name}", value, total == 0 ? 0 : value / total, p.MaxWeight, p.Security.LatestPrice is null, p.Security.PriceDate,
                labels, p.Security.Type);
        }).OrderByDescending(x => x.Weight).ThenBy(x => x.SecurityId).ToArray();
        var theses = await Theses(db, ids, ct); var accounting = await Accounting(db, ids, ct);
        return new(account.Id, account.Name, account.Currency, total, account.CurrentCash, nearRatio, rows,
            PortfolioRiskRules.Aggregate(rows, total == 0 ? 0 : account.CurrentCash / total, account.Currency), theses, accounting.Findings, accounting.Unassessed);
    }
    private static async Task<DecisionContext> Context(InvestmentDbContext db, Guid accountId, Guid securityId, CancellationToken ct)
    {
        var portfolio = await Read(db, accountId, ct); var security = await db.Securities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == securityId, ct) ?? throw new BusinessException("请选择证券。");
        var holding = portfolio.Positions.SingleOrDefault(x => x.SecurityId == securityId);
        var open = await Theses(db, [securityId], ct);
        var latest = (await db.Theses.AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct)).OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).FirstOrDefault();
        var accounting = await Accounting(db, [securityId], ct);
        var maximum = holding?.MaxWeight ?? await db.PortfolioAccounts.Where(x => x.Id == accountId).Select(x => x.DefaultMaxPositionWeight).SingleAsync(ct);
        var tags = await db.SecurityRiskTags.AsNoTracking().Include(x => x.RiskTag).Where(x => x.SecurityId == securityId).ToListAsync(ct);
        return new(portfolio, security.Id, $"{security.Symbol} · {security.Name}", holding?.Weight ?? 0, maximum, open.SingleOrDefault(x => x.IsCurrent), latest?.Status, open, accounting.Findings, accounting.Unassessed, EffectiveLabels(security, tags));
    }
    private static IReadOnlyList<RiskLabel> EffectiveLabels(Security security, IEnumerable<SecurityRiskTag> tags)
    {
        var labels = tags.Select(x => new RiskLabel(x.RiskTag.Category, x.RiskTag.Name)).ToList();
        void Fallback(RiskTagCategory category, string name) { if (!labels.Any(x => x.Category == category)) labels.Add(new(category, name)); }
        Fallback(RiskTagCategory.Sector, string.IsNullOrWhiteSpace(security.Sector) ? "未分类" : security.Sector);
        Fallback(RiskTagCategory.Market, security.Market.ToString()); Fallback(RiskTagCategory.Currency, security.Currency);
        return labels.OrderBy(x => x.Category).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
    }
    private static async Task<IReadOnlyList<ThesisRiskState>> Theses(InvestmentDbContext db, Guid[] ids, CancellationToken ct)
    {
        var theses = await db.Theses.AsNoTracking().Include(x => x.Security).Include(x => x.KillConditions).Where(x => ids.Contains(x.SecurityId) && !x.Archived && x.Status != ThesisStatus.Invalidated && x.Status != ThesisStatus.Closed).ToListAsync(ct);
        return theses.OrderBy(x => x.SecurityId).ThenBy(x => x.Id).Select(x => new ThesisRiskState(x.Id, x.SecurityId, x.Security.Symbol, x.Title, x.Version, x.Status, x.IsCurrent, x.KillConditions.Count,
            x.KillConditions.Count(k => !k.CurrentValue.HasValue), x.KillConditions.OrderBy(k => k.Id).Select(k => new KillRiskState(k.Title, k.Status, k.CurrentValue, k.Evidence)).ToArray())).ToArray();
    }
    private static async Task<(IReadOnlyList<AccountingAttention> Findings, int Unassessed)> Accounting(InvestmentDbContext db, Guid[] ids, CancellationToken ct)
    {
        var metrics = await db.FinancialMetrics.AsNoTracking().Include(x => x.Security).Where(x => ids.Contains(x.SecurityId)).ToListAsync(ct);
        var json = await db.AppSettings.Where(x => x.Key == "Research.RiskThresholds").Select(x => x.Value).SingleOrDefaultAsync(ct);
        var thresholds = json is null ? new RiskThresholds() : JsonSerializer.Deserialize<RiskThresholds>(json)!;
        var output = new List<AccountingAttention>(); var unknown = 0;
        foreach (var id in ids.Order())
        {
            var groups = metrics.Where(x => x.SecurityId == id && !x.IsEstimated).GroupBy(x => (x.PeriodType, x.Currency)).OrderBy(x => x.Key.PeriodType).ThenBy(x => x.Key.Currency, StringComparer.Ordinal).ToArray();
            if (groups.Length == 0) { unknown += 7; continue; }
            foreach (var group in groups)
            {
                var findings = AccountingRiskEngine.Evaluate(group.ToArray(), group.Key.PeriodType, group.Key.Currency, thresholds); unknown += findings.Count(x => !x.Assessed);
                output.AddRange(findings.Where(x => x.Assessed && x.Level == AttentionLevel.HighAttention).Select(x => new AccountingAttention(id, group.First().Security.Symbol, group.Key.PeriodType, group.Key.Currency, x.Rule, x.Evidence)));
            }
        }
        return (output, unknown);
    }
}
