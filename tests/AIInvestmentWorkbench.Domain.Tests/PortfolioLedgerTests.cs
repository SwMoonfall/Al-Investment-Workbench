using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;

namespace AIInvestmentWorkbench.Domain.Tests;
public class PortfolioLedgerTests
{
    private readonly PortfolioAccount _account = new("账户", "CNY");
    private readonly Security _security = new("TEST", "测试证券", "XSHG", "CNY", SecurityType.Stock);
    private static readonly DateTimeOffset Day = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private Transaction Trade(TransactionType type, decimal quantity = 0, decimal price = 0, decimal amount = 0, decimal fees = 0, int sequence = 1)
        => new(_account, type is TransactionType.Buy or TransactionType.Sell or TransactionType.Dividend ? _security : null,
            type, quantity, price, amount, fees, Day, sequence: sequence);
    [Fact]
    public void EmptyAccount_HasOnlyInitialCash()
    {
        var result = PortfolioLedger.Replay(500000, []);
        Assert.Equal(500000, result.Cash); Assert.Empty(result.Positions); Assert.Equal(0, result.RealizedPnL);
    }
    [Fact]
    public void Buy_IncreasesQuantityAndCapitalizesFees()
    {
        var result = PortfolioLedger.Replay(500000, [Trade(TransactionType.Buy, 100, 10, fees: 5)]);
        var p = Assert.Single(result.Positions);
        Assert.Equal(100, p.Quantity); Assert.Equal(1005, p.TotalCost); Assert.Equal(10.05m, p.AverageCost); Assert.Equal(498995, result.Cash);
    }
    [Fact]
    public void MultipleBuys_UseWeightedAverageCost()
    {
        var result = PortfolioLedger.Replay(10000, [Trade(TransactionType.Buy, 100, 10, fees: 10), Trade(TransactionType.Buy, 50, 20, fees: 5, sequence: 2)]);
        var p = Assert.Single(result.Positions);
        Assert.Equal(150, p.Quantity); Assert.Equal(2015, p.TotalCost); Assert.Equal(2015m/150m, p.AverageCost); Assert.Equal(7985, result.Cash);
    }
    [Fact]
    public void PartialSell_RetainsAverageCostAndAccountsForSellingFees()
    {
        var result = PortfolioLedger.Replay(10000, [Trade(TransactionType.Buy, 100, 10, fees: 10), Trade(TransactionType.Sell, 40, 15, fees: 2, sequence: 2)]);
        var p = Assert.Single(result.Positions);
        Assert.Equal(60, p.Quantity); Assert.Equal(606, p.TotalCost); Assert.Equal(10.1m, p.AverageCost);
        Assert.Equal(9588, result.Cash); Assert.Equal(194, result.RealizedPnL);
    }
    [Fact]
    public void FullLiquidation_ClearsCostWithoutRoundingResidual()
    {
        var result = PortfolioLedger.Replay(1000, [Trade(TransactionType.Buy, 3, 1, fees: .01m), Trade(TransactionType.Sell, 1, 2, sequence: 2), Trade(TransactionType.Sell, 2, 2, sequence: 3)]);
        var p = Assert.Single(result.Positions); Assert.Equal(0, p.Quantity); Assert.Equal(0, p.TotalCost); Assert.Equal(0, p.AverageCost);
        Assert.Equal(1002.99m, result.Cash); Assert.Equal(2.99m, result.RealizedPnL);
    }
    [Fact]
    public void RebuyAfterLiquidation_StartsNewCostBasis()
    {
        var result = PortfolioLedger.Replay(1000, [Trade(TransactionType.Buy, 10, 5), Trade(TransactionType.Sell, 10, 6, sequence: 2), Trade(TransactionType.Buy, 2, 8, sequence: 3)]);
        Assert.Equal(8, Assert.Single(result.Positions).AverageCost); Assert.Equal(994, result.Cash);
    }
    [Fact]
    public void CashFlows_DividendsAndFeesAreCountedOnce()
    {
        var result = PortfolioLedger.Replay(1000, [Trade(TransactionType.Deposit, amount: 200, fees: 2), Trade(TransactionType.Withdraw, amount: 100, fees: 1, sequence: 2), Trade(TransactionType.Dividend, amount: 50, fees: 3, sequence: 3), Trade(TransactionType.Fee, amount: 10, sequence: 4)]);
        Assert.Equal(1134, result.Cash); Assert.Equal(34, result.RealizedPnL); Assert.Empty(result.Positions);
    }
    [Fact]
    public void Oversell_IsRejected() => Assert.Throws<BusinessException>(() => PortfolioLedger.Replay(1000, [Trade(TransactionType.Buy, 10, 5), Trade(TransactionType.Sell, 11, 5, sequence: 2)]));
    [Fact]
    public void InsufficientCashIncludingFees_IsRejected() => Assert.Throws<BusinessException>(() => PortfolioLedger.Replay(100, [Trade(TransactionType.Buy, 10, 10, fees: .01m)]));
    [Fact]
    public void BackdatedSellBeforeBuy_IsRejected()
    {
        var sell = new Transaction(_account, _security, TransactionType.Sell, 1, 10, 0, 0, Day.AddDays(-1), sequence: 2);
        Assert.Throws<BusinessException>(() => PortfolioLedger.Replay(1000, [Trade(TransactionType.Buy, 1, 10), sell]));
    }
    [Fact]
    public void SameDayTrades_RespectSequenceNotInputOrder()
    {
        var result = PortfolioLedger.Replay(1000, [Trade(TransactionType.Sell, 10, 6, sequence: 2), Trade(TransactionType.Buy, 10, 5)]);
        Assert.Equal(1010, result.Cash);
    }
    [Fact]
    public void AccountTargets_AreEditableButMustSumToOne()
    {
        _account.Configure(500000, .2m, .3m, .4m, .1m, .15m);
        Assert.Equal(.2m, _account.TargetCashWeight);
        Assert.Throws<BusinessException>(() => _account.Configure(500000, .2m, .3m, .4m, .2m, .15m));
    }
    [Fact]
    public void FeeCannotHaveAdditionalFees() => Assert.Throws<BusinessException>(() => Trade(TransactionType.Fee, amount: 10, fees: 1));
    [Fact]
    public void RmbIsNormalizedToCny() => Assert.Equal("CNY", new PortfolioAccount("账户", "RMB").BaseCurrency);
}
