namespace AIInvestmentWorkbench.Application.Services;

public sealed record PositionValuation(decimal MarketValue, decimal UnrealizedProfitLoss, decimal? ReturnPercent);

public static class PositionCalculator
{
    // Inputs must share a currency. No rounding until presentation; no FX or live prices in Phase 0.
    public static PositionValuation Calculate(decimal quantity, decimal totalCost, decimal currentPrice)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (totalCost < 0 || (quantity == 0 && totalCost != 0)) throw new ArgumentOutOfRangeException(nameof(totalCost));
        if (currentPrice < 0) throw new ArgumentOutOfRangeException(nameof(currentPrice));
        var marketValue = checked(quantity * currentPrice);
        var profit = marketValue - totalCost;
        return new(marketValue, profit, totalCost == 0 ? null : profit / totalCost * 100m);
    }
}
