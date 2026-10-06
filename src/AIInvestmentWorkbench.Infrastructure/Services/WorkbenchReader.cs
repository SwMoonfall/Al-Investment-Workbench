using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class WorkbenchReader(IDbContextFactory<InvestmentDbContext> factory) : IWorkbenchReader
{
    public async Task<WorkbenchSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var accounts = await context.PortfolioAccounts.AsNoTracking().Include(x => x.Positions).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var positions = await context.Positions.AsNoTracking().Include(x => x.Security).Include(x => x.PortfolioAccount).ToListAsync(cancellationToken);
        var watchlist = await context.WatchlistItems.AsNoTracking().Include(x => x.Security).ToListAsync(cancellationToken);
        return new(
            accounts.Select(x => new AccountRow(x.Id, x.Name, x.Currency, x.Positions.Count, x.Positions.Sum(p => p.TotalCost))).ToArray(),
            positions.Select(x => new PositionRow(x.PortfolioAccount.Name, x.Security.Symbol, x.Security.Name, x.Security.Currency, x.Quantity, x.AverageCost, x.TotalCost)).ToArray(),
            watchlist.Select(x => new WatchlistRow(x.Security.Symbol, x.Security.Name, x.Security.Exchange, x.Security.Currency, x.Note)).ToArray());
    }
}
