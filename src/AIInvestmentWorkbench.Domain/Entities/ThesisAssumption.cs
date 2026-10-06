using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed record AssumptionContent(string Description, string Metric, decimal? Baseline, decimal? ExpectedValue,
    decimal WarningThreshold, decimal KillThreshold, decimal? CurrentValue, string Unit, ThresholdDirection Direction, string Evidence);
public sealed class ThesisAssumption : Entity
{
    private ThesisAssumption() { }
    public ThesisAssumption(Guid thesisId, AssumptionContent content, ThesisActor actor) { ThesisId = Guard.Id(thesisId, nameof(thesisId)); Edit(content, actor); }
    public Guid ThesisId { get; private set; }
    public Thesis Thesis { get; private set; } = null!;
    public string Description { get; private set; } = "";
    public string Metric { get; private set; } = "";
    public decimal? Baseline { get; private set; }
    public decimal? ExpectedValue { get; private set; }
    public decimal WarningThreshold { get; private set; }
    public decimal KillThreshold { get; private set; }
    public decimal? CurrentValue { get; private set; }
    public string Unit { get; private set; } = "";
    public ThresholdDirection Direction { get; private set; }
    public AssumptionStatus Status { get; private set; }
    public string Evidence { get; private set; } = "";
    public DateTimeOffset? LastReviewedAt { get; private set; }
    public void Edit(AssumptionContent c, ThesisActor actor)
    {
        ThesisRules.RequireUser(actor); ThesisRules.ValidateThresholds(c.Direction, c.WarningThreshold, c.KillThreshold);
        Description = Guard.Text(c.Description, nameof(c.Description), 10000); Metric = Guard.Text(c.Metric, nameof(c.Metric), 300);
        Baseline = c.Baseline; ExpectedValue = c.ExpectedValue; WarningThreshold = c.WarningThreshold; KillThreshold = c.KillThreshold;
        CurrentValue = c.CurrentValue; Unit = Guard.Text(c.Unit, nameof(c.Unit), 100); Direction = c.Direction; Evidence = Guard.OptionalText(c.Evidence, 10000);
        Status = ThesisRules.Evaluate(CurrentValue, Direction, WarningThreshold, KillThreshold);
        LastReviewedAt = CurrentValue.HasValue ? DateTimeOffset.UtcNow : null; MarkUpdated();
    }
}
