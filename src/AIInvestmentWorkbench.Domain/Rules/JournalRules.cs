using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
namespace AIInvestmentWorkbench.Domain.Rules;

public static class JournalRules
{
    public static void Validate(JournalContent c)
    {
        if (!Enum.IsDefined(c.Action) || c.Date == default || c.Date > DateOnly.FromDateTime(DateTime.Today)) throw new BusinessException("日志日期不能是未来，动作必须有效。");
        if (c.Price < 0 || c.Quantity < 0 || c.Confidence is < 0 or > 100 || c.ExpectedHoldingDays is < 1 or > 36500) throw new BusinessException("价格、数量不得为负；信心为 0–100，预计天数为 1–36500。");
        if (c.PortfolioWeight.HasValue) Guard.Weight(c.PortfolioWeight.Value);
        Guard.Text(c.Reason, "理由", 20000);
        foreach (var value in new[] { c.MarketConcern, c.WhyMarketMayBeWrong, c.ExpectedDevelopment, c.KillConditionSummary, c.ExpectedHoldingPeriod, c.Emotion, c.Notes }) Guard.OptionalText(value, 20000);
    }
    public static (DateOnly Start, DateOnly End) Period(ReviewKind kind, DateOnly anchor)
    {
        var start = kind switch { ReviewKind.Weekly => anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)), ReviewKind.Monthly => new(anchor.Year, anchor.Month, 1), ReviewKind.Quarterly => new(anchor.Year, ((anchor.Month - 1) / 3) * 3 + 1, 1), _ => throw new BusinessException("复盘类型无效。") };
        return (start, kind == ReviewKind.Weekly ? start.AddDays(6) : start.AddMonths(kind == ReviewKind.Monthly ? 1 : 3).AddDays(-1));
    }
    public static (DateOnly Start, DateOnly End) LastCompletedQuarter(DateOnly today) => Period(ReviewKind.Quarterly, Period(ReviewKind.Quarterly, today).Start.AddDays(-1));
    public static bool QuarterlyDue(DateOnly today, DateOnly end, ReviewStatus? latestStatus) => today > end && latestStatus != ReviewStatus.Completed;
    // Quantity-weighted FIFO duration for sold shares only; cash movements do not enter this statistic.
    public static decimal? AverageClosedHoldingDays(IEnumerable<Transaction> transactions)
    {
        decimal days = 0, sold = 0;
        foreach (var group in transactions.Where(x => x.SecurityId.HasValue).GroupBy(x => (x.PortfolioAccountId, x.SecurityId)))
        {
            var lots = new Queue<(DateOnly Date, decimal Quantity)>();
            foreach (var t in group.OrderBy(x => x.OccurredAt).ThenBy(x => x.Sequence))
            {
                var date = DateOnly.FromDateTime(t.OccurredAt.ToLocalTime().DateTime);
                if (t.Type == TransactionType.Buy) lots.Enqueue((date, t.Quantity));
                if (t.Type != TransactionType.Sell) continue;
                var remaining = t.Quantity;
                while (remaining > 0)
                {
                    if (!lots.TryPeek(out var lot)) return null;
                    var quantity = Math.Min(remaining, lot.Quantity); remaining -= quantity; sold += quantity; days += quantity * (date.DayNumber - lot.Date.DayNumber);
                    if (quantity == lot.Quantity) lots.Dequeue();
                    else { var tail = lots.Skip(1).ToArray(); lots.Clear(); lots.Enqueue((lot.Date, lot.Quantity - quantity)); foreach (var item in tail) lots.Enqueue(item); }
                }
            }
        }
        return sold == 0 ? null : days / sold;
    }
}
