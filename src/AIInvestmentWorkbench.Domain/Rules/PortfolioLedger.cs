using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;

namespace AIInvestmentWorkbench.Domain.Rules;

public sealed record LedgerPosition(Guid SecurityId, decimal Quantity, decimal TotalCost)
{
    public decimal AverageCost => Quantity == 0 ? 0 : TotalCost / Quantity;
}
public sealed record LedgerResult(decimal Cash, decimal RealizedPnL, IReadOnlyList<LedgerPosition> Positions);

/// <summary>Long-only, same-currency weighted-average book. Fees included in purchase cost.</summary>
public static class PortfolioLedger
{
    public static LedgerResult Replay(decimal initialCapital, IEnumerable<Transaction> transactions)
    {
        if (initialCapital < 0) throw new BusinessException("初始资金不能为负数。");
        decimal cash = initialCapital, realized = 0;
        var positions = new Dictionary<Guid, LedgerPosition>();
        foreach (var t in transactions.OrderBy(x => x.OccurredAt).ThenBy(x => x.Sequence).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var key = t.SecurityId ?? Guid.Empty;
            var position = positions.GetValueOrDefault(key) ?? new LedgerPosition(key, 0, 0);
            var gross = checked(t.Quantity * t.UnitPrice);
            switch (t.Type)
            {
                case TransactionType.Buy:
                    cash -= gross + t.Fees;
                    positions[key] = position with { Quantity = position.Quantity + t.Quantity, TotalCost = position.TotalCost + gross + t.Fees };
                    break;
                case TransactionType.Sell:
                    if (t.Quantity > position.Quantity) throw new BusinessException($"{t.OccurredAt.ToLocalTime():yyyy-MM-dd} 卖出数量超过当时可用持仓。");
                    var remaining = position.Quantity - t.Quantity;
                    var removedCost = remaining == 0 ? position.TotalCost : position.AverageCost * t.Quantity;
                    cash += gross - t.Fees; realized += gross - t.Fees - removedCost;
                    positions[key] = position with { Quantity = remaining, TotalCost = remaining == 0 ? 0 : position.TotalCost - removedCost };
                    break;
                case TransactionType.Deposit: cash += t.CashAmount - t.Fees; realized -= t.Fees; break;
                case TransactionType.Withdraw: cash -= t.CashAmount + t.Fees; realized -= t.Fees; break;
                case TransactionType.Dividend: cash += t.CashAmount - t.Fees; realized += t.CashAmount - t.Fees; break;
                case TransactionType.Fee: cash -= t.CashAmount; realized -= t.CashAmount; break;
                default: throw new BusinessException("未知交易类型。");
            }
            if (cash < 0) throw new BusinessException($"{t.OccurredAt.ToLocalTime():yyyy-MM-dd} 交易后现金不足；请检查初始资金、交易日期、金额和手续费。");
        }
        return new(cash, realized, positions.Values.ToArray());
    }
}

