using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Application.Services;

public sealed class PortfolioApplicationService(IPortfolioStore store)
{
    public Task<IReadOnlyList<AccountDto>> AccountsAsync(CancellationToken ct = default) => store.AccountsAsync(ct);
    public Task<Guid> SaveAccountAsync(AccountDraft draft, CancellationToken ct = default) => store.SaveAccountAsync(draft, ct);
    public Task<PortfolioSnapshot> ReadAsync(Guid id, CancellationToken ct = default) => store.ReadAsync(id, ct);
    public Task PostAsync(TransactionDraft draft, CancellationToken ct = default)
    {
        if (draft.Date > DateTimeOffset.UtcNow) throw new BusinessException("不能录入未来交易。");
        return store.PostAsync(draft, ct);
    }
    public Task UpdatePriceAsync(Guid id, decimal price, DateTimeOffset date, CancellationToken ct = default) => store.UpdatePriceAsync(id, price, date, ct);
    public Task UpdateHoldingAsync(Guid accountId, Guid id, decimal price, DateTimeOffset date, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default)
        => store.UpdateHoldingAsync(accountId, id, price, date, target, max, bucket, ct);
    public Task UpdatePolicyAsync(Guid accountId, Guid id, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default)
        => store.UpdatePolicyAsync(accountId, id, target, max, bucket, ct);
    public Task<IReadOnlyList<WatchlistDto>> WatchlistAsync(CancellationToken ct = default) => store.WatchlistAsync(ct);
    public Task SaveWatchlistAsync(WatchlistDraft draft, CancellationToken ct = default) => store.SaveWatchlistAsync(draft, ct);
    public Task RemoveWatchlistAsync(Guid id, CancellationToken ct = default) => store.RemoveWatchlistAsync(id, ct);
}
