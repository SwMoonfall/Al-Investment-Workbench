using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;

namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record AccountDraft(Guid? Id, string Name, string BaseCurrency, decimal InitialCapital,
    decimal TargetCashWeight, decimal TargetEtfWeight, decimal TargetActiveWeight, decimal TargetExperimentalWeight, decimal DefaultMaxPositionWeight);
public sealed record SecurityDraft(Guid? Id, string Ticker, string CompanyName, Market Market, string Exchange,
    string Currency, SecurityType SecurityType, string Sector = "", string Industry = "", string Country = "", string? ISIN = null, string Notes = "");
public sealed record TransactionDraft(Guid AccountId, Guid? SecurityId, DateTimeOffset Date, TransactionType Type,
    decimal Quantity, decimal Price, decimal Amount, decimal Fees, string Currency, string Notes = "");
public sealed record WatchlistDraft(Guid SecurityId, WatchlistStage Stage, WatchlistPriority Priority, string Reason,
    string NextAction, DateTimeOffset? LastResearchDate, RiskStatus RiskStatus, string Notes);
public sealed record AccountDto(Guid Id, string Name, string BaseCurrency, decimal InitialCapital, decimal CurrentCash,
    decimal TargetCashWeight, decimal TargetEtfWeight, decimal TargetActiveWeight, decimal TargetExperimentalWeight, decimal DefaultMaxPositionWeight)
{
    public override string ToString() => Name;
}
public sealed record HoldingDto(Guid SecurityId, string Ticker, string CompanyName, SecurityType SecurityType, string Currency,
    decimal Quantity, decimal AverageCost, decimal LatestPrice, DateTimeOffset? PriceDate, string PriceSource,
    decimal MarketValue, decimal Weight, decimal UnrealizedPnL, decimal? UnrealizedPnLPercent,
    decimal TargetWeight, decimal MaxWeight, InvestmentBucket Bucket)
{
    public string LimitStatus => Weight > MaxWeight ? "超出上限" : "正常";
}
public sealed record TransactionDto(Guid Id, DateTimeOffset Date, long Sequence, TransactionType Type, string Ticker,
    decimal Quantity, decimal Price, decimal Amount, decimal Fees, string Currency, string Notes);
public sealed record AllocationDto(string Name, decimal Value, decimal Weight, decimal TargetWeight);
public sealed record WatchlistDto(Guid SecurityId, string Ticker, string CompanyName, Market Market, WatchlistStage Stage,
    WatchlistPriority Priority, string Reason, string NextAction, DateTimeOffset? LastResearchDate, RiskStatus RiskStatus, string Notes);
public sealed record PortfolioSnapshot(AccountDto? Account, decimal TotalAssets, decimal Cash, decimal InvestedAssets,
    decimal UnrealizedPnL, int PositionCount, int WatchlistCount, int EstimatedPriceCount,
    IReadOnlyList<HoldingDto> Holdings, IReadOnlyList<TransactionDto> Transactions, IReadOnlyList<AllocationDto> Allocation)
{
    public static PortfolioSnapshot Empty { get; } = new(null, 0, 0, 0, 0, 0, 0, 0, [], [], []);
    public string ValuationNotice => EstimatedPriceCount == 0 ? "按已录入价格估值；请检查报价日期。" : $"{EstimatedPriceCount} 项缺少报价，暂用成本估值；总资产与浮盈亏为暂估。";
}

public interface ISecurityRepository
{
    Task<IReadOnlyList<Security>> SearchAsync(string query, CancellationToken ct = default);
    Task<Security?> GetAsync(Guid id, CancellationToken ct = default);
    Task SaveAsync(Security security, bool isNew, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
public interface IPortfolioStore
{
    Task<IReadOnlyList<AccountDto>> AccountsAsync(CancellationToken ct = default);
    Task<Guid> SaveAccountAsync(AccountDraft draft, CancellationToken ct = default);
    Task<PortfolioSnapshot> ReadAsync(Guid accountId, CancellationToken ct = default);
    Task PostAsync(TransactionDraft draft, CancellationToken ct = default);
    Task UpdatePriceAsync(Guid securityId, decimal price, DateTimeOffset date, CancellationToken ct = default);
    Task UpdateHoldingAsync(Guid accountId, Guid securityId, decimal price, DateTimeOffset date, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default);
    Task UpdatePolicyAsync(Guid accountId, Guid securityId, decimal target, decimal max, InvestmentBucket bucket, CancellationToken ct = default);
    Task<IReadOnlyList<WatchlistDto>> WatchlistAsync(CancellationToken ct = default);
    Task SaveWatchlistAsync(WatchlistDraft draft, CancellationToken ct = default);
    Task RemoveWatchlistAsync(Guid securityId, CancellationToken ct = default);
    Task<ImportOutcome> ImportAsync(ImportPlan plan, bool validateOnly, CancellationToken ct = default);
}

public enum ImportKind { Security, Positions, Transactions }
public sealed record CsvDocument(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);
public sealed record ImportPosition(string Ticker, string Exchange, decimal Quantity, decimal AverageCost, DateTimeOffset Date, string Currency);
public sealed record ImportTransaction(string Ticker, string Exchange, TransactionType Type, DateTimeOffset Date, decimal Quantity,
    decimal Price, decimal Amount, decimal Fees, string Currency, string Notes);
public sealed record ImportPlan(ImportKind Kind, Guid AccountId, string Fingerprint, IReadOnlyList<SecurityDraft> Securities,
    IReadOnlyList<ImportPosition> Positions, IReadOnlyList<ImportTransaction> Transactions);
public sealed record ImportOutcome(bool Success, int RowCount, IReadOnlyList<string> Errors);
