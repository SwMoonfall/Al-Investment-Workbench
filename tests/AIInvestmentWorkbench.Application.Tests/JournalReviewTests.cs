using System.Text.Json;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class JournalReviewTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.Journal.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private DatabaseWriter _writer = null!; private JournalReviewStore _store = null!; private PortfolioStore _portfolio = null!; private PortfolioRiskStore _risk = null!;
    private Guid _account, _other, _security;
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); _writer = new(_factory); _portfolio = new(_factory, _writer); _risk = new(_factory, _writer); await _risk.InitializeDefaultsAsync();
        _store = new(_factory, _writer, _portfolio, _risk, new ThesisStore(_factory, _writer), new ValuationStore(_factory, _writer), new(), new ResearchStore(_factory, _writer));
        _account = await _portfolio.SaveAccountAsync(new(null, "Journal", "CNY", 500000, .1m, .4m, .4m, .1m, .25m)); _other = await _portfolio.SaveAccountAsync(new(null, "Other", "CNY", 10000, .1m, .4m, .4m, .1m, .25m));
        var s = new Security("JRN", "复盘测试", "XSHG", "CNY", SecurityType.Stock); s.SetPrice(8, DateTimeOffset.UtcNow.AddDays(-1)); await new SecurityRepository(_factory, _writer).SaveAsync(s, true); _security = s.Id;
        await _portfolio.PostAsync(new(_account, _security, new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero), TransactionType.Buy, 100, 10, 0, 5, "CNY"));
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private JournalDraft Journal() => new(_account, _security, new(new(2025, 12, 1), JournalAction.Add, 10, 100, .1m, "原始理由", ExpectedHoldingPeriod: "三年", Confidence: 85, ExpectedHoldingDays: 1095));
    [Fact] public async Task JournalPersistsAndCorrectionsAppendHistory()
    {
        var id = await _store.SaveJournalAsync(Journal(), ThesisActor.HumanUser); var old = Assert.Single(await _store.JournalsAsync(_account)).Journal;
        await _store.SaveJournalAsync(Journal() with { PreviousId = id, Content = old.Content with { Reason = "更正补充证据" }, CorrectionReason = "补充资料" }, ThesisActor.HumanUser);
        var current = Assert.Single(await _store.JournalsAsync(_account)).Journal; Assert.Equal(2, current.Version); Assert.Equal(old.RootId, current.RootId); Assert.Equal("原始理由", (await _store.JournalsAsync(_account, true)).Single(x => x.Journal.Version == 1).Journal.Reason);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveJournalAsync(Journal() with { PreviousId = id }, ThesisActor.HumanUser));
        Assert.Empty(await _store.JournalsAsync(_other)); await using var reopened = _factory.CreateDbContext(); Assert.Equal(2, await reopened.InvestmentJournals.CountAsync());
    }
    [Fact] public async Task StoredJournalsAndReviewsAreImmutable()
    {
        await _store.SaveJournalAsync(Journal(), ThesisActor.HumanUser); await using var db = _factory.CreateDbContext(); var row = await db.InvestmentJournals.SingleAsync(); db.Entry(row).Property(x => x.Reason).CurrentValue = "overwrite"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); db.InvestmentJournals.Remove(await db.InvestmentJournals.SingleAsync()); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Theory] [InlineData(DecisionChoice.Add, JournalAction.Add)] [InlineData(DecisionChoice.Hold, JournalAction.Hold)] [InlineData(DecisionChoice.Trim, JournalAction.Trim)] [InlineData(DecisionChoice.Exit, JournalAction.Exit)] [InlineData(DecisionChoice.NoAction, JournalAction.ResearchDecision)]
    public async Task DecisionMappingIsDraftUntilHumanSave(DecisionChoice choice, JournalAction action)
    {
        var kind = choice is DecisionChoice.Add or DecisionChoice.NoAction ? DecisionKind.AddPosition : DecisionKind.ExitPosition; var preview = await _risk.PreviewAsync(_account, _security);
        await _risk.SaveDecisionAsync(new(_account, _security, kind, choice, DecisionRules.Questions(kind).Select(x => new DecisionAnswer(x.Key, ChecklistAnswer.Unknown)).ToArray(), "记录理由", preview.Fingerprint));
        var decision = Assert.Single(await _risk.DecisionsAsync(_account)); var draft = DecisionJournalMapper.Draft(decision);
        Assert.Equal(action, draft.Content.Action); Assert.Null(draft.Content.Price); Assert.Null(draft.Content.Quantity); Assert.Null(draft.Content.Confidence); Assert.Equal(decision.Context.CurrentWeight, draft.Content.PortfolioWeight); Assert.Empty(await _store.JournalsAsync(_account));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveJournalAsync(draft, ThesisActor.AI));
        await _store.SaveJournalAsync(draft, ThesisActor.HumanUser); Assert.Equal(decision.Id, Assert.Single(await _store.JournalsAsync(_account)).Journal.DecisionRecordId);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveJournalAsync(draft, ThesisActor.HumanUser)); Assert.Single((await _portfolio.ReadAsync(_account)).Transactions);
    }
    [Fact] public async Task QuarterlyDraftCompletionHistoryAndDue()
    {
        var today = DateOnly.FromDateTime(DateTime.Today); var period = JournalRules.LastCompletedQuarter(today); Assert.Contains((await _store.DashboardAsync(_account, today)).Due, x => x.SecurityId == _security);
        var evidence = await _store.PreviewAsync(_account, _security, ReviewKind.Quarterly, period.End); var id = await _store.SaveReviewAsync(new(evidence, "", evidence.Assumptions, null, "", ReviewStatus.Draft), ThesisActor.HumanUser);
        Assert.Contains((await _store.DashboardAsync(_account, today)).Due, x => x.SecurityId == _security);
        await _store.SaveReviewAsync(new(evidence, "本季度结果及缺口已核实", evidence.Assumptions, ReviewDecision.ContinueResearch, "继续补充研究，不执行交易", ReviewStatus.Completed, id), ThesisActor.HumanUser);
        Assert.DoesNotContain((await _store.DashboardAsync(_account, today)).Due, x => x.SecurityId == _security); Assert.Equal(2, (await _store.ReviewsAsync(_account, true)).Count); Assert.Equal(ReviewStatus.Completed, Assert.Single(await _store.ReviewsAsync(_account)).Review.Status);
        Assert.Equal(ReviewStatus.Draft, (await _store.ReviewsAsync(_account, true)).Single(x => x.Review.Id == id).Review.Status);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveReviewAsync(new(evidence, "", [], null, "", ReviewStatus.Draft, id), ThesisActor.HumanUser));
    }
    [Theory] [InlineData(ReviewKind.Weekly)] [InlineData(ReviewKind.Monthly)] [InlineData(ReviewKind.Quarterly)]
    public async Task ReviewHasRealEvidenceAndExplicitHistoricalGaps(ReviewKind kind)
    {
        var e = await _store.PreviewAsync(_account, kind == ReviewKind.Quarterly ? _security : null, kind, new(2025, 12, 15));
        Assert.Contains(e.Sections, x => x.Title.Contains("持仓数量变化") && x.Lines.Any(y => y.Contains("100.0000")));
        Assert.Contains(e.Sections, x => x.Title.Contains("资产配置")); Assert.Contains(e.Sections, x => x.Title.Contains("暴露")); Assert.Contains(e.Sections, x => x.Title.Contains("可验证快照变化") && x.Lines.Any(y => y.Contains("没有本期之前")));
        Assert.Contains(e.Sections, x => x.Title.Contains("时间与口径") && x.Lines.Any(y => y.Contains("不冒充历史期末状态")));
    }
    [Fact] public async Task OriginalThesisAndAssumptionsComeFromHistoricalVersion()
    {
        var created = new DateTimeOffset(2025, 9, 1, 12, 0, 0, TimeSpan.Zero); var t = new Thesis(_security, new("原始逻辑", "历史预期收入增长", ExpectedHoldingPeriod: "三年"), new(2026, 1, 15), ThesisActor.HumanUser);
        var a = new ThesisAssumption(t.Id, new("收入增长", "RevenueGrowth", .1m, .2m, .1m, 0, .15m, "ratio", ThresholdDirection.AtOrBelow, "初始证据"), ThesisActor.HumanUser); t.Assumptions.Add(a); t.ChangeStatus(ThesisStatus.Active, ThesisActor.HumanUser);
        var detail = new ThesisDetail(t.Id, _security, "JRN", t.Content, t.Status, created, t.ReviewDate, null, 1, false, true, created, [new(a.Id, new(a.Description, a.Metric, a.Baseline, a.ExpectedValue, a.WarningThreshold, a.KillThreshold, a.CurrentValue, a.Unit, a.Direction, a.Evidence), a.Status, a.LastReviewedAt)], []);
        await using (var db = _factory.CreateDbContext()) { db.Theses.Add(t); db.Entry(t).Property(x => x.CreatedDate).CurrentValue = created; var version = new ThesisVersion(t.Id, 1, JsonSerializer.Serialize(detail), "原始", ThesisActor.HumanUser); db.ThesisVersions.Add(version); db.Entry(version).Property(x => x.CreatedAt).CurrentValue = created; await db.SaveChangesAsync(); }
        var evidence = await _store.PreviewAsync(_account, _security, ReviewKind.Quarterly, new(2025, 12, 1)); Assert.Single(evidence.Assumptions); Assert.Contains(evidence.Sections, x => x.Title.Contains("原始 Thesis") && x.Lines.Contains("历史预期收入增长"));
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveReviewAsync(new(evidence, "结果", [], ReviewDecision.Hold, "结论", ReviewStatus.Completed), ThesisActor.HumanUser));
        var assessments = evidence.Assumptions.Select(x => x with { Outcome = AssumptionOutcome.Weakened, Evidence = "本季度数据弱于预期" }).ToArray();
        await _store.SaveReviewAsync(new(evidence, "核实的实际结果", assessments, ReviewDecision.Reduce, "人工计划，不下单", ReviewStatus.Completed), ThesisActor.HumanUser);
        await using var verify = _factory.CreateDbContext(); Assert.Equal(ThesisStatus.Active, (await verify.Theses.SingleAsync()).Status); Assert.Single(await verify.Transactions.ToListAsync());
    }
    [Fact] public async Task AiReadsOnlySelectedAccountJournalsAndCannotOverwriteThem()
    {
        var id = await _store.SaveJournalAsync(Journal() with { Content = Journal().Content with { Date = DateOnly.FromDateTime(DateTime.Today).AddDays(-2) } }, ThesisActor.HumanUser);
        await _store.SaveJournalAsync(new(_other, null, new(DateOnly.FromDateTime(DateTime.Today), JournalAction.Other, null, null, null, "PRIVATE-OTHER-ACCOUNT")), ThesisActor.HumanUser);
        var context = new AIContextBuilder(_factory, new ResearchStore(_factory, _writer), _risk, new ValuationStore(_factory, _writer), new(), _store);
        var aiStore = new AIStore(_factory, _writer); await aiStore.InitializeAsync(); await aiStore.SaveSettingsAsync(new("OpenAI", "offline", 2, 1024)); var fake = new FakeAIProvider(); using var service = new AIAnalysisService(aiStore, context, fake);
        var template = (await aiStore.TemplatesAsync()).Single(x => x.AnalysisType == AnalysisType.JournalReview); await service.RunAsync(template.Id, new(null, _account, AnalysisType.JournalReview));
        var request = Assert.Single(fake.Requests); Assert.Contains(id.ToString(), request.ContextJson); Assert.DoesNotContain("PRIVATE-OTHER-ACCOUNT", request.ContextJson); Assert.Contains("Investment horizon", request.Instructions); Assert.Contains("Thesis outcome", request.Instructions); Assert.Contains("不做心理诊断", request.Instructions); Assert.Contains("短期亏损", request.Instructions);
        Assert.Equal("原始理由", Assert.Single(await _store.JournalsAsync(_account)).Journal.Reason); Assert.Single((await _store.DashboardAsync(_account, DateOnly.FromDateTime(DateTime.Today))).RecentAI); Assert.Empty((await _store.DashboardAsync(_other, DateOnly.FromDateTime(DateTime.Today))).RecentAI);
    }
    [Fact] public async Task BehaviorSeparatesIntentFromTradesAndImmatureHorizon()
    {
        await _store.SaveJournalAsync(Journal(), ThesisActor.HumanUser); var text = await _store.BehaviorAsync(_account, DateOnly.FromDateTime(DateTime.Today));
        Assert.Contains("加仓意图 1", text); Assert.Contains("实际账本买入 1", text); Assert.Contains("-20.00%", text); Assert.Contains("已达到预期天数 0", text); Assert.Contains("非投资收益率", text); Assert.Contains("不进行心理诊断", text);
    }
    [Fact] public async Task CompletedReviewCannotBeDeletedOrOverwritten()
    {
        var evidence = await _store.PreviewAsync(_account, null, ReviewKind.Monthly, new(2025, 12, 1));
        await _store.SaveReviewAsync(new(evidence, "实际数据", [], null, "用户结论", ReviewStatus.Completed), ThesisActor.HumanUser);
        await using var db = _factory.CreateDbContext(); db.InvestmentReviews.Remove(await db.InvestmentReviews.SingleAsync()); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task ClosedBeforeQuarterDoesNotKeepCreatingDueTasks()
    {
        await _portfolio.PostAsync(new(_account, _security, new DateTimeOffset(2025, 2, 1, 12, 0, 0, TimeSpan.Zero), TransactionType.Sell, 100, 10, 0, 0, "CNY"));
        Assert.DoesNotContain((await _store.DashboardAsync(_account, new(2026, 1, 1))).Due, x => x.SecurityId == _security);
    }
    [Fact] public async Task SavedBaselineDetectsRealWatchlistChanges()
    {
        await _portfolio.SaveWatchlistAsync(new(_security, WatchlistStage.Candidate, WatchlistPriority.Normal, "研究", "核实", null, RiskStatus.Unknown, ""));
        var baseline = (await _store.PreviewAsync(_account, null, ReviewKind.Monthly, new(2025, 11, 1))) with { CapturedAt = new DateTimeOffset(2025, 11, 30, 12, 0, 0, TimeSpan.Zero) };
        await _store.SaveReviewAsync(new(baseline, "测试历史基准", [], null, "用户保存", ReviewStatus.Completed), ThesisActor.HumanUser);
        await _portfolio.SaveWatchlistAsync(new(_security, WatchlistStage.Researching, WatchlistPriority.High, "新增资料", "继续", null, RiskStatus.Unknown, ""));
        var next = await _store.PreviewAsync(_account, null, ReviewKind.Monthly, new(2025, 12, 1));
        Assert.Contains(next.Sections, x => x.Title.Contains("可验证快照变化") && x.Lines.Any(y => y.Contains("Candidate") && y.Contains("Researching")));
    }
    [Fact] public async Task ReviewAiUsesSavedVersionAndDoesNotChangeHumanChoice()
    {
        var e = await _store.PreviewAsync(_account, _security, ReviewKind.Quarterly, new(2025, 12, 1)); var id = await _store.SaveReviewAsync(new(e, "证据不足", [], ReviewDecision.ContinueResearch, "人工决定继续研究", ReviewStatus.Completed), ThesisActor.HumanUser);
        var context = new AIContextBuilder(_factory, new ResearchStore(_factory, _writer), _risk, new ValuationStore(_factory, _writer), new(), _store);
        var snapshot = await context.BuildAsync(new(_security, _account, AnalysisType.QuarterlyReview, ReviewId: id)); using var doc = JsonDocument.Parse(snapshot.Json);
        Assert.Equal("ContinueResearch", doc.RootElement.GetProperty("UserReview").GetProperty("Decision").GetString()); Assert.Equal("REVIEW:" + id, Assert.Single(snapshot.Sources).Id);
        await Assert.ThrowsAsync<BusinessException>(() => context.BuildAsync(new(_security, _other, AnalysisType.QuarterlyReview, ReviewId: id)));
        Assert.Equal(ReviewDecision.ContinueResearch, Assert.Single(await _store.ReviewsAsync(_account)).Review.Decision);
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}

