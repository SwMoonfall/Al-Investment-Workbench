using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;
namespace AIInvestmentWorkbench.Domain.Tests;

public sealed class ThesisRulesTests
{
    private const ThesisActor User = ThesisActor.HumanUser;
    private static Thesis Create() => new(Guid.NewGuid(), new("投资逻辑", "可验证的观点"), new DateOnly(2026, 10, 3), User);
    private static KillContent Condition(decimal? value = 30, ThresholdDirection direction = ThresholdDirection.AtOrBelow) => new("利润率", "盈利能力是关键前提", "OperatingMargin", direction == ThresholdDirection.AtOrBelow ? 20 : 80, direction == ThresholdDirection.AtOrBelow ? 10 : 90, value, "%", direction, "年报");
    [Theory]
    [InlineData(null, ThresholdDirection.AtOrBelow, AssumptionStatus.Unknown)]
    [InlineData(30, ThresholdDirection.AtOrBelow, AssumptionStatus.Normal)]
    [InlineData(20, ThresholdDirection.AtOrBelow, AssumptionStatus.Warning)]
    [InlineData(10, ThresholdDirection.AtOrBelow, AssumptionStatus.Triggered)]
    [InlineData(-1, ThresholdDirection.AtOrBelow, AssumptionStatus.Triggered)]
    [InlineData(79, ThresholdDirection.AtOrAbove, AssumptionStatus.Normal)]
    [InlineData(80, ThresholdDirection.AtOrAbove, AssumptionStatus.Warning)]
    [InlineData(90, ThresholdDirection.AtOrAbove, AssumptionStatus.Triggered)]
    [InlineData(100, ThresholdDirection.AtOrAbove, AssumptionStatus.Triggered)]
    public void ThresholdDirectionAndInclusiveBoundaries(int? value, ThresholdDirection direction, AssumptionStatus expected)
    {
        var c = Condition(value.HasValue ? (decimal)value : null, direction);
        Assert.Equal(expected, ThesisRules.Evaluate(c.CurrentValue, direction, c.WarningThreshold, c.TriggerThreshold));
    }
    [Fact]
    public void NoKillConditions_DoesNotInventRiskOrInvalidate()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Active, User); t.Reevaluate();
        Assert.Equal(ThesisStatus.Active, t.Status); Assert.True(t.IsCurrent);
    }
    [Fact]
    public void NormalKillCondition_PreservesActive()
    {
        var t = Create(); t.KillConditions.Add(new(t.Id, Condition(), User)); t.ChangeStatus(ThesisStatus.Active, User); t.Reevaluate(); Assert.Equal(ThesisStatus.Active, t.Status);
    }
    [Theory] [InlineData(20)] [InlineData(10)]
    public void WarningOrTriggered_EscalatesButNeverInvalidates(decimal value)
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Active, User); t.KillConditions.Add(new(t.Id, Condition(value), User)); t.Reevaluate();
        Assert.Equal(ThesisStatus.Warning, t.Status); Assert.True(t.IsCurrent);
        Assert.Throws<BusinessException>(() => t.ChangeStatus(ThesisStatus.Active, User));
        t.ChangeStatus(ThesisStatus.Invalidated, User); Assert.False(t.IsCurrent); Assert.Equal(ThesisStatus.Invalidated, t.Status);
    }
    [Fact]
    public void MultipleConditions_AnyTriggerEscalatesRegardlessOfOrdering()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Active, User);
        t.KillConditions.Add(new(t.Id, Condition(30), User)); t.KillConditions.Add(new(t.Id, Condition(5), User)); t.KillConditions.Add(new(t.Id, Condition(null), User));
        t.Reevaluate(); Assert.Equal(ThesisStatus.Warning, t.Status);
    }
    [Fact]
    public void TriggeredDraft_BecomesWarningButDoesNotStealCurrentSlot()
    {
        var t = Create(); t.KillConditions.Add(new(t.Id, Condition(0), User)); t.Reevaluate();
        Assert.Equal(ThesisStatus.Warning, t.Status); Assert.False(t.IsCurrent);
    }
    [Fact]
    public void ChangingConditionRecalculatesAndRecoveryRequiresUserConfirmation()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Active, User); var k = new KillCondition(t.Id, Condition(8), User); t.KillConditions.Add(k); t.Reevaluate();
        k.Edit(Condition(30), User); Assert.Equal(KillConditionStatus.Normal, k.Status); t.Reevaluate(); Assert.Equal(ThesisStatus.Warning, t.Status);
        t.ChangeStatus(ThesisStatus.Active, User); Assert.Equal(ThesisStatus.Active, t.Status);
    }
    [Fact]
    public void MissingValueIsUnassessed()
    {
        var k = new KillCondition(Guid.NewGuid(), Condition(null), User); Assert.False(k.IsAssessed); Assert.Null(k.LastReviewedAt);
        var a = new ThesisAssumption(Guid.NewGuid(), new("保持利润率", "Margin", 30, 35, 20, 10, null, "%", ThresholdDirection.AtOrBelow, ""), User);
        Assert.Equal(AssumptionStatus.Unknown, a.Status); a.Edit(new("保持利润率", "Margin", 30, 35, 20, 10, 9, "%", ThresholdDirection.AtOrBelow, "用户核对报表"), User);
        Assert.Equal(AssumptionStatus.Triggered, a.Status); Assert.NotNull(a.LastReviewedAt);
    }
    [Theory] [InlineData(ThesisActor.AI)] [InlineData(ThesisActor.System)]
    public void NonHumanCannotCreateOrEditConditionsOrDecideStatus(ThesisActor actor)
    {
        Assert.Throws<BusinessException>(() => new KillCondition(Guid.NewGuid(), Condition(), actor));
        var k = new KillCondition(Guid.NewGuid(), Condition(), User); Assert.Throws<BusinessException>(() => k.Edit(Condition(1), actor)); Assert.Equal(30, k.CurrentValue);
        var t = Create(); Assert.Throws<BusinessException>(() => t.ChangeStatus(ThesisStatus.Invalidated, actor)); Assert.Equal(ThesisStatus.Draft, t.Status);
    }
    [Theory] [InlineData(ThresholdDirection.AtOrBelow, 10, 20)] [InlineData(ThresholdDirection.AtOrAbove, 20, 10)]
    public void ReversedThresholdsRejected(ThresholdDirection direction, decimal warning, decimal trigger) => Assert.Throws<BusinessException>(() => ThesisRules.ValidateThresholds(direction, warning, trigger));
    [Fact]
    public void TerminalAndArchivedThesesCannotBeReactivatedOrEdited()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Invalidated, User); Assert.Throws<BusinessException>(() => t.ChangeStatus(ThesisStatus.Active, User));
        Assert.Throws<BusinessException>(() => t.Edit(new("覆盖", "覆盖"), null, User));
        t.ChangeStatus(ThesisStatus.Closed, User); t.Archive(User); t.Reevaluate(); Assert.Equal(ThesisStatus.Closed, t.Status); Assert.True(t.Archived);
    }
    [Fact]
    public void ActiveCannotReturnToDraftOrBeArchivedDirectly()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Active, User); Assert.Throws<BusinessException>(() => t.ChangeStatus(ThesisStatus.Draft, User)); Assert.Throws<BusinessException>(() => t.Archive(User));
    }
    [Theory]
    [InlineData(-1, ThesisStatus.Draft, false, true)] [InlineData(0, ThesisStatus.Active, false, true)] [InlineData(0, ThesisStatus.Warning, false, true)]
    [InlineData(1, ThesisStatus.Active, false, false)] [InlineData(0, ThesisStatus.Invalidated, false, false)] [InlineData(0, ThesisStatus.Closed, false, false)] [InlineData(0, ThesisStatus.Closed, true, false)]
    public void ReviewDateUsesInclusiveLocalCalendarDay(int offset, ThesisStatus status, bool archived, bool due)
    { var today = new DateOnly(2026, 10, 3); Assert.Equal(due, ThesisRules.ReviewDue(today.AddDays(offset), today, status, archived)); }
    [Fact] public void UnscheduledReviewIsNotDue() => Assert.False(ThesisRules.ReviewDue(null, new(2026, 10, 3), ThesisStatus.Active, false));
    [Fact]
    public void CompletedReviewMustHaveFutureDateAndDoesNotClearWarning()
    {
        var t = Create(); t.ChangeStatus(ThesisStatus.Warning, User); var today = new DateOnly(2026, 10, 3);
        Assert.Throws<BusinessException>(() => t.CompleteReview(today, today, User));
        t.CompleteReview(today.AddDays(30), today, User); Assert.Equal(ThesisStatus.Warning, t.Status); Assert.NotNull(t.LastReviewedAt);
        Assert.False(ThesisRules.ReviewDue(t.ReviewDate, today, t.Status, t.Archived));
    }
    [Fact] public void StaleVersionRejected() { var t = Create(); t.AdvanceVersion(1); Assert.Throws<BusinessException>(() => t.AdvanceVersion(1)); Assert.Equal(2, t.Version); }
    [Fact]
    public void DecimalThresholdsDoNotRoundAcrossTrigger()
    {
        Assert.Equal(AssumptionStatus.Warning, ThesisRules.Evaluate(10.00000000000001m, ThresholdDirection.AtOrBelow, 20, 10));
        Assert.Equal(AssumptionStatus.Triggered, ThesisRules.Evaluate(10m, ThresholdDirection.AtOrBelow, 20, 10));
    }
}
