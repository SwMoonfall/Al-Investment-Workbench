using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class Transaction : Entity
{
    private Transaction() { }
    public Transaction(PortfolioAccount account, Security? security, TransactionType type,
        decimal quantity, decimal unitPrice, decimal cashAmount, decimal fees, DateTimeOffset occurredAt,
        string notes = "", long sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (fees < 0) throw new ArgumentOutOfRangeException(nameof(fees), "手续费不能为负数。");
        if (occurredAt == default) throw new ArgumentException("交易时间不能为空。", nameof(occurredAt));
        if (type is TransactionType.Buy or TransactionType.Sell)
        {
            ArgumentNullException.ThrowIfNull(security);
            if (security.Currency != account.Currency) throw new ArgumentException("交易币种必须与账户一致。");
            if (quantity <= 0 || unitPrice <= 0 || cashAmount != 0) throw new ArgumentException("买卖数量和单价必须为正，现金转账金额必须为零。");
        }
        else if ((security is not null && type != TransactionType.Dividend) || quantity != 0 || unitPrice != 0 || cashAmount <= 0)
            throw new ArgumentException("现金转入转出不得关联证券，数量和单价必须为零，金额必须为正。");
        if (security is not null && security.Currency != account.Currency) throw new BusinessException("证券与账户币种不一致，Phase 1 不支持外汇换算。");
        if (type == TransactionType.Fee && fees != 0) throw new BusinessException("Fee 的费用填入金额，附加手续费必须为零，避免重复扣费。");
        PortfolioAccount = account; PortfolioAccountId = account.Id;
        Security = security; SecurityId = security?.Id;
        Type = type; Quantity = quantity; UnitPrice = unitPrice; CashAmount = cashAmount;
        Fees = fees; OccurredAt = occurredAt.ToUniversalTime();
        Currency = account.Currency; Notes = Guard.OptionalText(notes); Sequence = sequence;
    }
    public Guid PortfolioAccountId { get; private set; }
    public PortfolioAccount PortfolioAccount { get; private set; } = null!;
    public Guid? SecurityId { get; private set; }
    public Security? Security { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal CashAmount { get; private set; }
    public decimal Fees { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset Date => OccurredAt;
    public decimal Price => UnitPrice;
    public string Currency { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public long Sequence { get; private set; }
}
