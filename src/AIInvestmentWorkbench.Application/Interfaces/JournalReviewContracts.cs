using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Risk;
namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record JournalDraft(Guid AccountId, Guid? SecurityId, JournalContent Content, Guid? DecisionId = null, Guid? PreviousId = null, string CorrectionReason = "初始记录");
public sealed record JournalRow(InvestmentJournal Journal, string Security)
{ public string Label => $"{Journal.Date:yyyy-MM-dd} · {Security} · {Journal.Action} · v{Journal.Version}"; }
public sealed record ReviewSection(string Title, IReadOnlyList<string> Lines);
public sealed record ReviewEvidence(Guid AccountId, Guid? SecurityId, ReviewKind Kind, DateOnly Start, DateOnly End, DateTimeOffset CapturedAt,
    IReadOnlyList<ReviewSection> Sections, IReadOnlyList<AssumptionReview> Assumptions, IReadOnlyDictionary<string, string> ObservedState, int SchemaVersion = 1);
public sealed record ReviewDraft(ReviewEvidence Evidence, string ActualResults, IReadOnlyList<AssumptionReview> Assumptions,
    ReviewDecision? Decision, string Conclusion, ReviewStatus Status, Guid? PreviousId = null);
public sealed record ReviewRow(InvestmentReview Review, string Security)
{ public string Label => $"{Review.Kind} · {Review.PeriodStart:yyyy-MM-dd}—{Review.PeriodEnd:yyyy-MM-dd} · {Security} · {Review.Status} · v{Review.Version}"; }
public sealed record QuarterlyDueItem(Guid SecurityId, string Security, DateOnly PeriodStart, DateOnly PeriodEnd)
{ public string Label => $"{Security} · {PeriodStart:yyyy-MM-dd}—{PeriodEnd:yyyy-MM-dd} · 需要 Quarterly Review"; }
public sealed record JournalDashboard(IReadOnlyList<QuarterlyDueItem> Due, IReadOnlyList<string> ThesisWarnings, IReadOnlyList<JournalRow> RecentJournals, IReadOnlyList<AIAnalysis> RecentAI);
public interface IJournalReviewReader
{
    Task<IReadOnlyList<JournalRow>> JournalsAsync(Guid accountId, bool history = false, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewRow>> ReviewsAsync(Guid accountId, bool history = false, CancellationToken ct = default);
    Task<ReviewEvidence> PreviewAsync(Guid accountId, Guid? securityId, ReviewKind kind, DateOnly anchor, CancellationToken ct = default);
    Task<JournalDashboard> DashboardAsync(Guid accountId, DateOnly today, CancellationToken ct = default);
    Task<string> BehaviorAsync(Guid accountId, DateOnly today, CancellationToken ct = default);
}
public interface IJournalReviewCommands
{
    Task<Guid> SaveJournalAsync(JournalDraft draft, ThesisActor actor, CancellationToken ct = default);
    Task<Guid> SaveReviewAsync(ReviewDraft draft, ThesisActor actor, CancellationToken ct = default);
}
public static class DecisionJournalMapper
{
    public static JournalDraft Draft(DecisionRecordDto d) => new(d.Context.Portfolio.AccountId, d.Context.SecurityId,
        new(DateOnly.FromDateTime(d.CreatedAt.ToLocalTime().DateTime), d.Choice switch { DecisionChoice.Add => JournalAction.Add, DecisionChoice.Hold => JournalAction.Hold, DecisionChoice.Trim => JournalAction.Trim, DecisionChoice.Exit => JournalAction.Exit, _ => JournalAction.ResearchDecision },
            null, null, d.Context.CurrentWeight, d.Reason, KillConditionSummary: string.Join("\n", d.Context.OpenTheses.SelectMany(x => x.Conditions).Select(x => $"{x.Title}: {x.Status} · {x.Evidence}")),
            Notes: $"来自决策 {d.Id} / {d.Kind} / {d.Choice}。该清单是决策而非已执行交易；价格、数量、信心与持有周期需用户补充。"), d.Id);
}
