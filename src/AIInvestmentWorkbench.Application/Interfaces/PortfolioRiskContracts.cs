using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Risk;
namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record RiskTagDto(Guid Id, string Name, RiskTagCategory Category, bool IsBuiltIn)
{ public string Label => $"{Category} · {Name}"; }
public sealed record ThesisRiskState(Guid Id, Guid SecurityId, string Security, string Title, int Version, ThesisStatus Status, bool IsCurrent,
    int ConditionCount, int UnknownConditions, IReadOnlyList<KillRiskState> Conditions);
public sealed record KillRiskState(string Title, KillConditionStatus Status, decimal? CurrentValue, string Evidence);
public sealed record AccountingAttention(Guid SecurityId, string Security, PeriodType PeriodType, string Currency, string Rule, string Evidence);
public sealed record PortfolioRiskSnapshot(Guid AccountId, string AccountName, string Currency, decimal TotalAssets, decimal Cash, decimal NearMaxRatio,
    IReadOnlyList<RiskPosition> Positions, ExposureResult Exposure, IReadOnlyList<ThesisRiskState> Theses, IReadOnlyList<AccountingAttention> HighAttention,
    int UnassessedAccountingChecks)
{
    public int TriggeredCount => Theses.SelectMany(x => x.Conditions).Count(x => x.Status == KillConditionStatus.Triggered);
    public int ThesisWarningCount => Theses.Count(x => x.Status == ThesisStatus.Warning);
    public int EstimatedPriceCount => Positions.Count(x => x.EstimatedPrice);
}
public sealed record DecisionContext(PortfolioRiskSnapshot Portfolio, Guid SecurityId, string Security, decimal CurrentWeight, decimal MaxWeight,
    ThesisRiskState? CurrentThesis, ThesisStatus? LatestThesisStatus, IReadOnlyList<ThesisRiskState> OpenTheses,
    IReadOnlyList<AccountingAttention> HighAttention, int UnassessedAccountingChecks, IReadOnlyList<RiskLabel> SecurityTags, int SchemaVersion = 1)
{
    public WeightWarning MaxWeightWarning => PortfolioRiskRules.CheckMaximum(CurrentWeight, MaxWeight, Portfolio.NearMaxRatio);
    public bool IsThesisActive => CurrentThesis?.Status == ThesisStatus.Active;
    public int TriggeredCount => OpenTheses.SelectMany(x => x.Conditions).Count(x => x.Status == KillConditionStatus.Triggered);
}
public sealed record DecisionPreview(DecisionContext Context, string Fingerprint);
public sealed record DecisionDraft(Guid AccountId, Guid SecurityId, DecisionKind Kind, DecisionChoice Choice, IReadOnlyList<DecisionAnswer> Answers,
    string Reason, string ExpectedFingerprint, SizingInputs? Sizing = null);
public sealed record DecisionRecordDto(Guid Id, DateTimeOffset CreatedAt, DecisionKind Kind, DecisionChoice Choice, string Reason,
    IReadOnlyList<DecisionAnswer> Answers, DecisionContext Context, SizingInputs? Sizing)
{ public string Security => Context.Security; public string Label => $"{CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {Security} · {Choice}"; }
public interface IPortfolioRiskStore
{
    Task InitializeDefaultsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RiskTagDto>> TagsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RiskTagDto>> SecurityTagsAsync(Guid securityId, CancellationToken ct = default);
    Task<Guid> CreateTagAsync(string name, RiskTagCategory category, CancellationToken ct = default);
    Task SetTagAsync(Guid securityId, Guid tagId, bool assigned, CancellationToken ct = default);
    Task SetNearMaxRatioAsync(decimal value, CancellationToken ct = default);
    Task<PortfolioRiskSnapshot> ReadAsync(Guid accountId, CancellationToken ct = default);
    Task<DecisionPreview> PreviewAsync(Guid accountId, Guid securityId, CancellationToken ct = default);
    Task<Guid> SaveDecisionAsync(DecisionDraft draft, CancellationToken ct = default);
    Task<IReadOnlyList<DecisionRecordDto>> DecisionsAsync(Guid accountId, CancellationToken ct = default);
}
