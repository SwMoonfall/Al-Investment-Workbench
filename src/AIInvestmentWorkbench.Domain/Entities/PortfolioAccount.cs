using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class PortfolioAccount : Entity
{
    private PortfolioAccount() { }
    public PortfolioAccount(string name, string currency)
    {
        Name = Guard.Text(name, nameof(name), 100);
        Currency = Guard.Currency(currency);
    }
    public string Name { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public string BaseCurrency => Currency;
    public decimal InitialCapital { get; private set; }
    public decimal CurrentCash { get; private set; }
    public decimal TargetCashWeight { get; private set; }
    public decimal TargetEtfWeight { get; private set; }
    public decimal TargetActiveWeight { get; private set; }
    public decimal TargetExperimentalWeight { get; private set; }
    public decimal DefaultMaxPositionWeight { get; private set; }
    public void Configure(decimal initialCapital, decimal cash, decimal etf, decimal active, decimal experimental, decimal maxPosition)
    {
        if (initialCapital < 0) throw new BusinessException("初始资金不能为负数。");
        Guard.Weight(cash); Guard.Weight(etf); Guard.Weight(active); Guard.Weight(experimental); Guard.Weight(maxPosition);
        if (cash + etf + active + experimental != 1m) throw new BusinessException("四类目标权重合计必须为 100%。");
        InitialCapital = initialCapital; TargetCashWeight = cash; TargetEtfWeight = etf;
        TargetActiveWeight = active; TargetExperimentalWeight = experimental; DefaultMaxPositionWeight = maxPosition;
        MarkUpdated();
    }
    public void SetCash(decimal value)
    {
        if (value < 0) throw new BusinessException("现金余额不足，不支持融资或透支。");
        CurrentCash = value; MarkUpdated();
    }
    public ICollection<Position> Positions { get; private set; } = new List<Position>();
    public ICollection<Transaction> Transactions { get; private set; } = new List<Transaction>();
    public void Rename(string name) { Name = Guard.Text(name, nameof(name), 100); MarkUpdated(); }
}
