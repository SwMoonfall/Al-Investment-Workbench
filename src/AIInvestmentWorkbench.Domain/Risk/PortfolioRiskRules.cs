using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Enums;
namespace AIInvestmentWorkbench.Domain.Risk;

public enum RiskTagCategory { Sector, Market, Currency, Theme, Custom }
public enum WeightWarning { Normal, NearMaximum, AboveMaximum }
public sealed record RiskLabel(RiskTagCategory Category, string Name);
public sealed record RiskPosition(Guid SecurityId, string Security, decimal MarketValue, decimal Weight, decimal MaxWeight,
    bool EstimatedPrice, DateTimeOffset? PriceDate, IReadOnlyList<RiskLabel> Tags, SecurityType SecurityType = SecurityType.Stock);
public sealed record PortfolioRiskExposure(RiskTagCategory Category, string Name, decimal Weight, int SecurityCount);
public sealed record Concentration(decimal LargestPosition, decimal Top3, decimal Top5, decimal LargestSector, decimal LargestMarket, decimal LargestStockPosition = 0);
public sealed record ExposureResult(IReadOnlyList<PortfolioRiskExposure> Exposures, Concentration Concentration);
public sealed record SizingInputs(decimal BasePosition, decimal ConfidenceFactor, decimal ValuationFactor, decimal RiskFactor);
public sealed record SizingResult(decimal SuggestedPosition, WeightWarning Warning, bool AboveFullPortfolio);

public static class PortfolioRiskRules
{
    public const string SizingNotice = "计算结果仅为仓位规划辅助，最终仓位由用户决定。";
    public static string Normalize(string name) => Guard.Text(name, "标签名称", 100).ToUpperInvariant();
    public static ExposureResult Aggregate(IReadOnlyList<RiskPosition> positions, decimal cashWeight, string cashCurrency)
    {
        Guard.Weight(cashWeight); Guard.Currency(cashCurrency);
        if (positions.Select(x => x.SecurityId).Distinct().Count() != positions.Count) throw new BusinessException("组合风险输入包含重复证券。");
        foreach (var p in positions) { Guard.Weight(p.Weight); if (p.MarketValue < 0) throw new BusinessException("不支持负市值仓位。"); }
        if (positions.Sum(x => x.Weight) + cashWeight > 1.000000000001m) throw new BusinessException("持仓与现金权重超过总资产。");
        var rows = positions.SelectMany(p => p.Tags.DistinctBy(t => (t.Category, Normalize(t.Name)))
            .Select(t => new { t.Category, Name = t.Name.Trim(), Key = Normalize(t.Name), p.Weight, p.SecurityId })).ToArray();
        var exposures = rows.GroupBy(x => (x.Category, x.Key)).Select(g => new PortfolioRiskExposure(g.Key.Category, g.First().Name, g.Sum(x => x.Weight), g.Select(x => x.SecurityId).Distinct().Count())).ToList();
        if (cashWeight > 0)
        {
            var currency = Guard.Currency(cashCurrency); var index = exposures.FindIndex(x => x.Category == RiskTagCategory.Currency && Normalize(x.Name) == currency);
            if (index >= 0) exposures[index] = exposures[index] with { Weight = exposures[index].Weight + cashWeight };
            else exposures.Add(new(RiskTagCategory.Currency, currency, cashWeight, 0));
        }
        var weights = positions.Select(x => x.Weight).OrderDescending().ToArray();
        decimal Maximum(RiskTagCategory category) => exposures.Where(x => x.Category == category).Select(x => x.Weight).DefaultIfEmpty().Max();
        return new(exposures.OrderBy(x => x.Category).ThenByDescending(x => x.Weight).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray(),
            new(weights.FirstOrDefault(), weights.Take(3).Sum(), weights.Take(5).Sum(), Maximum(RiskTagCategory.Sector), Maximum(RiskTagCategory.Market), positions.Where(x => x.SecurityType == SecurityType.Stock).Select(x => x.Weight).DefaultIfEmpty().Max()));
    }
    public static WeightWarning CheckMaximum(decimal weight, decimal maximum, decimal nearRatio = .9m)
    {
        if (weight < 0) throw new BusinessException("仓位比例不能为负。"); Guard.Weight(maximum);
        if (nearRatio <= 0 || nearRatio > 1) throw new BusinessException("接近上限阈值应大于 0 且不超过 1。");
        return weight > maximum ? WeightWarning.AboveMaximum : weight > 0 && weight >= maximum * nearRatio ? WeightWarning.NearMaximum : WeightWarning.Normal;
    }
    public static SizingResult Size(SizingInputs inputs, decimal maximum, decimal nearRatio = .9m)
    {
        Guard.Weight(inputs.BasePosition);
        if (inputs.ConfidenceFactor < 0 || inputs.ValuationFactor < 0 || inputs.RiskFactor < 0) throw new BusinessException("仓位规划系数不得为负。");
        decimal result;
        try { result = checked(inputs.BasePosition * inputs.ConfidenceFactor * inputs.ValuationFactor * inputs.RiskFactor); }
        catch (OverflowException) { throw new BusinessException("计算结果超出范围，请检查系数。"); }
        return new(result, CheckMaximum(result, maximum, nearRatio), result > 1);
    }
}
