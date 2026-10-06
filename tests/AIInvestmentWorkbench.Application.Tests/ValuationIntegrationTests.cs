using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Valuation;
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

public sealed class ValuationIntegrationTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.Valuation.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private ValuationStore _store = null!; private Guid _security;
    private static DateOnly Today => new(2026, 10, 3);
    private static ValuationActuals A => new(1000, 100, 100, 10, "CNY", Today, Today, "年报");
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); _store = new(_factory, new(_factory));
        await using var db = _factory.CreateDbContext(); var security = new Security("VALUE", "估值测试", "XSHG", "CNY", SecurityType.Stock); db.Securities.Add(security); await db.SaveChangesAsync(); _security = security.Id;
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private Task<Guid> Create(ValuationModelType type = ValuationModelType.PE) => _store.CreateAsync(_security, "估值", type, A, new(), Today);
    [Fact] public async Task EveryModelPersistsThreeScenariosAndRecalculatesAfterReopening()
    {
        foreach (var type in Enum.GetValues<ValuationModelType>()) await Create(type);
        var reopened = new ValuationStore(_factory, new(_factory)); var rows = await reopened.ListAsync(_security); Assert.Equal(5, rows.Count);
        foreach (var row in rows) { Assert.Equal(3, row.Scenarios.Count); Assert.Equal(A, row.Actuals); Assert.All(row.Scenarios, x => Assert.Equal(Today, x.AssumptionDate)); Assert.Equal(3, new ValuationService().Compare(row).Count); }
    }
    [Fact] public async Task CopyBasePreservesDestinationIdentityAndIsIndependent()
    {
        var id = await Create(); var original = Assert.Single(await _store.ListAsync(_security)); var bear = original.Scenarios.Single(x => x.Kind == ScenarioKind.Bear);
        await _store.SaveScenarioAsync(id, 1, ScenarioKind.Base, new() { FutureEPS = 3.141592653589793238m, Notes = "证据" }, Today);
        await _store.CopyBaseAsync(id, 2, ScenarioKind.Bear, Today.AddDays(1));
        var copied = Assert.Single(await _store.ListAsync(_security)); var copy = copied.Scenarios.Single(x => x.Kind == ScenarioKind.Bear);
        Assert.Equal(bear.Id, copy.Id); Assert.Equal(3.141592653589793238m, copy.Inputs.FutureEPS); Assert.Equal(Today.AddDays(1), copy.AssumptionDate);
        await _store.SaveScenarioAsync(id, 3, ScenarioKind.Bear, copy.Inputs with { FutureEPS = 1 }, Today);
        var final = Assert.Single(await _store.ListAsync(_security)); Assert.Equal(3.141592653589793238m, final.Scenarios.Single(x => x.Kind == ScenarioKind.Base).Inputs.FutureEPS); Assert.Equal(1, final.Scenarios.Single(x => x.Kind == ScenarioKind.Bull).Inputs.FutureEPS);
    }
    [Fact] public async Task StaleEditAndInvalidInputDoNotPartiallyWrite()
    {
        var id = await Create(ValuationModelType.SimplifiedDCF);
        await _store.SaveScenarioAsync(id, 1, ScenarioKind.Base, new() { RevenueGrowth = .12m }, Today);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveScenarioAsync(id, 1, ScenarioKind.Bear, new(), Today));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveScenarioAsync(id, 2, ScenarioKind.Base, new() { DiscountRate = .01m }, Today));
        await Assert.ThrowsAsync<BusinessException>(() => _store.UpdateActualsAsync(id, 2, "坏数据", A with { ShareCount = 0 }));
        var model = Assert.Single(await _store.ListAsync(_security)); Assert.Equal(2, model.Revision); Assert.Equal("估值", model.Name); Assert.Equal(.12m, model.Scenarios.Single(x => x.Kind == ScenarioKind.Base).Inputs.RevenueGrowth);
    }
    [Fact] public async Task CreationAndCopyValidationRollBack()
    {
        await Assert.ThrowsAsync<BusinessException>(() => _store.CreateAsync(_security, "错误", ValuationModelType.PE, A with { Currency = "USD" }, new(), Today));
        await Assert.ThrowsAsync<BusinessException>(() => _store.CreateAsync(_security, "错误", ValuationModelType.PE, A, new(), default));
        Assert.Empty(await _store.ListAsync(_security)); var id = await Create();
        await Assert.ThrowsAsync<BusinessException>(() => _store.CopyBaseAsync(id, 1, ScenarioKind.Base, Today)); Assert.Equal(1, Assert.Single(await _store.ListAsync(_security)).Revision);
    }
    [Fact] public async Task NoSolutionInputsCanBeSavedAndExplained()
    {
        await _store.CreateAsync(_security, "无夹根", ValuationModelType.ReverseValuation, A with { CurrentPrice = 1000000 }, new() { LowerBound = 0, UpperBound = .1m }, Today);
        var model = Assert.Single(await _store.ListAsync(_security)); Assert.All(new ValuationService().Compare(model), x => Assert.Equal(SolveStatus.NoBracket, x.Result.Reverse!.Status));
    }
    [Fact] public async Task ReferencesProtectSecurityAndDeleteRemovesOnlyValuation()
    {
        var id = await Create(); var repo = new SecurityRepository(_factory, new(_factory)); await Assert.ThrowsAsync<BusinessException>(() => repo.DeleteAsync(_security));
        var security = (await repo.GetAsync(_security))!; security.Edit(security.Symbol, security.Name, security.Exchange, "USD", security.Type, security.Market, "", "", "", null, "");
        await Assert.ThrowsAsync<BusinessException>(() => repo.SaveAsync(security, false));
        await _store.DeleteAsync(id, 1); Assert.Empty(await _store.ListAsync(_security)); Assert.NotNull(await repo.GetAsync(_security));
        await using var db = _factory.CreateDbContext(); Assert.Empty(await db.ValuationScenarios.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact] public async Task PhaseThreeUpgradePreservesExistingDataAndCreatesBackup()
    {
        var paths = new AppPaths(Path.Combine(_paths.Root, "upgrade")); paths.EnsureDirectories(); var factory = new Factory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)).Options);
        await using (var db = factory.CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003060001_InvestmentThesis");
            db.Database.ExecuteSqlRaw("INSERT INTO AppSettings (Id, Key, Value, CreatedAt, UpdatedAt) VALUES ('4D49F079-0B0A-4561-85AB-BEA5A17BD6D1','valuation-upgrade-test','preserved','2026-10-03','2026-10-03')");
        }
        await new DatabaseInitializer(factory, paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await using var upgraded = factory.CreateDbContext(); Assert.Equal("preserved", (await upgraded.AppSettings.SingleAsync()).Value); Assert.Empty(await upgraded.ValuationModels.ToListAsync()); Assert.NotEmpty(Directory.GetFiles(paths.BackupsDirectory)); Assert.False(upgraded.Database.HasPendingModelChanges());
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}
