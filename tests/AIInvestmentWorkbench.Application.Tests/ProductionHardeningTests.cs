using Xunit;
using System.Security.Cryptography;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
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
namespace AIInvestmentWorkbench.Application.Tests;
public sealed class ProductionHardeningTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "AIWB-Hardening-" + Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private PortfolioStore _portfolio = null!; private Guid _account; private Guid _security;
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(_paths.DatabasePath); await Init(); var writer = new DatabaseWriter(_factory); _portfolio = new(_factory, writer);
        _account = await _portfolio.SaveAccountAsync(new(null,"真实账户","CNY",500000,.1m,.4m,.4m,.1m,.15m));
        using var db = _factory.CreateDbContext(); var s = new Security("HARD","安全测试","TEST","CNY",SecurityType.Stock); _security=s.Id; db.Securities.Add(s); await db.SaveChangesAsync();
    }
    private Task Init() => new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root,true); return Task.CompletedTask; }
    [Fact] public async Task DailyBackupIsOncePerDayAndRetainsSevenButKeepsManual()
    {
        var backup = new BackupService(_paths); var manual=await backup.CreateAsync();
        for(var i=0;i<10;i++) await backup.CreateAsync(true,new DateOnly(2025,1,1).AddDays(i));
        var last = await backup.CreateAsync(true,new(2025,1,10)); var hash = SHA256.HashData(await File.ReadAllBytesAsync(last));
        await backup.CreateAsync(true,new(2025,1,10)); Assert.Equal(hash,SHA256.HashData(await File.ReadAllBytesAsync(last)));
        Assert.Equal(7,Directory.GetFiles(_paths.BackupsDirectory,"daily-*.db").Length); Assert.True(File.Exists(manual));
    }
    [Fact] public async Task ConcurrentAutomaticRequestsCreateOneValidBackup()
    { var service=new BackupService(_paths); var paths=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>service.CreateAsync(true))); Assert.Single(paths.Distinct()); await DatabaseFiles.ValidateAsync(paths[0]); }
    [Fact] public async Task RestoreIsDeferredAndMakesCurrentDatabaseSafetyCopy()
    {
        var service=new BackupService(_paths); var old=await service.CreateAsync();
        await _portfolio.SaveAccountAsync(new(_account,"修改后","CNY",500000,.1m,.4m,.4m,.1m,.15m));
        await service.StageRestoreAsync(old); Assert.Equal("修改后",(await _portfolio.AccountsAsync()).Single().Name);
        await Init(); Assert.Equal("真实账户",(await _portfolio.AccountsAsync()).Single().Name);
        var safety=Assert.Single(Directory.GetFiles(_paths.BackupsDirectory,"before-restore-*.db"));
        using var original=DatabaseFiles.Context(safety); Assert.Equal("修改后",(await original.PortfolioAccounts.SingleAsync()).Name); Assert.False(File.Exists(service.PendingPath));
    }
    [Fact] public async Task InvalidRestoreNeverReplacesCurrentDatabase()
    { var bad=Path.Combine(_paths.Root,"bad.db"); await File.WriteAllTextAsync(bad,"not sqlite"); await Assert.ThrowsAsync<SqliteException>(()=>new BackupService(_paths).StageRestoreAsync(bad)); Assert.Equal("真实账户",(await _portfolio.AccountsAsync()).Single().Name); }
    [Fact] public async Task MissingBackupHasSpecificFailure()
    { await Assert.ThrowsAsync<FileNotFoundException>(()=>new BackupService(_paths).StageRestoreAsync(Path.Combine(_paths.Root,"missing.db"))); }
    [Fact] public async Task UnknownVersionIsRejectedForRestore()
    {
        var file=await new BackupService(_paths).CreateAsync(); using(var db=DatabaseFiles.Context(file)) await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('999_Future','99')");
        await Assert.ThrowsAsync<DatabaseCompatibilityException>(()=>new BackupService(_paths).StageRestoreAsync(file));
    }
    [Fact] public async Task MigrationFailureLeavesOriginalBytesIntact()
    {
        var path=Path.Combine(_paths.Root,"old"); var p=new AppPaths(path); p.EnsureDirectories(); var f=new Factory(p.DatabasePath);
        using(var db=f.CreateDbContext()) { await db.GetService<IMigrator>().MigrateAsync("20261004101350_AIIntegration"); await db.Database.ExecuteSqlRawAsync("ALTER TABLE AIAnalyses ADD COLUMN PortfolioAccountId TEXT NULL"); }
        await DatabaseFiles.CheckpointAsync(p.DatabasePath); SqliteConnection.ClearAllPools(); var bytes=await File.ReadAllBytesAsync(p.DatabasePath);
        await Assert.ThrowsAsync<DatabasePreparationException>(()=>new DatabaseInitializer(f,p,NullLogger<DatabaseInitializer>.Instance).InitializeAsync()); Assert.Equal(bytes,await File.ReadAllBytesAsync(p.DatabasePath));
    }
    [Fact] public async Task OldDatabaseUpgradesAndPreservesAccount()
    {
        var p=new AppPaths(Path.Combine(_paths.Root,"old")); p.EnsureDirectories(); var f=new Factory(p.DatabasePath);
        using(var db=f.CreateDbContext()) { await db.GetService<IMigrator>().MigrateAsync("20261004101350_AIIntegration"); db.PortfolioAccounts.Add(new("旧账户","CNY")); await db.SaveChangesAsync(); }
        await new DatabaseInitializer(f,p,NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); using var verify=f.CreateDbContext(); Assert.Equal("旧账户",(await verify.PortfolioAccounts.SingleAsync()).Name); Assert.Empty(await verify.Database.GetPendingMigrationsAsync()); Assert.Single(Directory.GetFiles(p.BackupsDirectory,"before-migration-*.db"));
    }
    [Fact] public async Task DemoResetAndDeleteDoNotTouchRealAccount()
    {
        var demo=new DemoService(_paths); var root=await demo.PrepareAsync(); using(var db=DatabaseFiles.Context(Path.Combine(root,"Data","investment.db"))) { Assert.Equal(3,await db.Securities.CountAsync()); Assert.StartsWith("DEMO",(await db.PortfolioAccounts.SingleAsync()).Name); }
        await demo.PrepareAsync(true); await demo.DeleteAsync(); Assert.False(Directory.Exists(root)); Assert.Equal("真实账户",(await _portfolio.AccountsAsync()).Single().Name); Assert.Equal(2,Directory.GetFiles(_paths.BackupsDirectory,"before-demo-delete-*.db").Length);
    }
    [Fact] public async Task DemoRefusesUnmarkedDirectory()
    { var demo=new DemoService(_paths); Directory.CreateDirectory(demo.Root); var keep=Path.Combine(demo.Root,"keep.txt"); await File.WriteAllTextAsync(keep,"user file"); await Assert.ThrowsAsync<BusinessException>(()=>demo.DeleteAsync()); await Assert.ThrowsAsync<BusinessException>(()=>demo.PrepareAsync()); Assert.True(File.Exists(keep)); }
    [Theory] [InlineData("=1+1")] [InlineData("+cmd")] [InlineData("-cmd")] [InlineData("@SUM(A1)")] [InlineData("  =1")]
    public void CsvTextCannotBecomeFormula(string value) => Assert.StartsWith("\"'",ExportService.Cell(value));
    [Fact] public void CsvPreservesQuotesNewlinesAndNumericNegatives() { Assert.Equal("\"a,\"\"b\"\"\n中\"",ExportService.Cell("a,\"b\"\n中")); Assert.Equal("\"-12.5\"",ExportService.Cell(-12.5m)); }
    [Theory] [InlineData(ExportKind.PortfolioCsv)] [InlineData(ExportKind.TransactionsCsv)] [InlineData(ExportKind.FinancialMetricsCsv)] [InlineData(ExportKind.JournalCsv)] [InlineData(ExportKind.ResearchJson)] [InlineData(ExportKind.ResearchMarkdown)] [InlineData(ExportKind.ThesisMarkdown)] [InlineData(ExportKind.QuarterlyReviewMarkdown)]
    public async Task AllExportFormatsWriteReadableFiles(ExportKind kind)
    {
        var writer=new DatabaseWriter(_factory); var research=new ResearchStore(_factory,writer); var risk=new PortfolioRiskStore(_factory,writer); var thesis=new ThesisStore(_factory,writer); var valuations=new ValuationStore(_factory,writer); var journals=new JournalReviewStore(_factory,writer,_portfolio,risk,thesis,valuations,new(),research);
        await research.SaveResearchAsync(_security,new(Overview:"中文研究内容"),0);
        var exporter=new ExportService(_factory,_portfolio,research,thesis,journals); var target=Path.Combine(_paths.Root,kind+".txt"); await exporter.ExportAsync(kind,_account,_security,target); var text=await File.ReadAllTextAsync(target); Assert.NotEmpty(text); if(kind==ExportKind.ResearchMarkdown) Assert.Contains("中文研究内容",text); Assert.Empty(Directory.GetFiles(_paths.Root,"*.tmp"));
    }
    private sealed class Factory(string path) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext()=>DatabaseFiles.Context(path); }
}

