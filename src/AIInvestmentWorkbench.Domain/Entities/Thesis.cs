using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed record ThesisContent(string Title, string InvestmentSummary, string WhyMarketMayBeWrong = "", string ExpectedHoldingPeriod = "",
    string BaseCaseNarrative = "", string BullCaseNarrative = "", string BearCaseNarrative = "", string ExpectedCatalysts = "", string KeyRisks = "");
public sealed class Thesis : Entity
{
    private Thesis() { }
    public Thesis(Guid securityId, ThesisContent content, DateOnly? reviewDate, ThesisActor actor)
    {
        ThesisRules.RequireUser(actor); SecurityId = Guard.Id(securityId, nameof(securityId)); SetContent(content); ReviewDate = reviewDate;
    }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string InvestmentSummary { get; private set; } = "";
    public string WhyMarketMayBeWrong { get; private set; } = "";
    public string ExpectedHoldingPeriod { get; private set; } = "";
    public string BaseCaseNarrative { get; private set; } = "";
    public string BullCaseNarrative { get; private set; } = "";
    public string BearCaseNarrative { get; private set; } = "";
    public string ExpectedCatalysts { get; private set; } = "";
    public string KeyRisks { get; private set; } = "";
    public ThesisStatus Status { get; private set; } = ThesisStatus.Draft;
    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;
    public DateOnly? ReviewDate { get; private set; }
    public DateTimeOffset? LastReviewedAt { get; private set; }
    public int Version { get; private set; } = 1;
    public bool Archived { get; private set; }
    public bool IsCurrent { get; private set; }
    public ICollection<ThesisAssumption> Assumptions { get; private set; } = new List<ThesisAssumption>();
    public ICollection<KillCondition> KillConditions { get; private set; } = new List<KillCondition>();
    public ThesisContent Content => new(Title, InvestmentSummary, WhyMarketMayBeWrong, ExpectedHoldingPeriod, BaseCaseNarrative, BullCaseNarrative, BearCaseNarrative, ExpectedCatalysts, KeyRisks);
    public void EnsureEditable(ThesisActor actor)
    {
        ThesisRules.RequireUser(actor);
        if (Archived || Status is ThesisStatus.Invalidated or ThesisStatus.Closed) throw new BusinessException("已失效、关闭或归档的逻辑只读；请新增 Thesis 保留历史。");
    }
    private void SetContent(ThesisContent c)
    {
        Title = Guard.Text(c.Title, nameof(c.Title), 300); InvestmentSummary = Guard.Text(c.InvestmentSummary, nameof(c.InvestmentSummary), 100000);
        WhyMarketMayBeWrong = Guard.OptionalText(c.WhyMarketMayBeWrong, 100000); ExpectedHoldingPeriod = Guard.OptionalText(c.ExpectedHoldingPeriod, 500);
        BaseCaseNarrative = Guard.OptionalText(c.BaseCaseNarrative, 100000); BullCaseNarrative = Guard.OptionalText(c.BullCaseNarrative, 100000); BearCaseNarrative = Guard.OptionalText(c.BearCaseNarrative, 100000);
        ExpectedCatalysts = Guard.OptionalText(c.ExpectedCatalysts, 100000); KeyRisks = Guard.OptionalText(c.KeyRisks, 100000);
    }
    public void Edit(ThesisContent content, DateOnly? reviewDate, ThesisActor actor) { EnsureEditable(actor); SetContent(content); ReviewDate = reviewDate; }
    public void Reevaluate()
    {
        if (!Archived && Status is not (ThesisStatus.Invalidated or ThesisStatus.Closed) && KillConditions.Any(x => x.Status is KillConditionStatus.Warning or KillConditionStatus.Triggered))
            Status = ThesisStatus.Warning;
        // Recovery is not an automatic investment decision: Warning remains until the user confirms Active.
    }
    public void ChangeStatus(ThesisStatus next, ThesisActor actor)
    {
        ThesisRules.RequireUser(actor);
        if (!Enum.IsDefined(next) || Archived || Status == ThesisStatus.Closed || Status == ThesisStatus.Invalidated && next != ThesisStatus.Closed)
            throw new BusinessException("此逻辑已结束，不能重新激活；请建立新的 Thesis。");
        if (next == ThesisStatus.Draft && Status != ThesisStatus.Draft) throw new BusinessException("已启用或警示的逻辑不能退回草稿；可关闭后新建。");
        if (next == ThesisStatus.Active && KillConditions.Any(x => x.Status != KillConditionStatus.Normal))
            throw new BusinessException("仍有 Warning / Triggered 条件，不能确认 Active。请核对条件与当前值。");
        Status = next;
        if (next == ThesisStatus.Active) IsCurrent = true;
        if (next is ThesisStatus.Invalidated or ThesisStatus.Closed) IsCurrent = false;
        Reevaluate();
    }
    public void CompleteReview(DateOnly nextReview, DateOnly today, ThesisActor actor)
    {
        EnsureEditable(actor);
        if (nextReview <= today) throw new BusinessException("完成复盘后的下次日期必须晚于今天。");
        ReviewDate = nextReview; LastReviewedAt = DateTimeOffset.UtcNow;
    }
    public void Archive(ThesisActor actor)
    {
        ThesisRules.RequireUser(actor);
        if (Status is not (ThesisStatus.Invalidated or ThesisStatus.Closed)) throw new BusinessException("请先确认失效或关闭，再归档历史逻辑。");
        Archived = true; IsCurrent = false;
    }
    public void AdvanceVersion(int expected)
    {
        if (Version != expected) throw new BusinessException("此 Thesis 已被更新，请刷新后重新编辑，避免覆盖历史。");
        Version++; MarkUpdated();
    }
}
