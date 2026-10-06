using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Risk;
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

public sealed class PortfolioRiskIntegrationTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.Risk.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private PortfolioRiskStore _risk = null!; private PortfolioStore _portfolio = null!; private ThesisStore _theses = null!;
    private Guid _account; private readonly List<Guid> _securities = [];
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); var writer = new DatabaseWriter(_factory);
        _risk = new(_factory, writer); _portfolio = new(_factory, writer); _theses = new(_factory, writer); await _risk.InitializeDefaultsAsync();
        _account = await _portfolio.SaveAccountAsync(new(null, "测试账户", "CNY", 500000, .1m, .4m, .4m, .1m, .25m));
        await using var db = _factory.CreateDbContext();
        for (var i = 0; i < 3; i++)
        {
            var security = new Security("RISK" + i, "风险测试" + i, "XSHG", "CNY", SecurityType.Stock);
            security.Edit(security.Symbol, security.Name, security.Exchange, security.Currency, security.Type, i == 1 ? Market.US : Market.ChinaA, i < 2 ? "Technology" : "Finance", "", "", null, ""); security.SetPrice(10, DateTimeOffset.UtcNow.AddDays(-1));
            db.Securities.Add(security); _securities.Add(security.Id);
        }
        await db.SaveChangesAsync();
        var quantities = new[] { 10000m, 15000m, 5000m };
        for (var i = 0; i < 3; i++) await _portfolio.PostAsync(new(_account, _securities[i], DateTimeOffset.UtcNow.AddDays(-2), TransactionType.Buy, quantities[i], 10, 0, 0, "CNY"));
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private async Task<Guid> Tag(string name) => (await _risk.TagsAsync()).Single(x => x.Name == name).Id;
    private static DecisionDraft Draft(DecisionPreview p, DecisionKind kind = DecisionKind.AddPosition, DecisionChoice choice = DecisionChoice.NoAction) => new(p.Context.Portfolio.AccountId, p.Context.SecurityId, kind, choice, DecisionRules.Questions(kind).Select(x => new DecisionAnswer(x.Key, ChecklistAnswer.Unknown)).ToArray(), "核对信息后由用户决定", p.Fingerprint);
    [Fact] public async Task DefaultsIdempotentAndCustomTagsNormalized()
    {
        var count = (await _risk.TagsAsync()).Count; await _risk.InitializeDefaultsAsync(); Assert.Equal(count, (await _risk.TagsAsync()).Count);
        await _risk.CreateTagAsync(" Customer Concentration ", RiskTagCategory.Custom);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.CreateTagAsync("customer concentration", RiskTagCategory.Custom));
        Assert.Contains(await _risk.TagsAsync(), x => x.Name == "Customer Concentration");
    }
    [Fact] public async Task MultipleTagsAggregateWithMetadataAndCash()
    {
        var semi = await Tag("Semiconductor"); var ai = await Tag("AI Capex");
        await _risk.SetTagAsync(_securities[0], semi, true); await _risk.SetTagAsync(_securities[0], semi, true); await _risk.SetTagAsync(_securities[0], ai, true); await _risk.SetTagAsync(_securities[1], semi, true);
        var risk = await _risk.ReadAsync(_account); Assert.Equal(500000, risk.TotalAssets); Assert.Equal(.6m, risk.Exposure.Concentration.Top3); Assert.Equal(.3m, risk.Exposure.Concentration.LargestPosition);
        Assert.Equal(.5m, Assert.Single(risk.Exposure.Exposures, x => x.Name == "Semiconductor").Weight); Assert.Equal(.2m, Assert.Single(risk.Exposure.Exposures, x => x.Name == "AI Capex").Weight);
        Assert.Equal(.5m, risk.Exposure.Concentration.LargestSector); Assert.Equal(1, Assert.Single(risk.Exposure.Exposures, x => x.Category == RiskTagCategory.Currency && x.Name == "CNY").Weight);
        Assert.Equal(2, (await _risk.SecurityTagsAsync(_securities[0])).Count);
    }
    [Fact] public async Task ExplicitCategoryOverridesFallbackAndRemovalRestoresIt()
    {
        var tag = await _risk.CreateTagAsync("Healthcare", RiskTagCategory.Sector); await _risk.SetTagAsync(_securities[0], tag, true);
        var risk = await _risk.ReadAsync(_account); Assert.Equal(.3m, Assert.Single(risk.Exposure.Exposures, x => x.Name == "Technology").Weight); Assert.Equal(.2m, Assert.Single(risk.Exposure.Exposures, x => x.Name == "Healthcare").Weight);
        await _risk.SetTagAsync(_securities[0], tag, false); risk = await _risk.ReadAsync(_account); Assert.Equal(.5m, Assert.Single(risk.Exposure.Exposures, x => x.Name == "Technology").Weight);
    }
    [Fact] public async Task TriggeredKillAndWarningFlowIntoDashboardAndSavedContext()
    {
        var id = await _theses.CreateAsync(_securities[0], new("现金流逻辑", "待验证"), null, ThesisActor.HumanUser);
        await _theses.ChangeStatusAsync(id, 1, ThesisStatus.Active, "人工启用", ThesisActor.HumanUser);
        var preview = await _risk.PreviewAsync(_account, _securities[0]); Assert.True(preview.Context.IsThesisActive);
        await _theses.SaveKillAsync(id, 2, null, new("利润率", "核对披露", "Margin", .15m, .1m, .08m, "ratio", ThresholdDirection.AtOrBelow, "测试证据"), "新增", ThesisActor.HumanUser);
        var snapshot = await _risk.ReadAsync(_account); Assert.Equal(1, snapshot.TriggeredCount); Assert.Equal(1, snapshot.ThesisWarningCount);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(preview)));
        preview = await _risk.PreviewAsync(_account, _securities[0]); Assert.False(preview.Context.IsThesisActive); Assert.Equal(1, preview.Context.TriggeredCount);
        await _risk.SaveDecisionAsync(Draft(preview, choice: DecisionChoice.Add));
        var record = Assert.Single(await _risk.DecisionsAsync(_account)); Assert.Equal(DecisionChoice.Add, record.Choice); Assert.Equal(1, record.Context.TriggeredCount);
        Assert.Equal(3, (await _portfolio.ReadAsync(_account)).Transactions.Count);
    }
    [Fact] public async Task AllDecisionChoicesPersistAndNeverTrade()
    {
        var before = await _portfolio.ReadAsync(_account);
        foreach (var pair in new[] { (DecisionKind.AddPosition, DecisionChoice.Add), (DecisionKind.AddPosition, DecisionChoice.NoAction), (DecisionKind.ExitPosition, DecisionChoice.Hold), (DecisionKind.ExitPosition, DecisionChoice.Trim), (DecisionKind.ExitPosition, DecisionChoice.Exit), (DecisionKind.ExitPosition, DecisionChoice.NoAction) })
            await _risk.SaveDecisionAsync(Draft(await _risk.PreviewAsync(_account, _securities[0]), pair.Item1, pair.Item2) with { Sizing = new(.1m, .8m, 1.5m, .5m) });
        var reopened = new PortfolioRiskStore(_factory, new(_factory)); var records = await reopened.DecisionsAsync(_account); Assert.Equal(6, records.Count);
        Assert.All(records, x => { Assert.NotEmpty(x.Answers); Assert.Equal("核对信息后由用户决定", x.Reason); Assert.Equal(.06m, PortfolioRiskRules.Size(x.Sizing!, x.Context.MaxWeight).SuggestedPosition); });
        var after = await _portfolio.ReadAsync(_account); Assert.Equal(before.Cash, after.Cash); Assert.Equal(before.Transactions.Count, after.Transactions.Count); Assert.Equal(before.Holdings, after.Holdings);
    }
    [Fact] public async Task SavedSnapshotRemainsFixedAfterPriceTagsAndThresholdChange()
    {
        var preview = await _risk.PreviewAsync(_account, _securities[0]); await _risk.SaveDecisionAsync(Draft(preview));
        await _portfolio.UpdatePriceAsync(_securities[0], 12, DateTimeOffset.UtcNow); await _risk.SetNearMaxRatioAsync(.8m); await _risk.SetTagAsync(_securities[0], await Tag("Growth"), true);
        var record = Assert.Single(await _risk.DecisionsAsync(_account)); Assert.Equal(.2m, record.Context.CurrentWeight); Assert.Equal(.9m, record.Context.Portfolio.NearMaxRatio); Assert.DoesNotContain(record.Context.Portfolio.Exposure.Exposures, x => x.Name == "Growth");
        Assert.NotEqual(record.Context.CurrentWeight, (await _risk.PreviewAsync(_account, _securities[0])).Context.CurrentWeight);
    }
    [Fact] public async Task NearMaxWarningAndSettingsPersist()
    {
        Assert.Equal(WeightWarning.AboveMaximum, (await _risk.PreviewAsync(_account, _securities[1])).Context.MaxWeightWarning);
        Assert.Equal(WeightWarning.Normal, (await _risk.PreviewAsync(_account, _securities[0])).Context.MaxWeightWarning);
        await _risk.SetNearMaxRatioAsync(.8m); Assert.Equal(WeightWarning.NearMaximum, (await _risk.PreviewAsync(_account, _securities[0])).Context.MaxWeightWarning);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SetNearMaxRatioAsync(0)); Assert.Equal(.8m, (await _risk.ReadAsync(_account)).NearMaxRatio);
    }
    [Fact] public async Task PriceAndTagChangesRejectStaleChecklistAtomically()
    {
        var p = await _risk.PreviewAsync(_account, _securities[0]); await _risk.SetTagAsync(_securities[1], await Tag("AI Capex"), true);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(p)));
        p = await _risk.PreviewAsync(_account, _securities[0]); await _portfolio.UpdatePriceAsync(_securities[0], 11, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(p))); Assert.Empty(await _risk.DecisionsAsync(_account));
    }
    [Fact] public async Task InvalidAnswersAndSizingNeverWrite()
    {
        var p = await _risk.PreviewAsync(_account, _securities[0]); await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(p) with { Answers = [] }));
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(p) with { Sizing = new(.1m, -1, 1, 1) })); Assert.Empty(await _risk.DecisionsAsync(_account));
    }
    [Fact] public async Task AccountingHighAttentionIsAssessedAndMissingDataIsExplicit()
    {
        Assert.Equal(21, (await _risk.ReadAsync(_account)).UnassessedAccountingChecks);
        await using (var db = _factory.CreateDbContext())
        {
            db.FinancialMetrics.Add(new(_securities[0], "2025", PeriodType.Annual, MetricType.NetIncome, 100, "CNY"));
            db.FinancialMetrics.Add(new(_securities[0], "2025", PeriodType.Annual, MetricType.OperatingCashFlow, 20, "CNY")); await db.SaveChangesAsync();
        }
        var risk = await _risk.ReadAsync(_account); var finding = Assert.Single(risk.HighAttention); Assert.Equal(_securities[0], finding.SecurityId); Assert.Equal("经营现金流弱于净利润", finding.Rule); Assert.Equal(20, risk.UnassessedAccountingChecks);
    }
    [Fact] public async Task CashOnlyAccountAndAccountIsolation()
    {
        var account = await _portfolio.SaveAccountAsync(new(null, "空仓", "CNY", 500000, .1m, .4m, .4m, .1m, .1m)); var risk = await _risk.ReadAsync(account);
        Assert.Empty(risk.Positions); Assert.Empty(risk.Theses); Assert.Equal(0, risk.Exposure.Concentration.Top3);
        await _risk.SaveDecisionAsync(Draft(await _risk.PreviewAsync(account, _securities[0]))); Assert.Empty(await _risk.DecisionsAsync(_account));
        Assert.Equal(0, Assert.Single(await _risk.DecisionsAsync(account)).Context.CurrentWeight);
    }
    [Fact] public async Task DecisionHistoryIsAppendOnlyAndProtectsSecurity()
    {
        await _risk.SaveDecisionAsync(Draft(await _risk.PreviewAsync(_account, _securities[0])));
        await using var db = _factory.CreateDbContext(); var record = await db.DecisionRecords.SingleAsync(); db.DecisionRecords.Remove(record);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); Assert.Single(await _risk.DecisionsAsync(_account));
        await Assert.ThrowsAsync<BusinessException>(() => new SecurityRepository(_factory, new(_factory)).DeleteAsync(_securities[0])); Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact] public async Task UnheldSecurityTagsAreSnapshottedAndInvalidateStaleChecklist()
    {
        var account = await _portfolio.SaveAccountAsync(new(null, "建仓前", "CNY", 500000, .1m, .4m, .4m, .1m, .1m));
        var p = await _risk.PreviewAsync(account, _securities[0]); await _risk.SetTagAsync(_securities[0], await Tag("HighValuation"), true);
        await Assert.ThrowsAsync<BusinessException>(() => _risk.SaveDecisionAsync(Draft(p)));
        p = await _risk.PreviewAsync(account, _securities[0]); await _risk.SaveDecisionAsync(Draft(p));
        Assert.Contains(Assert.Single(await _risk.DecisionsAsync(account)).Context.SecurityTags, x => x.Name == "HighValuation");
    }
    [Fact] public async Task PhaseFourUpgradePreservesDataAndBacksUp()
    {
        var paths = new AppPaths(Path.Combine(_paths.Root, "upgrade")); paths.EnsureDirectories(); var factory = new Factory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)).Options);
        await using (var db = factory.CreateDbContext()) { await db.GetService<IMigrator>().MigrateAsync("20261003131634_ValuationEngine"); db.AppSettings.Add(new("RiskUpgradeTest", "preserved")); await db.SaveChangesAsync(); }
        await new DatabaseInitializer(factory, paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await using var updated = factory.CreateDbContext(); Assert.Equal("preserved", (await updated.AppSettings.SingleAsync()).Value); Assert.Empty(await updated.DecisionRecords.ToListAsync()); Assert.NotEmpty(Directory.GetFiles(paths.BackupsDirectory));
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}
