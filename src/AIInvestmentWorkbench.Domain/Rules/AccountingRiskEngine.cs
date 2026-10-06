using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
namespace AIInvestmentWorkbench.Domain.Rules;

public sealed record RiskThresholds(decimal GrowthGapWarning = .15m, decimal GrowthGapHigh = .30m,
    decimal CashConversionWarning = .8m, decimal CashConversionHigh = .5m,
    decimal DebtGrowthWarning = .25m, decimal DebtGrowthHigh = .5m,
    decimal DilutionWarning = .02m, decimal DilutionHigh = .05m,
    decimal SbcWarning = .1m, decimal SbcHigh = .2m,
    int ConsecutivePeriods = 3, decimal FcfWarning = 0m, decimal FcfHigh = 0m)
{
    public void Validate()
    {
        if (GrowthGapWarning < 0 || GrowthGapHigh < GrowthGapWarning || CashConversionHigh < 0 || CashConversionWarning < CashConversionHigh
            || DebtGrowthWarning < 0 || DebtGrowthHigh < DebtGrowthWarning || DilutionWarning < 0 || DilutionHigh < DilutionWarning
            || SbcWarning < 0 || SbcHigh < SbcWarning || ConsecutivePeriods is < 2 or > 10 || FcfHigh > FcfWarning)
            throw new BusinessException("风险阈值无效：增长/稀释/SBC 的高关注阈值须不小于警示阈值；现金转换率相反；连续期数为 2 至 10。");
    }
}
public sealed record RiskFinding(string Rule, AttentionLevel Level, bool Assessed, string Evidence);

public static class AccountingRiskEngine
{
    public static IReadOnlyList<RiskFinding> Evaluate(IReadOnlyList<FinancialMetric> metrics, PeriodType type, string currency, RiskThresholds thresholds)
    {
        thresholds.Validate();
        var series = metrics.Where(x => x.PeriodType == type && x.Currency == currency && !x.IsEstimated).ToList();
        if (series.Select(x => x.SecurityId).Distinct().Count() > 1) throw new BusinessException("风险检查一次仅支持一只证券。");
        var period = series.Select(x => x.Period).OrderDescending(StringComparer.Ordinal).FirstOrDefault();
        var output = new List<RiskFinding>();
        decimal? V(string? p, MetricType m) => p is null ? null : FinancialCalculations.Value(series, p, m);
        var prior = period is null ? null : FinancialPeriod.PreviousYear(period);
        var revenueGrowth = FinancialCalculations.Growth(V(period, MetricType.Revenue), V(prior, MetricType.Revenue));
        void Check(string rule, decimal? value, decimal warning, decimal high, string evidence, bool lower = false)
        {
            var level = value is null ? AttentionLevel.Normal : (lower ? value <= high : value >= high) ? AttentionLevel.HighAttention
                : (lower ? value <= warning : value >= warning) ? AttentionLevel.Warning : AttentionLevel.Normal;
            output.Add(new(rule, level, value.HasValue, value.HasValue ? $"{period}：{evidence} {value:P2}；警示 {warning:P0} / 高关注 {high:P0}" : "数据不足或基期非正，未能评估。"));
        }
        Check("应收账款增长高于收入", FinancialCalculations.Growth(V(period, MetricType.AccountsReceivable), V(prior, MetricType.AccountsReceivable)) - revenueGrowth,
            thresholds.GrowthGapWarning, thresholds.GrowthGapHigh, "同比增速差");
        Check("库存增长高于收入", FinancialCalculations.Growth(V(period, MetricType.Inventory), V(prior, MetricType.Inventory)) - revenueGrowth,
            thresholds.GrowthGapWarning, thresholds.GrowthGapHigh, "同比增速差");
        Check("经营现金流弱于净利润", FinancialCalculations.Ratio(V(period, MetricType.OperatingCashFlow), V(period, MetricType.NetIncome)),
            thresholds.CashConversionWarning, thresholds.CashConversionHigh, "OCF / 净利润", true);
        Check("债务快速增长", FinancialCalculations.Growth(V(period, MetricType.Debt), V(prior, MetricType.Debt)),
            thresholds.DebtGrowthWarning, thresholds.DebtGrowthHigh, "债务同比增长");
        var periods = new List<string>();
        if (period is not null) { periods.Add(period); for (var i = 1; i < thresholds.ConsecutivePeriods; i++) periods.Add(FinancialPeriod.Previous(periods[^1], type)); }
        var shares = periods.Select(p => V(p, MetricType.ShareCount)).ToList();
        var sharesComplete = shares.Count == thresholds.ConsecutivePeriods && shares.All(x => x > 0);
        var rising = sharesComplete && shares.Zip(shares.Skip(1), (a, b) => a > b).All(x => x);
        if (sharesComplete && !rising) output.Add(new("股票数量持续增加", AttentionLevel.Normal, true, $"截至 {period} 最近 {thresholds.ConsecutivePeriods} 期没有逐期增加。"));
        else Check("股票数量持续增加", !sharesComplete ? null : FinancialCalculations.Growth(shares[0], shares[^1]),
            thresholds.DilutionWarning, thresholds.DilutionHigh, $"连续 {thresholds.ConsecutivePeriods} 期累计增幅（须逐期增加）");
        Check("股票薪酬占比较高", FinancialCalculations.Ratio(V(period, MetricType.StockBasedCompensation), V(period, MetricType.Revenue)),
            thresholds.SbcWarning, thresholds.SbcHigh, "SBC / 收入");
        var flows = periods.Select(p => FinancialCalculations.Derived(series, p, MetricType.FreeCashFlow) ?? V(p, MetricType.FreeCashFlow)).ToList();
        var complete = flows.Count == thresholds.ConsecutivePeriods && flows.All(x => x.HasValue);
        output.Add(new("FCF 持续偏弱", complete && flows.All(x => x <= thresholds.FcfWarning) ? (flows.All(x => x < thresholds.FcfHigh) ? AttentionLevel.HighAttention : AttentionLevel.Warning) : AttentionLevel.Normal,
            complete, complete ? $"截至 {period} 连续 {thresholds.ConsecutivePeriods} 期 FCF（新至旧）：{string.Join(" / ", flows.Select(x => x!.Value.ToString("N2")))} {currency}。全部 ≤ {thresholds.FcfWarning:N2} 为警示；全部 < {thresholds.FcfHigh:N2} 为高关注。" : "连续期间不足，未能评估。"));
        return output;
    }
}
