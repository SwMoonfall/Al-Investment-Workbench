namespace AIInvestmentWorkbench.Application.Interfaces;

public enum AppTheme { Light, Dark }

public interface ISettingsService
{
    Task<AppTheme> GetThemeAsync(CancellationToken cancellationToken = default);
    Task SaveThemeAsync(AppTheme theme, CancellationToken cancellationToken = default);
}

public interface IWorkbenchReader
{
    Task<WorkbenchSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record AccountRow(Guid Id, string Name, string Currency, int PositionCount, decimal TotalCost);
public sealed record PositionRow(string Account, string Symbol, string Name, string Currency, decimal Quantity, decimal AverageCost, decimal TotalCost);
public sealed record WatchlistRow(string Symbol, string Name, string Exchange, string Currency, string Note);
public sealed record WorkbenchSnapshot(IReadOnlyList<AccountRow> Accounts, IReadOnlyList<PositionRow> Positions, IReadOnlyList<WatchlistRow> Watchlist);
