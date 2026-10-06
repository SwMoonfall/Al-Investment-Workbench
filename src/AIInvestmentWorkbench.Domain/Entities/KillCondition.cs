using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed record KillContent(string Title, string Description, string Metric, decimal WarningThreshold, decimal TriggerThreshold,
    decimal? CurrentValue, string Unit, ThresholdDirection Direction, string Evidence);
public sealed class KillCondition : Entity
{
    private KillCondition() { }
    public KillCondition(Guid thesisId, KillContent content, ThesisActor actor) { ThesisId = Guard.Id(thesisId, nameof(thesisId)); Edit(content, actor); }
    public Guid ThesisId { get; private set; }
    public Thesis Thesis { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Metric { get; private set; } = "";
    public decimal WarningThreshold { get; private set; }
    public decimal TriggerThreshold { get; private set; }
    public decimal? CurrentValue { get; private set; }
    public string Unit { get; private set; } = "";
    public ThresholdDirection Direction { get; private set; }
    public KillConditionStatus Status { get; private set; }
    public string Evidence { get; private set; } = "";
    public DateTimeOffset? LastReviewedAt { get; private set; }
    public bool IsAssessed => CurrentValue.HasValue;
    public void Edit(KillContent c, ThesisActor actor)
    {
        ThesisRules.RequireUser(actor); ThesisRules.ValidateThresholds(c.Direction, c.WarningThreshold, c.TriggerThreshold);
        Title = Guard.Text(c.Title, nameof(c.Title), 300); Description = Guard.OptionalText(c.Description, 10000); Metric = Guard.Text(c.Metric, nameof(c.Metric), 300);
        WarningThreshold = c.WarningThreshold; TriggerThreshold = c.TriggerThreshold; CurrentValue = c.CurrentValue;
        Unit = Guard.Text(c.Unit, nameof(c.Unit), 100); Direction = c.Direction; Evidence = Guard.OptionalText(c.Evidence, 10000);
        Status = ThesisRules.Evaluate(CurrentValue, Direction, WarningThreshold, TriggerThreshold) switch { AssumptionStatus.Triggered => KillConditionStatus.Triggered, AssumptionStatus.Warning => KillConditionStatus.Warning, _ => KillConditionStatus.Normal };
        LastReviewedAt = CurrentValue.HasValue ? DateTimeOffset.UtcNow : null; MarkUpdated();
    }
}
