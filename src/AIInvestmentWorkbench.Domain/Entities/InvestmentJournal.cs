using System.Text.Json;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public enum JournalAction { Open, Add, Hold, Trim, Exit, ResearchDecision, Other }
public enum ReviewKind { Weekly, Monthly, Quarterly }
public enum ReviewStatus { Draft, Completed }
public enum AssumptionOutcome { Unassessed, Validated, Unchanged, Weakened, Invalidated }
public enum ReviewDecision { Increase, Hold, Reduce, Exit, ContinueResearch }
public sealed record JournalContent(DateOnly Date, JournalAction Action, decimal? Price, decimal? Quantity, decimal? PortfolioWeight,
    string Reason, string MarketConcern = "", string WhyMarketMayBeWrong = "", string ExpectedDevelopment = "", string KillConditionSummary = "",
    string ExpectedHoldingPeriod = "", string Emotion = "", decimal? Confidence = null, string Notes = "", int? ExpectedHoldingDays = null);

// Journal versions are append-only. The actor is enforced in both domain and application commands.
public sealed class InvestmentJournal : Entity
{
    private InvestmentJournal() { }
    public InvestmentJournal(Guid account, Guid? security, JournalContent c, ThesisActor actor, Guid? decision = null, Guid? root = null, int version = 1, string correctionReason = "初始记录")
    {
        ThesisRules.RequireUser(actor); JournalRules.Validate(c);
        PortfolioAccountId = Guard.Id(account, "账户"); if (security == Guid.Empty || decision == Guid.Empty) throw new BusinessException("关联标识无效。");
        SecurityId = security; DecisionRecordId = decision; RootId = root ?? Id; Version = version;
        if (version < 1 || RootId == Guid.Empty) throw new BusinessException("日志版本无效。");
        CorrectionReason = Guard.Text(correctionReason, "更正原因", 2000);
        Date = c.Date; Action = c.Action; Price = c.Price; Quantity = c.Quantity; PortfolioWeight = c.PortfolioWeight;
        Reason = c.Reason.Trim(); MarketConcern = c.MarketConcern.Trim(); WhyMarketMayBeWrong = c.WhyMarketMayBeWrong.Trim(); ExpectedDevelopment = c.ExpectedDevelopment.Trim();
        KillConditionSummary = c.KillConditionSummary.Trim(); ExpectedHoldingPeriod = c.ExpectedHoldingPeriod.Trim(); Emotion = c.Emotion.Trim(); Confidence = c.Confidence; Notes = c.Notes.Trim(); ExpectedHoldingDays = c.ExpectedHoldingDays;
    }
    public Guid PortfolioAccountId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public Guid? DecisionRecordId { get; private set; }
    public Guid RootId { get; private set; }
    public int Version { get; private set; }
    public string CorrectionReason { get; private set; } = "";
    public DateOnly Date { get; private set; }
    public JournalAction Action { get; private set; }
    public decimal? Price { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? PortfolioWeight { get; private set; }
    public string Reason { get; private set; } = "";
    public string MarketConcern { get; private set; } = "";
    public string WhyMarketMayBeWrong { get; private set; } = "";
    public string ExpectedDevelopment { get; private set; } = "";
    public string KillConditionSummary { get; private set; } = "";
    public string ExpectedHoldingPeriod { get; private set; } = "";
    public string Emotion { get; private set; } = "";
    public decimal? Confidence { get; private set; }
    public string Notes { get; private set; } = "";
    public int? ExpectedHoldingDays { get; private set; }
    public JournalContent Content => new(Date, Action, Price, Quantity, PortfolioWeight, Reason, MarketConcern, WhyMarketMayBeWrong, ExpectedDevelopment, KillConditionSummary, ExpectedHoldingPeriod, Emotion, Confidence, Notes, ExpectedHoldingDays);
}
public sealed record AssumptionReview(Guid Id, string Description, AssumptionOutcome Outcome, string Evidence);
public sealed class InvestmentReview : Entity
{
    private InvestmentReview() { }
    public InvestmentReview(Guid account, Guid? security, ReviewKind kind, DateOnly start, DateOnly end, string snapshot,
        string actualResults, IReadOnlyList<AssumptionReview> assumptions, ReviewDecision? decision, string conclusion, ReviewStatus status,
        ThesisActor actor, DateOnly today, Guid? root = null, int version = 1)
    {
        ThesisRules.RequireUser(actor); PortfolioAccountId = Guard.Id(account, "账户");
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(status) || decision.HasValue && !Enum.IsDefined(decision.Value) || version < 1) throw new BusinessException("复盘类型或状态无效。");
        if (start > end || start > today || kind == ReviewKind.Quarterly && security is null || security == Guid.Empty) throw new BusinessException("复盘期间或证券无效。");
        if (assumptions.Select(x => x.Id).Distinct().Count() != assumptions.Count || assumptions.Any(x => !Enum.IsDefined(x.Outcome))) throw new BusinessException("假设评估无效。");
        if (status == ReviewStatus.Completed)
        {
            if (end > today) throw new BusinessException("期间尚未结束，只能保存草稿。");
            Guard.Text(conclusion, "复盘结论", 20000);
            if (kind == ReviewKind.Quarterly && (decision is null || string.IsNullOrWhiteSpace(actualResults) || assumptions.Any(x => x.Outcome == AssumptionOutcome.Unassessed || string.IsNullOrWhiteSpace(x.Evidence)))) throw new BusinessException("完成季度复盘前，请填写实际结果、逐项假设证据和用户决策。");
        }
        SecurityId = security; Kind = kind; PeriodStart = start; PeriodEnd = end; RootId = root ?? Id; Version = version; Status = status;
        SnapshotJson = Guard.Text(snapshot, "复盘快照", 2000000); ActualResults = Guard.OptionalText(actualResults, 20000); AssumptionsJson = JsonSerializer.Serialize(assumptions);
        Decision = decision; Conclusion = Guard.OptionalText(conclusion, 20000);
    }
    public Guid PortfolioAccountId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public Guid RootId { get; private set; }
    public int Version { get; private set; }
    public ReviewKind Kind { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public ReviewStatus Status { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    public string ActualResults { get; private set; } = "";
    public string AssumptionsJson { get; private set; } = "[]";
    public ReviewDecision? Decision { get; private set; }
    public string Conclusion { get; private set; } = "";
}
