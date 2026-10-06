using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class PortfolioStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IPortfolioStore
{
    private static AccountDto Map(PortfolioAccount a) => new(a.Id, a.Name, a.Currency, a.InitialCapital, a.CurrentCash,
        a.TargetCashWeight, a.TargetEtfWeight, a.TargetActiveWeight, a.TargetExperimentalWeight, a.DefaultMaxPositionWeight);

    public async Task<IReadOnlyList<AccountDto>> AccountsAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.PortfolioAccounts.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)).Select(Map).ToArray();
    }
    public Task<Guid> SaveAccountAsync(AccountDraft draft, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        var account = draft.Id is { } id ? await db.PortfolioAccounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("账户已不存在。")
            : new PortfolioAccount(draft.Name, draft.BaseCurrency);
        if (account.Currency != Guard.Currency(draft.BaseCurrency)) throw new BusinessException("已建立的账户不能更改本位币，请创建新账户。");
        account.Rename(draft.Name);
        var history = await db.Transactions.Where(x => x.PortfolioAccountId == account.Id).ToListAsync(ct);
        await EnsureLedgerBackedAsync(db, account.Id, history.Count, ct);
        account.Configure(draft.InitialCapital, draft.TargetCashWeight, draft.TargetEtfWeight, draft.TargetActiveWeight, draft.TargetExperimentalWeight, draft.DefaultMaxPositionWeight);
        if (draft.Id is null) db.PortfolioAccounts.Add(account);
        await SynchronizeAsync(db, account, history, ct);
        return account.Id;
    }, ct);

    public async Task<PortfolioSnapshot> ReadAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var read = await db.Database.BeginTransactionAsync(ct);
        var a = await db.PortfolioAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId, ct);
        var watchCount = await db.WatchlistItems.CountAsync(ct);
        if (a is null) return PortfolioSnapshot.Empty with { WatchlistCount = watchCount };
        var positions = await db.Positions.AsNoTracking().Include(x => x.Security).Where(x => x.PortfolioAccountId == a.Id).ToListAsync(ct);
        positions = positions.Where(x => x.Quantity > 0).ToList();
        var values = positions.ToDictionary(x => x.Id, x => x.Quantity * (x.Security.LatestPrice ?? x.AverageCost));
        var invested = values.Values.Sum(); var total = invested + a.CurrentCash;
        var holdings = positions.Select(p => new HoldingDto(p.SecurityId, p.Security.Symbol, p.Security.Name, p.Security.Type, p.Security.Currency,
            p.Quantity, p.AverageCost, p.Security.LatestPrice ?? p.AverageCost, p.Security.PriceDate?.ToLocalTime(), p.Security.LatestPrice is null ? "成本暂估" : "手工报价",
            values[p.Id], total == 0 ? 0 : values[p.Id] / total, values[p.Id] - p.TotalCost,
            p.TotalCost == 0 ? null : (values[p.Id] - p.TotalCost) / p.TotalCost, p.TargetWeight, p.MaxWeight, p.Bucket)).OrderByDescending(x => x.MarketValue).ToArray();
        var transactions = (await db.Transactions.AsNoTracking().Include(x => x.Security).Where(x => x.PortfolioAccountId == a.Id).ToListAsync(ct))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Sequence).Select(x => new TransactionDto(x.Id, x.OccurredAt.ToLocalTime(), x.Sequence, x.Type, x.Security?.Symbol ?? "—", x.Quantity, x.UnitPrice, x.CashAmount, x.Fees, x.Currency, x.Notes)).ToArray();
        var allocations = new List<AllocationDto> { new("现金", a.CurrentCash, total == 0 ? 0 : a.CurrentCash / total, a.TargetCashWeight) };
        foreach (var bucket in Enum.GetValues<InvestmentBucket>())
        {
            var amount = holdings.Where(x => x.Bucket == bucket).Sum(x => x.MarketValue);
            allocations.Add(new(bucket switch { InvestmentBucket.Etf => "ETF", InvestmentBucket.Active => "主动投资", _ => "观察 / 试验" }, amount,
                total == 0 ? 0 : amount / total, bucket switch { InvestmentBucket.Etf => a.TargetEtfWeight, InvestmentBucket.Active => a.TargetActiveWeight, _ => a.TargetExperimentalWeight }));
        }
        return new(Map(a), total, a.CurrentCash, invested, holdings.Sum(x => x.UnrealizedPnL), holdings.Length, watchCount,
            positions.Count(x => x.Security.LatestPrice is null), holdings, transactions, allocations);
    }
    public async Task PostAsync(TransactionDraft draft, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            var a = await AccountAsync(db, draft.AccountId, ct);
            var history = await db.Transactions.Where(x => x.PortfolioAccountId == a.Id).ToListAsync(ct);
            await EnsureLedgerBackedAsync(db, a.Id, history.Count, ct);
            var security = draft.SecurityId is { } id ? await db.Securities.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("证券不存在。") : null;
            var t = CreateTransaction(a, security, draft, history.Select(x => x.Sequence).DefaultIfEmpty().Max() + 1);
            db.Transactions.Add(t); history.Add(t);
            await SynchronizeAsync(db, a, history, ct); return true;
        }, ct);
    }
    private static Transaction CreateTransaction(PortfolioAccount a, Security? security, TransactionDraft d, long sequence)
    {
        if (Guard.Currency(d.Currency) != a.Currency) throw new BusinessException("交易币种必须与账户本位币一致，当前不提供汇率换算。");
        if (d.Date > DateTimeOffset.UtcNow) throw new BusinessException("不能录入未来交易。");
        if (d.Type is TransactionType.Buy or TransactionType.Sell && security is null) throw new BusinessException("买入或卖出必须选择证券，请先到证券库新增证券。");
        return new(a, security, d.Type, d.Quantity, d.Price, d.Amount, d.Fees, d.Date, d.Notes, sequence);
    }
    private static Task<PortfolioAccount> AccountAsync(InvestmentDbContext db, Guid id, CancellationToken ct)
        => FindAccountAsync(db, id, ct);
    private static async Task<PortfolioAccount> FindAccountAsync(InvestmentDbContext db, Guid id, CancellationToken ct)
        => await db.PortfolioAccounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("请选择有效账户。");
    private static async Task EnsureLedgerBackedAsync(InvestmentDbContext db, Guid id, int historyCount, CancellationToken ct)
    {
        if (historyCount == 0 && (await db.Positions.Where(x => x.PortfolioAccountId == id).ToListAsync(ct)).Any(x => x.Quantity > 0))
            throw new BusinessException("此账户包含 Phase 0 手工仓位但没有交易历史。请保留原账户，在新账户导入交易或期初持仓进行核对。");
    }
    private static async Task SynchronizeAsync(InvestmentDbContext db, PortfolioAccount account, IReadOnlyList<Transaction> history, CancellationToken ct)
    {
        var result = PortfolioLedger.Replay(account.InitialCapital, history);
        account.SetCash(result.Cash);
        var stored = await db.Positions.Where(x => x.PortfolioAccountId == account.Id).ToListAsync(ct);
        foreach (var balance in result.Positions)
        {
            var position = stored.SingleOrDefault(x => x.SecurityId == balance.SecurityId);
            if (position is null)
            {
                var security = await db.Securities.SingleAsync(x => x.Id == balance.SecurityId, ct);
                db.Positions.Add(new Position(account, security, balance.Quantity, balance.TotalCost));
            }
            else position.SetBalance(balance.Quantity, balance.TotalCost);
        }
        foreach (var position in stored.Where(p => result.Positions.All(x => x.SecurityId != p.SecurityId))) position.SetBalance(0, 0);
    }
    public async Task UpdatePriceAsync(Guid securityId, decimal price, DateTimeOffset date, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db => { (await db.Securities.SingleOrDefaultAsync(x => x.Id == securityId, ct) ?? throw new BusinessException("证券不存在。")).SetPrice(price, date); return true; }, ct);
    public async Task UpdatePolicyAsync(Guid accountId, Guid securityId, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db => { (await db.Positions.SingleOrDefaultAsync(x => x.PortfolioAccountId == accountId && x.SecurityId == securityId, ct) ?? throw new BusinessException("仓位不存在。")).SetPolicy(target, max, bucket); return true; }, ct);
    public async Task UpdateHoldingAsync(Guid accountId, Guid securityId, decimal price, DateTimeOffset date, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db =>
        {
            var p = await db.Positions.Include(x => x.Security).SingleOrDefaultAsync(x => x.PortfolioAccountId == accountId && x.SecurityId == securityId, ct) ?? throw new BusinessException("仓位不存在。");
            p.SetPolicy(target, max, bucket); p.Security.SetPrice(price, date); return true;
        }, ct);
    public async Task<IReadOnlyList<WatchlistDto>> WatchlistAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.WatchlistItems.AsNoTracking().Include(x => x.Security).ToListAsync(ct)).Select(x => new WatchlistDto(x.SecurityId, x.Security.Symbol,
            x.Security.Name, x.Security.Market, x.Stage, x.Priority, x.Reason, x.NextAction, x.LastResearchDate?.ToLocalTime(), x.RiskStatus, x.Note)).ToArray();
    }
    public async Task SaveWatchlistAsync(WatchlistDraft draft, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            var item = await db.WatchlistItems.SingleOrDefaultAsync(x => x.SecurityId == draft.SecurityId, ct);
            if (item is null)
            {
                var security = await db.Securities.SingleOrDefaultAsync(x => x.Id == draft.SecurityId, ct) ?? throw new BusinessException("请先创建证券。");
                item = new WatchlistItem(security); db.WatchlistItems.Add(item);
            }
            item.Edit(draft.Stage, draft.Priority, draft.Reason, draft.NextAction, draft.LastResearchDate, draft.RiskStatus, draft.Notes); return true;
        }, ct);
    }
    public async Task RemoveWatchlistAsync(Guid securityId, CancellationToken ct = default)
        => await writer.ExecuteAsync(async db => { var item = await db.WatchlistItems.SingleOrDefaultAsync(x => x.SecurityId == securityId, ct); if (item is not null) db.WatchlistItems.Remove(item); return true; }, ct);

    public async Task<ImportOutcome> ImportAsync(ImportPlan plan, bool validateOnly, CancellationToken ct = default)
    {
        try
        {
            return await writer.ExecuteAsync(async db =>
            {
                var key = "Import:" + plan.Fingerprint;
                if (await db.AppSettings.AnyAsync(x => x.Key == key, ct)) throw new BusinessException("相同内容已导入，已阻止重复写入。");
                var errors = new List<string>();
                if (plan.Kind == ImportKind.Security)
                {
                    var identities = (await db.Securities.Select(x => x.Exchange + ":" + x.Symbol).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < plan.Securities.Count; i++)
                    {
                        try
                        {
                            var d = plan.Securities[i]; var entity = new Security(d.Ticker, d.CompanyName, d.Exchange, d.Currency, d.SecurityType);
                            SecurityApplicationService.Apply(entity, d);
                            if (!identities.Add(entity.Exchange + ":" + entity.Symbol)) throw new BusinessException("重复的交易所与证券代码。");
                            db.Securities.Add(entity);
                        }
                        catch (Exception ex) when (ex is BusinessException or ArgumentException) { errors.Add($"记录 {i + 2}: {ex.Message}"); }
                    }
                }
                else
                {
                    var a = await AccountAsync(db, plan.AccountId, ct);
                    var history = await db.Transactions.Where(x => x.PortfolioAccountId == a.Id).ToListAsync(ct);
                    await EnsureLedgerBackedAsync(db, a.Id, history.Count, ct);
                    var securities = await db.Securities.ToListAsync(ct);
                    var rows = plan.Kind == ImportKind.Positions ? plan.Positions.Select(p => new ImportTransaction(p.Ticker, p.Exchange, TransactionType.Buy, p.Date, p.Quantity, p.AverageCost, 0, 0, p.Currency, "CSV 期初持仓转换为买入；成本包含历史费用")).ToArray() : plan.Transactions.ToArray();
                    long sequence = history.Select(x => x.Sequence).DefaultIfEmpty().Max();
                    for (int i = 0; i < rows.Length; i++)
                    {
                        try
                        {
                            var d = rows[i]; Security? security = null;
                            if (d.Ticker.Length > 0 || d.Exchange.Length > 0)
                                security = securities.SingleOrDefault(s => s.Symbol.Equals(d.Ticker, StringComparison.OrdinalIgnoreCase) && s.Exchange.Equals(d.Exchange, StringComparison.OrdinalIgnoreCase)) ?? throw new BusinessException("找不到证券；请先导入证券，并检查 Ticker / Exchange。");
                            var t = CreateTransaction(a, security, new(a.Id, security?.Id, d.Date, d.Type, d.Quantity, d.Price, d.Amount, d.Fees, d.Currency, d.Notes), ++sequence);
                            db.Transactions.Add(t); history.Add(t);
                        }
                        catch (Exception ex) when (ex is BusinessException or ArgumentException) { errors.Add($"记录 {i + 2}: {ex.Message}"); }
                    }
                    if (errors.Count == 0) await SynchronizeAsync(db, a, history, ct);
                }
                if (errors.Count > 0) throw new BusinessException(string.Join(Environment.NewLine, errors));
                var count = plan.Securities.Count + plan.Positions.Count + plan.Transactions.Count;
                if (count == 0) throw new BusinessException("没有可导入的记录。");
                db.AppSettings.Add(new AppSetting(key, $"{plan.Kind}:{count}"));
                return new ImportOutcome(true, count, []);
            }, ct, validateOnly);
        }
        catch (Exception ex) when (ex is BusinessException or ArgumentException or OverflowException)
        { return new(false, 0, [ex is OverflowException ? "金额超出可计算范围。" : ex.Message]); }
    }
}

