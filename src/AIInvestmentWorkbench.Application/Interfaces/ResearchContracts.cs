using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record MetricDraft(Guid? Id, Guid SecurityId, string Period, PeriodType PeriodType, MetricType MetricType,
    decimal Value, string Currency, Guid? SourceId = null, bool IsEstimated = false, string Notes = "");
public sealed record SourceDraft(Guid? Id, Guid SecurityId, string Title, string Publisher, DateTimeOffset? PublishedDate,
    string Url, string LocalFilePath, SourceType SourceType, int ReliabilityLevel, string Notes, string ExtractedText);
public sealed record ScoreDraft(ScoreDimension Dimension, decimal Weight, decimal? UserScore, string Reason, string Evidence);
public sealed record ResearchSnapshot(CompanyResearch? Research, IReadOnlyList<FinancialMetric> Metrics,
    IReadOnlyList<ResearchSource> Sources, IReadOnlyList<ResearchScore> Scores);
public sealed record SearchHit(string Kind, Guid SecurityId, string Security, string Title, string Content);
public interface IResearchStore
{
    Task<ResearchSnapshot> ReadAsync(Guid securityId, CancellationToken ct = default);
    Task SaveResearchAsync(Guid securityId, ResearchContent content, int revision, CancellationToken ct = default);
    Task SaveMetricAsync(MetricDraft draft, CancellationToken ct = default);
    Task DeleteMetricAsync(Guid id, CancellationToken ct = default);
    Task<ImportOutcome> ImportMetricsAsync(IReadOnlyList<MetricDraft> drafts, bool validateOnly, CancellationToken ct = default);
    Task<Guid> SaveSourceAsync(SourceDraft draft, CancellationToken ct = default);
    Task DeleteSourceAsync(Guid id, CancellationToken ct = default);
    Task SaveScoresAsync(Guid securityId, IReadOnlyList<ScoreDraft> drafts, CancellationToken ct = default);
    Task<RiskThresholds> ReadThresholdsAsync(CancellationToken ct = default);
    Task SaveThresholdsAsync(RiskThresholds thresholds, CancellationToken ct = default);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default);
}
public interface IPdfTextExtractor { string Extract(string path); }
public interface IResearchFileReader { Task<string> ReadAsync(string path, CancellationToken ct = default); }
