using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class ProviderCatalogTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "provider-catalog-" + Guid.NewGuid() + ".db");
    private AIStore _store = null!;
    private Factory _factory = null!;
    private readonly MemorySecrets _secrets = new();
    private ProviderCatalogService Service => new(_store, _store, _secrets);
    public async Task InitializeAsync()
    {
        _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite($"Data Source={_path};Pooling=False").Options);
        await using var db = _factory.CreateDbContext(); await db.Database.EnsureCreatedAsync(); _store = new(_factory, new(_factory));
    }
    public Task DisposeAsync() { File.Delete(_path); return Task.CompletedTask; }
    [Fact] public async Task LegacyMigrationRetainsModelAndCopiesKeyOnce()
    {
        var legacy = new AISettings("OpenAI", "old-model"); await _store.SaveSettingsAsync(legacy); await _secrets.WriteAsync(legacy.SecretScope, "old-key");
        var catalog = await Service.LoadAsync(); var source = Assert.Single(catalog.Sources); var model = Assert.Single(catalog.Models);
        Assert.Equal(source.Id, model.SourceId); Assert.Equal(model.Id, catalog.ActiveModelId); Assert.Equal("old-key", await _secrets.ReadAsync(source.Settings().SecretScope));
        Assert.Equal(source.Id, Assert.Single((await Service.LoadAsync()).Sources).Id);
        Assert.Equal("old-model", (await _store.ReadSettingsAsync()).Model);
    }
    [Fact] public async Task ManyModelsPersistWithinExistingSettingSizeLimitAndNoPlaintextKey()
    {
        var source = new ProviderSource(Guid.NewGuid(), "Source", "GoogleGenAI"); var catalog = await Service.SaveSourceAsync(ProviderCatalog.Empty, source, "private-key");
        for (var i = 0; i < 100; i++) catalog = ProviderCatalogService.AddModel(catalog, source.Id, "models/model-" + i);
        catalog = catalog with { ActiveModelId = catalog.Models[0].Id }; await Service.SaveAsync(catalog);
        var loaded = (await _store.ReadCatalogAsync())!; Assert.Equal(100, loaded.Models.Length); Assert.Equal("model-0", (await _store.ReadSettingsAsync()).Model);
        await using var db = _factory.CreateDbContext(); Assert.All(await db.AppSettings.ToListAsync(), x => Assert.DoesNotContain("private-key", x.Value));
    }
    [Fact] public async Task DuplicateModelReenablesExistingIdentityAndIsScopedToSource()
    {
        var a = new ProviderSource(Guid.NewGuid(), "A", "GoogleGenAI"); var b = a with { Id = Guid.NewGuid(), Name = "B" };
        var catalog = await Service.SaveSourceAsync(ProviderCatalog.Empty, a, null); catalog = await Service.SaveSourceAsync(catalog, b, null);
        catalog = ProviderCatalogService.AddModel(catalog, a.Id, " models/same "); var id = catalog.Models[0].Id;
        catalog = catalog with { Models = [catalog.Models[0] with { Enabled = false }] };
        catalog = ProviderCatalogService.AddModel(catalog, a.Id, "same"); Assert.Equal(id, Assert.Single(catalog.Models).Id); Assert.True(catalog.Models[0].Enabled);
        catalog = ProviderCatalogService.AddModel(catalog, b.Id, "same"); Assert.Equal(2, catalog.Models.Length); catalog.Validate();
    }
    [Fact] public async Task RenameKeepsIdWhileTypeChangeRemovesModelsAndActiveSelection()
    {
        var source = new ProviderSource(Guid.NewGuid(), "Source", "OpenAI"); var catalog = await Service.SaveSourceAsync(ProviderCatalog.Empty, source, "key");
        catalog = ProviderCatalogService.AddModel(catalog, source.Id, "test"); catalog = catalog with { ActiveModelId = catalog.Models[0].Id };
        catalog = await Service.SaveSourceAsync(catalog, source with { Name = "Renamed" }, null); Assert.Single(catalog.Models); Assert.NotNull(catalog.ActiveModelId);
        catalog = await Service.SaveSourceAsync(catalog, source with { Type = "Anthropic" }, null); Assert.Empty(catalog.Models); Assert.Null(catalog.ActiveModelId);
        Assert.Equal("Disabled", (await _store.ReadSettingsAsync()).Provider);
    }
    [Fact] public async Task DifferentSourcesAndEndpointsNeverShareKeys()
    {
        var source = new ProviderSource(Guid.NewGuid(), "Source", "OpenAI"); await Service.SaveSourceAsync(ProviderCatalog.Empty, source, "key");
        Assert.Null(await _secrets.ReadAsync((source with { Id = Guid.NewGuid() }).Settings().SecretScope));
        Assert.Null(await _secrets.ReadAsync((source with { BaseUrl = "https://other.example/v1" }).Settings().SecretScope));
    }
    [Fact] public async Task DanglingAndDuplicateAssociationsRejectedWithoutOverwritingSavedCatalog()
    {
        var source = new ProviderSource(Guid.NewGuid(), "Source", "OpenAI"); var catalog = await Service.SaveSourceAsync(ProviderCatalog.Empty, source, null);
        catalog = ProviderCatalogService.AddModel(catalog, source.Id, "test"); await Service.SaveAsync(catalog);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveCatalogAsync(catalog with { Sources = [] }));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveCatalogAsync(catalog with { Models = [.. catalog.Models, catalog.Models[0] with { Id = Guid.NewGuid() }] }));
        Assert.Single((await _store.ReadCatalogAsync())!.Models);
    }
    [Fact] public async Task EmptyCatalogPreventsOldLegacySettingsFromBeingReactivated()
    {
        await _store.SaveSettingsAsync(new("OpenAI", "old")); await _store.SaveCatalogAsync(ProviderCatalog.Empty);
        Assert.Equal("Disabled", (await _store.ReadSettingsAsync()).Provider); Assert.Empty((await Service.LoadAsync()).Sources);
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext>
    { public InvestmentDbContext CreateDbContext() => new(options); public Task<InvestmentDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext()); }
}
