using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class FinancialMetric : Entity
{
    private FinancialMetric() { }
    public FinancialMetric(Guid securityId, string period, PeriodType periodType, MetricType metricType, decimal value,
        string currency, Guid? sourceId = null, bool isEstimated = false, string notes = "")
    {
        SecurityId = Guard.Id(securityId, nameof(securityId));
        Edit(period, periodType, metricType, value, currency, sourceId, isEstimated, notes);
    }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Period { get; private set; } = "";
    public PeriodType PeriodType { get; private set; }
    public MetricType MetricType { get; private set; }
    public decimal Value { get; private set; }
    public string Currency { get; private set; } = "";
    public Guid? SourceId { get; private set; }
    public ResearchSource? Source { get; private set; }
    public bool IsEstimated { get; private set; }
    public string Notes { get; private set; } = "";
    public void Edit(string period, PeriodType periodType, MetricType metricType, decimal value, string currency, Guid? sourceId, bool isEstimated, string notes)
    {
        if (!Enum.IsDefined(metricType)) throw new BusinessException("未知财务指标。");
        Period = FinancialPeriod.Validate(period, periodType); PeriodType = periodType; MetricType = metricType;
        if (metricType is MetricType.Capex or MetricType.ShareCount && value < 0) throw new BusinessException("Capex 为正数现金支出，股票数量不得为负数。");
        if (sourceId == Guid.Empty) throw new BusinessException("来源标识无效。");
        Value = value; Currency = Guard.Currency(currency); SourceId = sourceId; IsEstimated = isEstimated; Notes = Guard.OptionalText(notes, 10000); MarkUpdated();
    }
}
