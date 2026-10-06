using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Infrastructure.Logging;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIInvestmentWorkbench.Application.Tests;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "AIInvestmentWorkbench.Tests", Guid.NewGuid().ToString("N")));
    private TestFactory _factory = null!;
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories();
        _factory = new TestFactory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
    }
    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_paths.Root, true);
        return Task.CompletedTask;
    }
    [Fact]
    public async Task Migration_CreatesRealFileAndIsIdempotent()
    {
        Assert.True(File.Exists(_paths.DatabasePath));
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await using var db = _factory.CreateDbContext();
        Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(Directory.EnumerateFiles(_paths.BackupsDirectory));
    }
    [Fact]
    public async Task Settings_PersistAcrossContextsAndRejectInvalidTheme()
    {
        var settings = new SettingsService(_factory);
        Assert.Equal(AppTheme.Light, await settings.GetThemeAsync());
        await settings.SaveThemeAsync(AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, await new SettingsService(_factory).GetThemeAsync());
        await settings.SaveThemeAsync(AppTheme.Light);
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.AppSettings.ToListAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => settings.SaveThemeAsync((AppTheme)99));
    }
    [Fact]
    public async Task UnknownStoredTheme_FallsBackToLight()
    {
        await using var db = _factory.CreateDbContext();
        db.AppSettings.Add(new AppSetting("Appearance.Theme", "999"));
        await db.SaveChangesAsync();
        Assert.Equal(AppTheme.Light, await new SettingsService(_factory).GetThemeAsync());
    }
    [Fact]
    public async Task Reader_PreservesDecimalsAndRelationships()
    {
        await using (var db = _factory.CreateDbContext())
        {
            var account = new PortfolioAccount("测试账户", "CNY");
            var security = new Security("600000", "测试证券", "XSHG", "CNY", SecurityType.Stock);
            db.Positions.Add(new Position(account, security, 3m, 0.9m));
            db.WatchlistItems.Add(new WatchlistItem(security, "待研究"));
            db.Transactions.Add(new Transaction(account, security, TransactionType.Buy, 3m, 0.3m, 0m, 0m, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var snapshot = await new WorkbenchReader(_factory).ReadAsync();
        Assert.Equal(0.9m, Assert.Single(snapshot.Accounts).TotalCost);
        Assert.Equal(0.3m, Assert.Single(snapshot.Positions).AverageCost);
        Assert.Equal("待研究", Assert.Single(snapshot.Watchlist).Note);
        await using var verify = _factory.CreateDbContext();
        Assert.NotNull((await verify.Transactions.Include(x => x.Security).SingleAsync()).Security);
    }
    [Fact]
    public async Task Database_EnforcesUniqueSecurityAndRestrictedDeletion()
    {
        await using var db = _factory.CreateDbContext();
        var account = new PortfolioAccount("账户", "CNY");
        var security = new Security("600000", "证券", "XSHG", "CNY", SecurityType.Stock);
        db.Positions.Add(new Position(account, security, 1m, 1m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Securities.Add(new Security("600000", "重复", "XSHG", "CNY", SecurityType.Stock));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Securities.Remove(await db.Securities.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Database_EnforcesForeignKeys()
    {
        await using var db = _factory.CreateDbContext();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO WatchlistItems (Id, SecurityId, Note, CreatedAt, UpdatedAt) VALUES ({0}, {1}, '', {2}, {2})",
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
    [Fact]
    public async Task Update_PreservesCreatedTimestampAndAdvancesUpdatedTimestamp()
    {
        await using var db = _factory.CreateDbContext();
        var account = new PortfolioAccount("原名称", "CNY");
        db.PortfolioAccounts.Add(account); await db.SaveChangesAsync();
        var created = account.CreatedAt; var previous = account.UpdatedAt;
        account.Rename("新名称"); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = await db.PortfolioAccounts.SingleAsync();
        Assert.Equal(created, loaded.CreatedAt); Assert.True(loaded.UpdatedAt > previous);
    }
    [Fact]
    public async Task UnknownMigration_IsRejectedWithoutChangingHistory()
    {
        await using var db = _factory.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('OtherVersion', '10.0.9')");
        var initializer = new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance);
        await Assert.ThrowsAsync<DatabaseCompatibilityException>(() => initializer.InitializeAsync());
        Assert.Equal(db.Database.GetMigrations().Count() + 1, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(Directory.EnumerateFiles(_paths.BackupsDirectory));
    }
    [Fact]
    public async Task EmptyExistingDatabase_IsBackedUpBeforeMigration()
    {
        var backupPaths = new AppPaths(Path.Combine(_paths.Root, "existing-empty"));
        backupPaths.EnsureDirectories();
        await using (var connection = new SqliteConnection(InvestmentDbContextFactory.ConnectionString(backupPaths)))
            await connection.OpenAsync();
        var factory = new TestFactory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(backupPaths)).Options);
        await new DatabaseInitializer(factory, backupPaths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var backup = Assert.Single(Directory.EnumerateFiles(backupPaths.BackupsDirectory, "*.db"));
        await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await verify.OpenAsync();
        await using var command = verify.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await command.ExecuteScalarAsync());
    }
    [Fact]
    public void FileLogger_RedactsCredentialsAndOmitsExceptionMessages()
    {
        using var provider = new FileLoggerProvider(_paths.LogsDirectory);
        var logger = provider.CreateLogger("Test");
        logger.LogInformation("api_key=example-key password=example-password Bearer example-token");
        logger.LogError(new InvalidOperationException("secret-from-provider"), "Operation failed");
        var log = File.ReadAllText(Assert.Single(Directory.EnumerateFiles(_paths.LogsDirectory, "*.log")));
        Assert.DoesNotContain("example-key", log); Assert.DoesNotContain("example-password", log);
        Assert.DoesNotContain("example-token", log); Assert.DoesNotContain("secret-from-provider", log);
        Assert.Contains("InvalidOperationException", log); Assert.Contains("[REDACTED]", log);
    }
    private sealed class TestFactory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext>
    {
        public InvestmentDbContext CreateDbContext() => new(options);
    }
}

