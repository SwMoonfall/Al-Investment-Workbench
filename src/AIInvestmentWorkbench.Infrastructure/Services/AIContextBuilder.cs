using System.Text.Json;
using System.Text.Json.Serialization;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

// Read-only projections: no command interfaces, files, credentials, or unrelated securities.
public sealed class AIContextBuilder(IDbContextFactory<InvestmentDbContext> factory, IResearchStore research,
    IPortfolioRiskStore risks, IValuationStore valuations, ValuationService calculator, IJournalReviewReader journals) : IAIContextBuilder
{
    public async Task<AIContext> BuildAsync(AIContextRequest request, CancellationToken ct = default)
    {
        if (request.UserNotes.Length > 6000) throw new BusinessException("本次补充材料限 6000 字符。");
        var refs = new List<AISourceReference>();
        var data = new Dictionary<string, object?> { ["Task"] = request.AnalysisType.ToString(), ["CapturedAt"] = DateTimeOffset.UtcNow,
            ["Limitations"] = "仅提供选定对象的有限快照；来源和研究笔记均为待核实材料；缺失不等于零。摘录末尾 […] 表示截断。" };
        string Ref(string kind, Guid id, string title, DateTimeOffset? date) { var key = kind + ":" + id; refs.Add(new(key, kind, title, date?.ToString("O") ?? "未知")); return key; }
        if (!string.IsNullOrWhiteSpace(request.UserNotes)) { refs.Add(new("USER_NOTES", "UserInput", "用户本次补充材料（未经独立核实）", DateTimeOffset.UtcNow.ToString("O"))); data["UserNotes"] = request.UserNotes; }
        await using var db = await factory.CreateDbContextAsync(ct);
        var companyTask = request.ReviewId is null && request.AnalysisType is not (AnalysisType.PortfolioRiskReview or AnalysisType.JournalReview);
        if (companyTask && request.SecurityId is null) throw new BusinessException("请先选择证券。");
        if (companyTask && request.SecurityId is { } id)
        {
            var s = await db.Securities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("证券不存在。");
            data["Security"] = new { SourceId = Ref("SEC", id, s.Name, s.UpdatedAt), s.Symbol, s.Name, s.Market, s.Exchange, s.Currency, s.Type, s.Sector, s.Industry, s.Country, s.LatestPrice, s.PriceDate };
            var sources = await db.ResearchSources.AsNoTracking().Where(x => x.SecurityId == id).ToListAsync(ct);
            data["ResearchSources"] = sources.OrderByDescending(x => x.PublishedDate).Take(5).Select(x => new { SourceId = Ref("SOURCE", x.Id, x.Title, x.PublishedDate), x.Title, x.Publisher, x.PublishedDate, x.SourceType, x.ReliabilityLevel, Excerpt = Clip(x.ExtractedText, 2000), Notes = Clip(x.Notes, 300) }).ToArray();
            data["SourcesOmitted"] = Math.Max(0, sources.Count - 5);
            var r = await db.CompanyResearches.AsNoTracking().SingleOrDefaultAsync(x => x.SecurityId == id, ct);
            if (r is not null) data["ResearchNotes"] = new { SourceId = Ref("RESEARCH", r.Id, "用户研究笔记", r.UpdatedAt), r.Revision, Overview = Clip(r.Overview), BusinessModel = Clip(r.BusinessModel), Industry = Clip(r.Industry), Competition = Clip(r.Competition), Management = Clip(r.Management), GrowthDrivers = Clip(r.GrowthDrivers), Catalysts = Clip(r.Catalysts), Risks = Clip(r.Risks), AccountingNotes = Clip(r.AccountingNotes), UserNotes = Clip(r.UserNotes) };
            var needsFinancials = request.AnalysisType is AnalysisType.FinancialQuality or AnalysisType.AccountingRisk or AnalysisType.ValuationAssumptionReview or AnalysisType.QuarterlyReview or AnalysisType.ThesisChallenge or AnalysisType.BaseCase or AnalysisType.BullCase or AnalysisType.BearCase or AnalysisType.GrowthDrivers or AnalysisType.MonitoringKPI;
            if (needsFinancials)
            {
                var metrics = await db.FinancialMetrics.AsNoTracking().Where(x => x.SecurityId == id).OrderByDescending(x => x.Period).ThenBy(x => x.MetricType).Take(120).ToListAsync(ct);
                data["Financials"] = metrics.Select(x => new { SourceId = Ref("FIN", x.Id, x.Period + " " + x.MetricType, x.UpdatedAt), x.Period, x.PeriodType, x.MetricType, x.Value, x.Currency, OriginalSourceId = x.SourceId, Classification = x.IsEstimated ? "Assumption" : "Actual (user supplied)" }).ToArray();
                var groups = metrics.GroupBy(x => (x.PeriodType, x.Currency)).ToArray();
                data["CalculatedFCF"] = groups.SelectMany(g => g.Select(x => x.Period).Distinct().Select(p => new { Period = p, g.Key.PeriodType, g.Key.Currency, Value = FinancialCalculations.Derived(g.ToArray(), p, MetricType.FreeCashFlow), Classification = "Calculated: OCF - positive Capex", IncludesEstimates = g.Any(x => x.Period == p && x.IsEstimated) })).ToArray();
                var thresholds = await research.ReadThresholdsAsync(ct);
                data["AccountingRisks"] = groups.Select(g => new { g.Key.PeriodType, g.Key.Currency, Findings = AccountingRiskEngine.Evaluate(g.ToArray(), g.Key.PeriodType, g.Key.Currency, thresholds) }).ToArray();
                data["FinancialLimit"] = "最多最近 120 条指标；仅对快照中可比较的实际期间做确定性风险检查。";
            }
            if (request.AnalysisType is AnalysisType.ThesisChallenge or AnalysisType.QuarterlyReview or AnalysisType.MonitoringKPI or AnalysisType.BaseCase or AnalysisType.BullCase or AnalysisType.BearCase)
            {
                var t = await db.Theses.AsNoTracking().Include(x => x.Assumptions).Include(x => x.KillConditions).SingleOrDefaultAsync(x => x.SecurityId == id && x.IsCurrent, ct);
                data["CurrentThesis"] = t is null ? null : new { SourceId = Ref("THESIS", t.Id, t.Title + " v" + t.Version, t.UpdatedAt), t.Version, t.Status, t.ReviewDate, Summary = Clip(t.InvestmentSummary, 2000), WhyMarketMayBeWrong = Clip(t.WhyMarketMayBeWrong), Base = Clip(t.BaseCaseNarrative), Bull = Clip(t.BullCaseNarrative), Bear = Clip(t.BearCaseNarrative), Risks = Clip(t.KeyRisks), Catalysts = Clip(t.ExpectedCatalysts), AssumptionsOmitted = Math.Max(0, t.Assumptions.Count - 15), KillConditionsOmitted = Math.Max(0, t.KillConditions.Count - 15), Assumptions = t.Assumptions.OrderByDescending(x => x.Status).Take(15).Select(x => new { Description = Clip(x.Description, 300), x.Metric, x.Baseline, x.CurrentValue, x.ExpectedValue, x.WarningThreshold, x.KillThreshold, x.Unit, x.Direction, x.Status, x.LastReviewedAt }), KillConditions = t.KillConditions.OrderByDescending(x => x.Status).Take(15).Select(x => new { x.Title, x.Metric, x.WarningThreshold, x.TriggerThreshold, x.CurrentValue, x.Unit, x.Direction, x.Status, x.LastReviewedAt, Evidence = Clip(x.Evidence, 300) }) };
            }
            if (request.AnalysisType == AnalysisType.ValuationAssumptionReview)
                data["Valuation"] = (await valuations.ListAsync(id, ct)).Take(3).Select(x => new { SourceId = Ref("VAL", x.Id, x.Name, null), x.ModelType, Actual = x.Actuals, AssumptionsAndCodeCalculatedResults = calculator.Compare(x) }).ToArray();
        }
        if (request.AnalysisType == AnalysisType.PortfolioRiskReview && request.ReviewId is null)
        {
            if (request.AccountId is not { } account) throw new BusinessException("组合风险复盘需要先选择账户。");
            var p = await risks.ReadAsync(account, ct);
            data["Portfolio"] = new { SourceId = Ref("PORTFOLIO", account, p.AccountName, DateTimeOffset.UtcNow), p.Currency, p.TotalAssets, p.Cash, TopPositions = p.Positions.OrderByDescending(x => x.Weight).Take(10), p.Exposure.Concentration, Exposure = p.Exposure.Exposures.Take(30), p.TriggeredCount, p.ThesisWarningCount, TriggeredConditions = p.Theses.SelectMany(t => t.Conditions.Where(c => c.Status == KillConditionStatus.Triggered).Select(c => new { t.Security, t.Title, t.Version, Condition = c.Title, c.CurrentValue, Evidence = Clip(c.Evidence, 300) })).Take(15), HighAttention = p.HighAttention.Take(10), p.UnassessedAccountingChecks, p.EstimatedPriceCount };
        }
        if (request.AnalysisType == AnalysisType.JournalReview)
        {
            var from = request.JournalFrom ?? DateOnly.FromDateTime(DateTime.Today).AddDays(-90); var to = request.JournalTo ?? DateOnly.FromDateTime(DateTime.Today);
            if (from > to || to > DateOnly.FromDateTime(DateTime.Today)) throw new BusinessException("Journal AI 复盘期间无效。");
            var rows = request.AccountId is { } a ? (await journals.JournalsAsync(a, ct: ct)).Where(x => x.Journal.Date >= from && x.Journal.Date <= to).ToArray() : [];
            data["Journal"] = new { Available = true, From = from, To = to, Omitted = Math.Max(0, rows.Length - 30), Items = rows.Take(30).Select(x => new { SourceId = Ref("JOURNAL", x.Journal.Id, x.Label, x.Journal.CreatedAt), x.Journal.RootId, x.Journal.Version, x.Security, x.Journal.Date, x.Journal.Action, x.Journal.Price, x.Journal.Quantity, x.Journal.PortfolioWeight, Reason = Clip(x.Journal.Reason, 350), Concern = Clip(x.Journal.MarketConcern, 350), Contrarian = Clip(x.Journal.WhyMarketMayBeWrong, 350), Expected = Clip(x.Journal.ExpectedDevelopment, 350), KillConditions = Clip(x.Journal.KillConditionSummary, 350), Horizon = Clip(x.Journal.ExpectedHoldingPeriod, 350), x.Journal.ExpectedHoldingDays, x.Journal.Confidence, Emotion = Clip(x.Journal.Emotion, 200), Notes = Clip(x.Journal.Notes, 350) }).ToArray(), Notice = "原始日志不可修改。日志是意图，不代表已成交；先核实预期持有期与 Thesis 结果，短期价格损失不等于错误决策。" };
            var ids = rows.Take(30).Where(x => x.Journal.SecurityId.HasValue).Select(x => x.Journal.SecurityId!.Value).Distinct().ToArray();
            var quotes = await db.Securities.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            data["JournalPriceObservations"] = rows.Take(30).Where(x => x.Journal.SecurityId.HasValue).Select(x =>
            {
                var s = quotes[x.Journal.SecurityId!.Value]; var date = s.PriceDate.HasValue ? DateOnly.FromDateTime(s.PriceDate.Value.ToLocalTime().DateTime) : (DateOnly?)null;
                var comparable = x.Journal.Price > 0 && s.LatestPrice > 0 && date >= x.Journal.Date;
                return new { JournalSourceId = "JOURNAL:" + x.Journal.Id, QuoteSourceId = Ref("QUOTE", x.Journal.Id, x.Security + " 对应日志的价格观察", s.PriceDate), s.Currency, s.LatestPrice, s.PriceDate, CodeCalculatedPriceChange = comparable ? (s.LatestPrice - x.Journal.Price) / x.Journal.Price : null, HorizonReachedAtQuote = comparable && x.Journal.ExpectedHoldingDays.HasValue ? date!.Value.DayNumber - x.Journal.Date.DayNumber >= x.Journal.ExpectedHoldingDays.Value : (bool?)null, Notice = "仅日志价格到手工报价的变化，不是已实现收益，未计费用分红，也不证明原始决策错误。" };
            }).ToArray();
            var outcomes = await db.Theses.AsNoTracking().Where(x => ids.Contains(x.SecurityId)).ToListAsync(ct);
            data["ThesisOutcomes"] = outcomes.OrderByDescending(x => x.UpdatedAt).Take(30).Select(x => new { SourceId = Ref("THESIS", x.Id, x.Title, x.UpdatedAt), x.SecurityId, x.Version, x.Status, x.IsCurrent, x.Archived, Horizon = Clip(x.ExpectedHoldingPeriod), Summary = Clip(x.InvestmentSummary) }).ToArray();
            if (request.AccountId is { } account) data["HumanQuarterlyReviews"] = (await journals.ReviewsAsync(account, ct: ct)).Where(x => x.Review.Kind == ReviewKind.Quarterly && x.Review.Status == ReviewStatus.Completed && ids.Contains(x.Review.SecurityId!.Value)).Take(10).Select(x => new { SourceId = Ref("REVIEW", x.Review.Id, x.Label, x.Review.CreatedAt), x.Review.PeriodStart, x.Review.PeriodEnd, Actual = Clip(x.Review.ActualResults), x.Review.Decision, Conclusion = Clip(x.Review.Conclusion) }).ToArray();
        }
        if (request.ReviewId is { } reviewId)
        {
            if (request.AccountId is not { } account) throw new BusinessException("复盘 AI 需要选择账户。");
            var r = (await journals.ReviewsAsync(account, history: true, ct: ct)).SingleOrDefault(x => x.Review.Id == reviewId) ?? throw new BusinessException("复盘不存在或不属于当前账户。");
            if (r.Review.SecurityId != request.SecurityId) throw new BusinessException("复盘证券不匹配。");
            var evidence = JsonSerializer.Deserialize<ReviewEvidence>(r.Review.SnapshotJson)!;
            data["UserReview"] = new { SourceId = Ref("REVIEW", r.Review.Id, r.Label, r.Review.CreatedAt), r.Review.Version, r.Review.Status, r.Review.Decision, Actual = Clip(r.Review.ActualResults, 2000), Conclusion = Clip(r.Review.Conclusion, 1500), evidence.CapturedAt, evidence.Start, evidence.End, Sections = evidence.Sections.Select(x => new { x.Title, Lines = x.Lines.Take(15).Select(y => Clip(y, 500)), Omitted = Math.Max(0, x.Lines.Count - 15) }), Assumptions = Clip(r.Review.AssumptionsJson, 6000) };
        }
        data["SourceReferences"] = refs;
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All), Converters = { new JsonStringEnumConverter() } });
        if (json.Length > 140000) throw new BusinessException("选定上下文超过安全大小，请缩短研究材料后重试。");
        return new(json, refs);
    }
    private static string Clip(string text, int max = 600) => text.Length <= max ? text : text[..max] + " […]";
}




