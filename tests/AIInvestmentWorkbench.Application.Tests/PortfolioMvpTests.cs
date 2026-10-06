using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIInvestmentWorkbench.Application.Tests;
public sealed class PortfolioMvpTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "AIInvestmentWorkbench.Mvp.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!;
    private PortfolioStore _store = null!;
    private SecurityApplicationService _securities = null!;
    private CsvImportService _csv = null!;
    private Guid _account, _security;
    private static DateTimeOffset Day => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories();
        _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var writer = new DatabaseWriter(_factory); _store = new(_factory, writer);
        _securities = new(new SecurityRepository(_factory, writer)); _csv = new(_store);
        _account = await _store.SaveAccountAsync(new(null, "50 万账户", "CNY", 500000, .1m, .4m, .4m, .1m, .1m));
        _security = await _securities.SaveAsync(new(null, "TEST", "测试公司", Market.ChinaA, "XSHG", "CNY", SecurityType.Stock));
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private TransactionDraft Buy(decimal quantity = 100, decimal price = 10, decimal fees = 5) => new(_account, _security, Day, TransactionType.Buy, quantity, price, 0, fees, "CNY");
    private Task<ImportOutcome> Import(string text, ImportKind kind, bool preview = false)
    {
        var doc = CsvImportService.Parse(text);
        return _csv.ExecuteAsync(doc, kind, _account, doc.Headers.ToDictionary(x => x, x => x), preview);
    }
    [Fact]
    public async Task VerticalFlow_PersistsAcrossNewServiceAndContext()
    {
        await _store.PostAsync(Buy());
        await _store.UpdateHoldingAsync(_account, _security, 12, Day, .05m, .15m, InvestmentBucket.Active);
        var reopened = new PortfolioStore(_factory, new DatabaseWriter(_factory));
        var snapshot = await reopened.ReadAsync(_account);
        Assert.Equal(498995, snapshot.Cash); Assert.Equal(1200, snapshot.InvestedAssets); Assert.Equal(500195, snapshot.TotalAssets);
        var p = Assert.Single(snapshot.Holdings); Assert.Equal(195, p.UnrealizedPnL);
        Assert.Equal(195m/1005m, p.UnrealizedPnLPercent); Assert.Equal(1200m/500195m, p.Weight); Assert.Equal(Day, p.PriceDate);
        Assert.Equal(.05m, p.TargetWeight); Assert.Single(snapshot.Transactions); Assert.Equal(1m, snapshot.Allocation.Sum(x => x.Weight));
    }
    [Fact]
    public async Task MissingPrice_IsExplicitlyCostEstimated()
    {
        await _store.PostAsync(Buy()); var s = await _store.ReadAsync(_account);
        Assert.Equal(1, s.EstimatedPriceCount); Assert.Equal("成本暂估", Assert.Single(s.Holdings).PriceSource); Assert.Equal(500000, s.TotalAssets);
    }
    [Fact]
    public async Task FailedTrade_RollsBackCashPositionAndHistory()
    {
        await _store.PostAsync(Buy());
        await Assert.ThrowsAsync<BusinessException>(() => _store.PostAsync(Buy() with { Type = TransactionType.Sell, Quantity = 101 }));
        var s = await _store.ReadAsync(_account); Assert.Equal(498995, s.Cash); Assert.Single(s.Transactions); Assert.Equal(100, Assert.Single(s.Holdings).Quantity);
    }
    [Fact]
    public async Task CurrencyMismatch_IsRejectedWithoutPosting()
    {
        await Assert.ThrowsAsync<BusinessException>(() => _store.PostAsync(Buy() with { Currency = "USD" }));
        Assert.Empty((await _store.ReadAsync(_account)).Transactions);
    }
    [Fact]
    public async Task ClosingAllPositions_LeavesCashAndHistory()
    {
        await _store.PostAsync(Buy());
        await _store.PostAsync(Buy() with { Type = TransactionType.Sell, Price = 12, Fees = 2 });
        var s = await _store.ReadAsync(_account); Assert.Empty(s.Holdings); Assert.Equal(500193, s.TotalAssets); Assert.Equal(2, s.Transactions.Count);
    }
    [Fact]
    public async Task InvalidCombinedPriceAndPolicy_DoesNotPartiallySave()
    {
        await _store.PostAsync(Buy());
        await Assert.ThrowsAsync<BusinessException>(() => _store.UpdateHoldingAsync(_account, _security, 99, Day, .3m, .1m, InvestmentBucket.Active));
        Assert.Null((await _securities.GetAsync(_security))!.LatestPrice);
    }
    [Fact]
    public async Task SecurityCrud_SearchAndHistoricalProtection()
    {
        var id = await _securities.SaveAsync(new(null, "OTHER", "另一个公司", Market.US, "XNAS", "USD", SecurityType.Other));
        await _securities.SaveAsync(new(id, "OTHER", "新公司名", Market.US, "XNAS", "USD", SecurityType.Stock, "科技"));
        Assert.Single(await _securities.SearchAsync("新公司"));
        await _securities.DeleteAsync(id); Assert.Null(await _securities.GetAsync(id));
        await _store.PostAsync(Buy()); await Assert.ThrowsAsync<BusinessException>(() => _securities.DeleteAsync(_security));
        await Assert.ThrowsAsync<BusinessException>(() => _securities.SaveAsync(new(_security, "TEST", "测试", Market.ChinaA, "XSHG", "USD", SecurityType.Stock)));
    }
    [Fact]
    public async Task Watchlist_UpdatesWithoutDuplicateAndSurvivesReload()
    {
        var draft = new WatchlistDraft(_security, WatchlistStage.Candidate, WatchlistPriority.Normal, "理由", "看年报", Day, RiskStatus.Unknown, "备注");
        await _store.SaveWatchlistAsync(draft);
        await _store.SaveWatchlistAsync(draft with { Stage = WatchlistStage.Researching, Priority = WatchlistPriority.High });
        var item = Assert.Single(await _store.WatchlistAsync()); Assert.Equal(WatchlistStage.Researching, item.Stage); Assert.Equal(WatchlistPriority.High, item.Priority);
        await _store.RemoveWatchlistAsync(_security); Assert.Empty(await _store.WatchlistAsync()); Assert.NotNull(await _securities.GetAsync(_security));
    }
    [Fact]
    public async Task CsvSecurity_ValidatesWithoutWritingAndPreventsDuplicateBatch()
    {
        const string csv = "Ticker,CompanyName,Market,Exchange,Currency,SecurityType\nETFTEST,测试ETF,ChinaA,XSHG,CNY,Etf";
        Assert.True((await Import(csv, ImportKind.Security, true)).Success); Assert.Single(await _securities.SearchAsync());
        Assert.True((await Import(csv, ImportKind.Security)).Success); Assert.Equal(2, (await _securities.SearchAsync()).Count);
        Assert.False((await Import(csv, ImportKind.Security)).Success);
    }
    [Fact]
    public async Task CsvSecurity_OneDuplicateRollsBackEntireBatch()
    {
        var result = await Import("Ticker,CompanyName,Market,Exchange,Currency,SecurityType\nNEW,新证券,ChinaA,XSHG,CNY,Stock\nTEST,重复,ChinaA,XSHG,CNY,Stock", ImportKind.Security);
        Assert.False(result.Success); Assert.Contains("记录 3", string.Join("", result.Errors)); Assert.Single(await _securities.SearchAsync());
    }
    [Fact]
    public async Task CsvPositions_CreatesHistoryAndDecreasesCash()
    {
        var result = await Import("Ticker,Exchange,Quantity,AverageCost,Date,Currency\nTEST,XSHG,100,10.25,2026-01-01,CNY", ImportKind.Positions);
        Assert.True(result.Success); var s = await _store.ReadAsync(_account); Assert.Single(s.Transactions); Assert.Equal(498975, s.Cash); Assert.Equal(10.25m, Assert.Single(s.Holdings).AverageCost);
    }
    [Fact]
    public async Task CsvTransactions_ValidatesEntireLedgerAndRollsBackInsufficientCash()
    {
        var result = await Import("Ticker,Exchange,Type,Date,Quantity,Price,Amount,Fees,Currency\nTEST,XSHG,Buy,2026-01-01,100,10,0,5,CNY\nTEST,XSHG,Buy,2026-01-02,999999,10,0,0,CNY", ImportKind.Transactions);
        Assert.False(result.Success); var s = await _store.ReadAsync(_account); Assert.Empty(s.Transactions); Assert.Empty(s.Holdings); Assert.Equal(500000, s.Cash);
    }
    [Fact]
    public async Task CsvTransactions_RecordsValidMixedFlows()
    {
        var result = await Import("Ticker,Exchange,Type,Date,Quantity,Price,Amount,Fees,Currency\n,,Deposit,2026-01-01,0,0,1000,0,CNY\nTEST,XSHG,Buy,2026-01-02,100,10,0,5,CNY\nTEST,XSHG,Sell,2026-01-03,40,12,0,2,CNY", ImportKind.Transactions);
        Assert.True(result.Success); var s = await _store.ReadAsync(_account); Assert.Equal(500473, s.Cash); Assert.Equal(60, Assert.Single(s.Holdings).Quantity); Assert.Equal(3, s.Transactions.Count);
    }
    [Fact]
    public async Task ConcurrentPosts_AreSerializedWithoutLostUpdates()
    {
        await Task.WhenAll(Task.Run(() => _store.PostAsync(Buy(1,10,0))), Task.Run(() => _store.PostAsync(Buy(2,10,0))));
        var s = await _store.ReadAsync(_account); Assert.Equal(3, Assert.Single(s.Holdings).Quantity); Assert.Equal(499970, s.Cash); Assert.Equal(2, s.Transactions.Select(x => x.Sequence).Distinct().Count());
    }
    [Fact]
    public async Task CsvCustomHeaderMapping_IsUsedForAllRows()
    {
        var doc = CsvImportService.Parse("代码,交易所,股数,成本,日期,币种\nTEST,XSHG,12,10.25,2026-01-01,CNY");
        var mapping = new Dictionary<string,string> { ["Ticker"]="代码", ["Exchange"]="交易所", ["Quantity"]="股数", ["AverageCost"]="成本", ["Date"]="日期", ["Currency"]="币种" };
        var result = await _csv.ExecuteAsync(doc, ImportKind.Positions, _account, mapping, false);
        Assert.True(result.Success); Assert.Equal(12, Assert.Single((await _store.ReadAsync(_account)).Holdings).Quantity);
    }
    [Fact]
    public async Task CsvMissingMapping_DoesNotWriteAnything()
    {
        var doc = CsvImportService.Parse("Ticker,Exchange,Quantity,AverageCost,Date,Currency\nTEST,XSHG,12,10,2026-01-01,CNY");
        var result = await _csv.ExecuteAsync(doc, ImportKind.Positions, _account, new Dictionary<string,string>(), false);
        Assert.False(result.Success); Assert.Empty((await _store.ReadAsync(_account)).Transactions);
    }
    [Fact]
    public async Task CsvAmbiguousDecimal_IsRejectedInsteadOfChangingItsMeaning()
    {
        var result = await Import("Ticker,Exchange,Quantity,AverageCost,Date,Currency\nTEST,XSHG,\"1,2\",10,2026-01-01,CNY", ImportKind.Positions);
        Assert.False(result.Success); Assert.Empty((await _store.ReadAsync(_account)).Transactions);
    }
    [Fact]
    public async Task UpgradeFromPhase0_PreservesRecordsAndMakesBackup()
    {
        var paths = new AppPaths(Path.Combine(_paths.Root, "upgrade")); paths.EnsureDirectories();
        var factory = new Factory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)).Options);
        var accountId = Guid.NewGuid(); var securityId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using (var old = factory.CreateDbContext())
        {
            await old.GetService<IMigrator>().MigrateAsync("20261002200827_InitialCreate");
            await old.Database.ExecuteSqlRawAsync("INSERT INTO PortfolioAccounts (Id, Name, Currency, CreatedAt, UpdatedAt) VALUES ({0}, '旧账户', 'CNY', {1}, {1})", accountId, now);
            await old.Database.ExecuteSqlRawAsync("INSERT INTO Securities (Id, Symbol, Name, Exchange, Currency, Type, CreatedAt, UpdatedAt) VALUES ({0}, 'OLD', '旧证券', 'XSHG', 'CNY', 4, {1}, {1})", securityId, now);
            await old.Database.ExecuteSqlRawAsync("INSERT INTO Positions (Id, PortfolioAccountId, SecurityId, Quantity, TotalCost, CreatedAt, UpdatedAt) VALUES ({0}, {1}, {2}, '2.0', '20.0', {3}, {3})", Guid.NewGuid(), accountId, securityId, now);
        }
        await new DatabaseInitializer(factory, paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        Assert.Single(Directory.EnumerateFiles(paths.BackupsDirectory, "*.db"));
        await using var db = factory.CreateDbContext();
        Assert.Equal("旧账户", (await db.PortfolioAccounts.SingleAsync()).Name);
        Assert.Equal(SecurityType.Other, (await db.Securities.SingleAsync()).Type);
        Assert.Equal(20, (await db.Positions.SingleAsync()).TotalCost);
        var store = new PortfolioStore(factory, new DatabaseWriter(factory));
        await Assert.ThrowsAsync<BusinessException>(() => store.PostAsync(new(accountId, securityId, Day, TransactionType.Buy, 1, 1, 0, 0, "CNY")));
        Assert.Empty(await db.Transactions.ToListAsync());
    }
    [Fact]
    public async Task EmptyZeroCapitalAccount_HasDefinedZeroWeights()
    {
        var id = await _store.SaveAccountAsync(new(null, "空账户", "USD", 0, .25m, .25m, .25m, .25m, .2m));
        var s = await _store.ReadAsync(id); Assert.Equal(0, s.TotalAssets); Assert.All(s.Allocation, x => Assert.Equal(0, x.Weight));
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}
