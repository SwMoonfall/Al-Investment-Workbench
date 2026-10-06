using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Enums;

namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class Position : Entity
{
    private Position() { }
    public Position(PortfolioAccount account, Security security, decimal quantity, decimal totalCost)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(security);
        if (account.Currency != security.Currency) throw new ArgumentException("Phase 0 仓位币种必须与账户一致。");
        PortfolioAccountId = account.Id;
        SecurityId = security.Id;
        PortfolioAccount = account;
        Security = security;
        Bucket = security.Type == SecurityType.Etf ? InvestmentBucket.Etf : InvestmentBucket.Active;
        MaxWeight = account.DefaultMaxPositionWeight;
        SetBalance(quantity, totalCost);
    }
    public Guid PortfolioAccountId { get; private set; }
    public Guid SecurityId { get; private set; }
    public PortfolioAccount PortfolioAccount { get; private set; } = null!;
    public Security Security { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal AverageCost => Quantity == 0 ? 0 : TotalCost / Quantity;
    public decimal TargetWeight { get; private set; }
    public decimal MaxWeight { get; private set; }
    public InvestmentBucket Bucket { get; private set; }
    public void SetPolicy(decimal target, decimal max, InvestmentBucket bucket)
    {
        Guard.Weight(target); Guard.Weight(max);
        if (target > max) throw new BusinessException("目标权重不能超过最大权重。");
        if (!Enum.IsDefined(bucket)) throw new BusinessException("无效资产分类。");
        TargetWeight = target; MaxWeight = max; Bucket = bucket; MarkUpdated();
    }
    public void SetBalance(decimal quantity, decimal totalCost)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (totalCost < 0 || (quantity == 0 && totalCost != 0)) throw new ArgumentOutOfRangeException(nameof(totalCost));
        Quantity = quantity;
        TotalCost = totalCost;
        MarkUpdated();
    }
}
