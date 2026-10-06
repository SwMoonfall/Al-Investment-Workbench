using AIInvestmentWorkbench.Application.Interfaces;
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
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class ThesisIntegrationTests : IAsyncLifetime
{
    private const ThesisActor User = ThesisActor.HumanUser;
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.Thesis.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private ThesisStore _store = null!; private Guid _security;
    private static readonly DateOnly Today = new(2026, 10, 3);
    private static KillContent Condition(decimal? value = 30) => new("利润率不应持续恶化", "核对披露口径", "OperatingMargin", 20, 10, value, "%", ThresholdDirection.AtOrBelow, "年报第 10 页");
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); _store = new(_factory, new(_factory));
        await using var db = _factory.CreateDbContext(); var s = new Security("THESIS", "逻辑测试股份", "XSHG", "CNY", SecurityType.Stock); db.Securities.Add(s); await db.SaveChangesAsync(); _security = s.Id;
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private Task<Guid> Create(string title = "原始观点") => _store.CreateAsync(_security, new(title, "原始投资摘要", "市场低估现金流", "3 年", "基准", "乐观", "悲观", "新产品", "竞争"), Today, User);
    private async Task<ThesisDetail> Get(Guid id) => (await _store.ListAsync(_security)).Single(x => x.Id == id);
    [Fact]
    public async Task CoreEditsAndKillEditsKeepImmutableFullVersions()
    {
        var id = await Create(); var t = await Get(id); await _store.SaveKillAsync(id, t.Version, null, Condition(), "增加反证", User);
        t = await Get(id); var child = t.KillConditions.Single(); await _store.EditAsync(id, t.Version, t.Content with { InvestmentSummary = "新投资摘要" }, Today.AddDays(1), "新证据", User);
        t = await Get(id); await _store.SaveKillAsync(id, t.Version, child.Id, Condition(8) with { TriggerThreshold = 9 }, "修改触发点与当前数据", User);
        var history = await _store.HistoryAsync(id); Assert.Equal(new[] { 4, 3, 2, 1 }, history.Select(x => x.Version));
        Assert.Empty(history.Last().Snapshot.KillConditions); Assert.Equal("原始投资摘要", history.Single(x => x.Version == 2).Snapshot.Content.InvestmentSummary);
        Assert.Equal(10, history.Single(x => x.Version == 2).Snapshot.KillConditions.Single().Content.TriggerThreshold);
        Assert.Equal(30, history.Single(x => x.Version == 2).Snapshot.KillConditions.Single().Content.CurrentValue);
        Assert.Equal(9, history[0].Snapshot.KillConditions.Single().Content.TriggerThreshold); Assert.Equal(ThesisStatus.Warning, history[0].Snapshot.Status);
        Assert.Equal("年报第 10 页", history[0].Snapshot.KillConditions.Single().Content.Evidence);
        Assert.Equal((await Get(id)).UpdatedAt, history[0].Snapshot.UpdatedAt);
        Assert.All(history, x => Assert.Equal(User, x.Actor));
    }
    [Fact]
    public async Task DeleteTriggerDoesNotEraseHistoryOrAutomaticallyRestoreActive()
    {
        var id = await Create(); await _store.ChangeStatusAsync(id, 1, ThesisStatus.Active, "用户启用", User); await _store.SaveKillAsync(id, 2, null, Condition(5), "反证", User);
        var t = await Get(id); Assert.Equal(ThesisStatus.Warning, t.Status); Assert.True(t.IsCurrent);
        await _store.DeleteKillAsync(id, 3, t.KillConditions.Single().Id, "更正错误指标", User);
        t = await Get(id); Assert.Empty(t.KillConditions); Assert.Equal(ThesisStatus.Warning, t.Status);
        Assert.Single((await _store.HistoryAsync(id)).Single(x => x.Version == 3).Snapshot.KillConditions);
        await _store.ChangeStatusAsync(id, 4, ThesisStatus.Active, "用户重新确认", User); Assert.Equal(ThesisStatus.Active, (await Get(id)).Status);
    }
    [Theory] [InlineData(ThesisActor.AI)] [InlineData(ThesisActor.System)]
    public async Task NonUserCannotCreateEditDeleteOrChangeTriggers(ThesisActor actor)
    {
        var id = await Create(); await _store.SaveKillAsync(id, 1, null, Condition(), "新增", User); var child = (await Get(id)).KillConditions.Single();
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveKillAsync(id, 2, null, Condition(), "AI 新增", actor));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveKillAsync(id, 2, child.Id, Condition(0), "AI 修改", actor));
        await Assert.ThrowsAsync<BusinessException>(() => _store.DeleteKillAsync(id, 2, child.Id, "AI 删除", actor));
        Assert.Equal(2, (await Get(id)).Version); Assert.Equal(30, (await Get(id)).KillConditions.Single().Content.CurrentValue); Assert.Equal(2, (await _store.HistoryAsync(id)).Count);
    }
    [Fact]
    public async Task OnlyOneCurrentThesisIncludingWarningAndExplicitReplacement()
    {
        var first = await Create(); var second = await Create("下一份观点");
        await _store.ChangeStatusAsync(first, 1, ThesisStatus.Active, "启用", User);
        await _store.SaveKillAsync(first, 2, null, Condition(1), "观察到反证", User);
        await Assert.ThrowsAsync<BusinessException>(() => _store.ChangeStatusAsync(second, 1, ThesisStatus.Active, "尝试替换", User));
        Assert.Equal(1, (await Get(second)).Version); Assert.Single(await _store.HistoryAsync(second));
        await _store.ChangeStatusAsync(first, 3, ThesisStatus.Invalidated, "人工判定反证成立", User);
        await _store.ChangeStatusAsync(second, 1, ThesisStatus.Active, "重新建立逻辑", User);
        Assert.Single(await _store.ListAsync(_security), x => x.IsCurrent); Assert.Equal(ThesisStatus.Invalidated, (await Get(first)).Status);
    }
    [Fact]
    public async Task ConcurrentActivationHasSingleWinnerAndRollbackLeavesNoVersion()
    {
        var a = await Create(); var b = await Create("第二份");
        async Task<bool> Activate(Guid id) { try { await _store.ChangeStatusAsync(id, 1, ThesisStatus.Active, "启用", User); return true; } catch (BusinessException) { return false; } }
        var result = await Task.WhenAll(Task.Run(() => Activate(a)), Task.Run(() => Activate(b)));
        Assert.Single(result, x => x); Assert.Single(await _store.ListAsync(_security), x => x.IsCurrent);
        Assert.Equal(3, (await _store.HistoryAsync(a)).Count + (await _store.HistoryAsync(b)).Count);
    }
    [Fact]
    public async Task StaleAndInvalidEditsRollbackAggregateAndSnapshot()
    {
        var id = await Create(); var t = await Get(id); await _store.EditAsync(id, 1, t.Content with { InvestmentSummary = "正确更新" }, Today, "依据", User);
        await Assert.ThrowsAsync<BusinessException>(() => _store.EditAsync(id, 1, t.Content with { InvestmentSummary = "过时更新" }, Today, "依据", User));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveKillAsync(id, 2, null, Condition() with { WarningThreshold = 1, TriggerThreshold = 20 }, "错误阈值", User));
        Assert.Equal("正确更新", (await Get(id)).Content.InvestmentSummary); Assert.Empty((await Get(id)).KillConditions); Assert.Equal(2, (await _store.HistoryAsync(id)).Count);
    }
    [Fact]
    public async Task ReviewCompletionPersistsEvidenceAndPreservesDecisionState()
    {
        var id = await Create(); await _store.ChangeStatusAsync(id, 1, ThesisStatus.Warning, "待核实", User);
        Assert.True((await _store.IndicatorsAsync(Today)).Single().NeedsReview);
        await Assert.ThrowsAsync<BusinessException>(() => _store.CompleteReviewAsync(id, 2, Today, Today, "非法日期", User));
        await _store.CompleteReviewAsync(id, 2, Today.AddDays(30), Today, "已核对新年报，仍需观察", User);
        var t = await Get(id); Assert.Equal(ThesisStatus.Warning, t.Status); Assert.NotNull(t.LastReviewedAt);
        Assert.False((await _store.IndicatorsAsync(Today)).Single().NeedsReview);
        Assert.Contains("已核对新年报", (await _store.HistoryAsync(id)).First().ChangeReason);
    }
    [Fact]
    public async Task AssumptionsCurrentValueUpdatesAndCrossThesisEditsRejected()
    {
        var id = await Create(); var other = await Create("另一个逻辑");
        var content = new AssumptionContent("利润率改善", "Margin", 20, 30, 15, 10, null, "%", ThresholdDirection.AtOrBelow, "");
        await _store.SaveAssumptionAsync(id, 1, null, content, "增加假设", User); var a = (await Get(id)).Assumptions.Single(); Assert.Equal(AssumptionStatus.Unknown, a.Status);
        await _store.SaveAssumptionAsync(id, 2, a.Id, content with { CurrentValue = 12, Evidence = "手工核实" }, "更新数据", User);
        Assert.Equal(AssumptionStatus.Warning, (await Get(id)).Assumptions.Single().Status);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveAssumptionAsync(other, 1, a.Id, content, "跨逻辑修改", User));
        await _store.DeleteAssumptionAsync(id, 3, a.Id, "修正假设", User); Assert.Empty((await Get(id)).Assumptions);
        Assert.Single((await _store.HistoryAsync(id)).Single(x => x.Version == 3).Snapshot.Assumptions);
    }
    [Fact]
    public async Task HistoryIsAppendOnlyAndArchivedThesisSurvivesReopen()
    {
        var id = await Create(); await _store.ChangeStatusAsync(id, 1, ThesisStatus.Closed, "研究结束", User); await _store.ArchiveAsync(id, 2, "保留历史", User);
        var reopened = new ThesisStore(_factory, new(_factory)); var t = Assert.Single(await reopened.ListAsync(_security)); Assert.True(t.Archived); Assert.Equal(3, t.Version);
        Assert.Empty(await reopened.IndicatorsAsync(Today)); Assert.Equal(3, (await reopened.HistoryAsync(id)).Count);
        await using var db = _factory.CreateDbContext(); var history = await db.ThesisVersions.FirstAsync(); db.ThesisVersions.Remove(history);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task DatabaseFilteredUniqueIndexEnforcesCurrentSlot()
    {
        var a = await Create(); var b = await Create("第二份"); await _store.ChangeStatusAsync(a, 1, ThesisStatus.Active, "启用", User);
        await using var db = _factory.CreateDbContext(); var second = await db.Theses.SingleAsync(x => x.Id == b); second.ChangeStatus(ThesisStatus.Active, User);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task NoKillConditionsAndMissingValuesUseNeutralInsteadOfGreen()
    {
        var id = await Create(); await _store.ChangeStatusAsync(id, 1, ThesisStatus.Active, "启用", User);
        Assert.Equal("Neutral", (await Get(id)).Signal);
        await _store.SaveKillAsync(id, 2, null, Condition(null), "数据待补充", User); Assert.Equal("Neutral", (await Get(id)).Signal);
        Assert.Equal("未评估", (await Get(id)).KillConditions.Single().Assessment);
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
    [Fact]
    public async Task Phase2UpgradePreservesResearchFinancialsAndVersion()
    {
        var paths = new AppPaths(Path.Combine(_paths.Root, "phase2")); paths.EnsureDirectories();
        var factory = new Factory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)).Options);
        Guid securityId;
        await using (var db = factory.CreateDbContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003052010_ResearchAndFinancialData");
            var security = new Security("OLD", "旧研究证券", "XSHG", "CNY", SecurityType.Stock); securityId = security.Id; db.Securities.Add(security);
            var research = new CompanyResearch(security.Id); research.Update(new(Overview: "保留研究正文", UserNotes: "原始笔记"), 0); db.CompanyResearches.Add(research);
            db.FinancialMetrics.Add(new(security.Id, "2025", PeriodType.Annual, MetricType.Revenue, 123.4567m, "CNY")); await db.SaveChangesAsync();
        }
        await new DatabaseInitializer(factory, paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var data = await new ResearchStore(factory, new(factory)).ReadAsync(securityId);
        Assert.Equal("保留研究正文", data.Research!.Overview); Assert.Equal("原始笔记", data.Research.UserNotes); Assert.Equal(1, data.Research.Revision);
        Assert.Equal(123.4567m, Assert.Single(data.Metrics).Value); Assert.Single(Directory.GetFiles(paths.BackupsDirectory));
        Assert.Empty(await new ThesisStore(factory, new(factory)).ListAsync(securityId));
    }
}
