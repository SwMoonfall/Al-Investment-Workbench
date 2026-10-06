using AIInvestmentWorkbench.Application.Services;
using Xunit;

namespace AIInvestmentWorkbench.Application.Tests;

public class PositionCalculatorTests
{
    [Fact]
    public void Valuation_CalculatesProfitAndPercentage()
    {
        var result = PositionCalculator.Calculate(200m, 2000m, 12.5m);
        Assert.Equal(2500m, result.MarketValue);
        Assert.Equal(500m, result.UnrealizedProfitLoss);
        Assert.Equal(25m, result.ReturnPercent);
    }
    [Fact]
    public void Valuation_CalculatesLossWithDecimalPrecision()
    {
        var result = PositionCalculator.Calculate(3m, 0.9m, 0.2m);
        Assert.Equal(0.6m, result.MarketValue);
        Assert.Equal(-0.3m, result.UnrealizedProfitLoss);
    }
    [Fact]
    public void ZeroCost_HasNoDefinedReturnPercentage()
        => Assert.Null(PositionCalculator.Calculate(100m, 0, 10m).ReturnPercent);
    [Theory]
    [InlineData(-1, 10, 10)]
    [InlineData(1, -1, 10)]
    [InlineData(1, 10, -1)]
    [InlineData(0, 10, 1)]
    public void InvalidInputs_AreRejected(decimal quantity, decimal cost, decimal price)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PositionCalculator.Calculate(quantity, cost, price));
}
