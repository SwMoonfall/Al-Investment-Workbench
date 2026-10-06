using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using Xunit;

namespace AIInvestmentWorkbench.Domain.Tests;

public class EntityTests
{
    private static Security Stock() => new("600000", "浦发银行", "XSHG", "CNY", SecurityType.Stock);
    [Fact]
    public void Security_NormalizesIdentityAndCreatesAuditFields()
    {
        var security = new Security(" aapl ", " Apple ", " xnas ", "usd", SecurityType.Stock);
        Assert.Equal("AAPL", security.Symbol);
        Assert.Equal("XNAS", security.Exchange);
        Assert.Equal("USD", security.Currency);
        Assert.NotEqual(Guid.Empty, security.Id);
        Assert.Equal(TimeSpan.Zero, security.CreatedAt.Offset);
        Assert.True(security.UpdatedAt >= security.CreatedAt);
    }
    [Theory]
    [InlineData("", "Name", "XSHG", "CNY")]
    [InlineData("ABC", " ", "XSHG", "CNY")]
    [InlineData("ABC", "Name", "", "CNY")]
    [InlineData("ABC", "Name", "XSHG", "US")]
    [InlineData("ABC", "Name", "XSHG", "123")]
    public void Security_RejectsInvalidData(string symbol, string name, string exchange, string currency)
        => Assert.Throws<ArgumentException>(() => new Security(symbol, name, exchange, currency, SecurityType.Stock));
    [Fact]
    public void Security_RejectsUnknownType()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Security("ABC", "Name", "XNAS", "USD", (SecurityType)99));
    [Theory]
    [InlineData("", "CNY")]
    [InlineData(" ", "USD")]
    [InlineData("账户", "INVALID")]
    public void Account_RejectsInvalidData(string name, string currency)
        => Assert.Throws<ArgumentException>(() => new PortfolioAccount(name, currency));
    [Fact]
    public void Account_RenamePreservesIdentityAndCreationTime()
    {
        var account = new PortfolioAccount(" 主账户 ", "cny");
        var id = account.Id; var created = account.CreatedAt;
        account.Rename("长期账户");
        Assert.Equal("长期账户", account.Name);
        Assert.Equal("CNY", account.Currency);
        Assert.Equal(id, account.Id); Assert.Equal(created, account.CreatedAt);
        Assert.True(account.UpdatedAt >= created);
    }
    [Fact]
    public void Position_ComputesAverageCostAndHandlesEmptyBalance()
    {
        var position = new Position(new PortfolioAccount("主账户", "CNY"), Stock(), 200m, 2460m);
        Assert.Equal(12.3m, position.AverageCost);
        position.SetBalance(0, 0);
        Assert.Equal(0, position.AverageCost);
    }
    [Theory]
    [InlineData(-1, 100)]
    [InlineData(1, -100)]
    [InlineData(0, 100)]
    public void Position_RejectsInvalidBalance(decimal quantity, decimal totalCost)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Position(new PortfolioAccount("账户", "CNY"), Stock(), quantity, totalCost));
    [Fact]
    public void Position_RejectsMixedCurrencies()
        => Assert.Throws<ArgumentException>(() => new Position(new PortfolioAccount("账户", "USD"), Stock(), 1, 1));
    [Fact]
    public void CashTransaction_CannotHaveASecurity()
        => Assert.Throws<ArgumentException>(() => new Transaction(new PortfolioAccount("账户", "CNY"), Stock(), TransactionType.Deposit, 0, 0, 100, 0, DateTimeOffset.UtcNow));
    [Fact]
    public void BuyTransaction_RequiresSecurity()
        => Assert.Throws<ArgumentNullException>(() => new Transaction(new PortfolioAccount("账户", "CNY"), null, TransactionType.Buy, 100, 10, 0, 1, DateTimeOffset.UtcNow));
}
