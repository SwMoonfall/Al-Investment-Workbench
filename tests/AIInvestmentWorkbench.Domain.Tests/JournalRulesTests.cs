using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;
namespace AIInvestmentWorkbench.Domain.Tests;
public sealed class JournalRulesTests
{
    [Theory]
    [InlineData(ReviewKind.Weekly, "2026-01-01", "2025-12-29", "2026-01-04")]
    [InlineData(ReviewKind.Monthly, "2024-02-20", "2024-02-01", "2024-02-29")]
    [InlineData(ReviewKind.Quarterly, "2025-12-31", "2025-10-01", "2025-12-31")]
    public void PeriodBoundaries(ReviewKind kind, string anchor, string start, string end)
    { var p = JournalRules.Period(kind, DateOnly.Parse(anchor)); Assert.Equal(DateOnly.Parse(start), p.Start); Assert.Equal(DateOnly.Parse(end), p.End); }
    [Theory] [InlineData("2026-03-31", "2025-12-31")] [InlineData("2026-04-01", "2026-03-31")] [InlineData("2026-01-01", "2025-12-31")]
    public void LastCompletedQuarterHandlesBoundary(string today, string end) => Assert.Equal(DateOnly.Parse(end), JournalRules.LastCompletedQuarter(DateOnly.Parse(today)).End);
    [Fact] public void DueRequiresPeriodEndAndHumanCompletion()
    { var end = new DateOnly(2025, 12, 31); Assert.False(JournalRules.QuarterlyDue(end, end, null)); Assert.True(JournalRules.QuarterlyDue(end.AddDays(1), end, null)); Assert.True(JournalRules.QuarterlyDue(end.AddDays(1), end, ReviewStatus.Draft)); Assert.False(JournalRules.QuarterlyDue(end.AddDays(1), end, ReviewStatus.Completed)); }
    [Theory] [InlineData(ThesisActor.AI)] [InlineData(ThesisActor.System)]
    public void NonHumanCannotWriteJournalOrReview(ThesisActor actor)
    {
        Assert.Throws<BusinessException>(() => new InvestmentJournal(Guid.NewGuid(), null, Content(), actor));
        Assert.Throws<BusinessException>(() => new InvestmentReview(Guid.NewGuid(), null, ReviewKind.Monthly, new(2025, 1, 1), new(2025, 1, 31), "{}", "", [], null, "", ReviewStatus.Draft, actor, new(2026, 1, 1)));
    }
    [Theory] [InlineData(-1)] [InlineData(101)] public void ConfidenceOutOfRangeRejected(decimal confidence) => Assert.Throws<BusinessException>(() => JournalRules.Validate(Content() with { Confidence = confidence }));
    [Fact] public void OptionalTradeFieldsRemainUnknown() { var j = new InvestmentJournal(Guid.NewGuid(), null, Content(), ThesisActor.HumanUser); Assert.Null(j.Price); Assert.Null(j.Quantity); Assert.Null(j.Confidence); Assert.Equal(j.Id, j.RootId); Assert.Equal(1, j.Version); }
    [Fact] public void FutureJournalAndInvalidWeightsRejected()
    { Assert.Throws<BusinessException>(() => JournalRules.Validate(Content() with { Date = DateOnly.FromDateTime(DateTime.Today).AddDays(1) })); Assert.Throws<BusinessException>(() => JournalRules.Validate(Content() with { PortfolioWeight = 1.2m })); }
    [Fact] public void QuarterlyCompletionRequiresAssessmentsEvidenceAndDecision()
    {
        var a = new AssumptionReview(Guid.NewGuid(), "Margin", AssumptionOutcome.Unassessed, "");
        InvestmentReview Create(AssumptionReview assessment, ReviewDecision? choice, DateOnly today) => new(Guid.NewGuid(), Guid.NewGuid(), ReviewKind.Quarterly, new(2025, 10, 1), new(2025, 12, 31), "{}", "实际结果", [assessment], choice, "用户结论", ReviewStatus.Completed, ThesisActor.HumanUser, today);
        Assert.Throws<BusinessException>(() => Create(a, ReviewDecision.Hold, new(2026, 1, 1)));
        Assert.Throws<BusinessException>(() => Create(a with { Outcome = AssumptionOutcome.Validated, Evidence = "财报" }, null, new(2026, 1, 1)));
        Assert.Throws<BusinessException>(() => Create(a with { Outcome = AssumptionOutcome.Validated, Evidence = "财报" }, ReviewDecision.Hold, new(2025, 12, 30)));
        Assert.Equal(ReviewStatus.Completed, Create(a with { Outcome = AssumptionOutcome.Weakened, Evidence = "用户核实" }, ReviewDecision.ContinueResearch, new(2026, 1, 1)).Status);
    }
    [Fact] public void FifoDurationUsesActualSoldQuantitiesNotJournalIntent()
    {
        var a = new PortfolioAccount("test", "CNY"); var s = new Security("T", "Test", "XSHG", "CNY", SecurityType.Stock);
        Transaction T(int day, TransactionType type, decimal qty, long sequence) => new(a, s, type, qty, 10, 0, 0, new DateTimeOffset(2025, 1, day, 12, 0, 0, TimeSpan.Zero), sequence: sequence);
        Assert.Null(JournalRules.AverageClosedHoldingDays([T(1, TransactionType.Buy, 100, 1)]));
        Assert.Equal(15, JournalRules.AverageClosedHoldingDays([T(1, TransactionType.Buy, 100, 1), T(6, TransactionType.Buy, 100, 2), T(11, TransactionType.Sell, 50, 3), T(21, TransactionType.Sell, 100, 4)]));
        Assert.Null(JournalRules.AverageClosedHoldingDays([T(1, TransactionType.Sell, 100, 1)]));
    }
    private static JournalContent Content() => new(new(2025, 1, 1), JournalAction.Hold, null, null, null, "等待证据");
}

