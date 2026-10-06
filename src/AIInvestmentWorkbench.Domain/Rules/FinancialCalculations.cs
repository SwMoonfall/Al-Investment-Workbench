using System.Globalization;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
namespace AIInvestmentWorkbench.Domain.Rules;

public static class FinancialPeriod
{
    public static string Validate(string period, PeriodType type)
    {
        period = period.Trim().ToUpperInvariant();
        if (!Enum.IsDefined(type) || period.Length != (type == PeriodType.Annual ? 4 : 7)
            || !int.TryParse(period.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year is < 1900 or > 2200
            || type != PeriodType.Annual && (period[4..6] != "-Q" || period[6] is < '1' or > '4'))
            throw new BusinessException("年度期间用 YYYY；季度和 TTM 用 YYYY-Q1 至 YYYY-Q4（TTM 为截至该季的十二个月）。");
        return period;
    }
    public static string PreviousYear(string period) => (int.Parse(period[..4], CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture) + period[4..];
    public static string Previous(string period, PeriodType type)
    {
        if (type == PeriodType.Annual) return PreviousYear(period);
        return period[6] == '1' ? PreviousYear(period)[..4] + "-Q4" : period[..6] + (period[6] - '0' - 1);
    }
    public static IReadOnlyList<string> ContinuousRange(IEnumerable<string> periods, PeriodType type)
    {
        var ordered = periods.Distinct().Order(StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) return [];
        var result = new List<string> { ordered[^1] };
        while (string.CompareOrdinal(result[^1], ordered[0]) > 0) result.Add(Previous(result[^1], type));
        result.Reverse(); return result;
    }
}
public static class FinancialCalculations
{
    public static decimal? FreeCashFlow(decimal? ocf, decimal? capex) => ocf.HasValue && capex.HasValue ? ocf.Value - capex.Value : null;
    // A zero or negative base does not have a meaningful conventional percentage growth rate.
    public static decimal? Growth(decimal? current, decimal? prior) => current.HasValue && prior > 0 ? (current.Value - prior.Value) / prior.Value : null;
    public static decimal? Ratio(decimal? numerator, decimal? denominator) => numerator.HasValue && denominator > 0 ? numerator.Value / denominator.Value : null;
    public static decimal? Value(IEnumerable<FinancialMetric> metrics, string period, MetricType type)
        => metrics.SingleOrDefault(x => x.Period == period && x.MetricType == type)?.Value;
    // Callers must supply one security, currency and period type; never aggregate mixed series.
    public static decimal? Derived(IReadOnlyList<FinancialMetric> series, string period, MetricType type)
    {
        if (series.Select(x => (x.SecurityId, x.PeriodType, x.Currency)).Distinct().Count() > 1) throw new BusinessException("不能混合证券、期间类型或币种计算。");
        decimal? V(MetricType metric) => Value(series, period, metric);
        return type switch
        {
            MetricType.FreeCashFlow => FreeCashFlow(V(MetricType.OperatingCashFlow), V(MetricType.Capex)),
            MetricType.GrossMargin => Ratio(V(MetricType.GrossProfit), V(MetricType.Revenue)),
            MetricType.OperatingMargin => Ratio(V(MetricType.OperatingIncome), V(MetricType.Revenue)),
            MetricType.RevenueGrowth => Growth(V(MetricType.Revenue), Value(series, FinancialPeriod.PreviousYear(period), MetricType.Revenue)),
            MetricType.NetDebt => V(MetricType.Debt) - V(MetricType.Cash),
            _ => null
        };
    }
}
