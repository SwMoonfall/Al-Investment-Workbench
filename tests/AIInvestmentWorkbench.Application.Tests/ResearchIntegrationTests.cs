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
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class ResearchIntegrationTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.Research.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!;
    private ResearchStore _store = null!;
    private Guid _security;
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        _store = new(_factory, new(_factory));
        await using var db = _factory.CreateDbContext(); var s = new Security("RESEARCH", "测试研究证券", "XSHG", "CNY", SecurityType.Stock); db.Securities.Add(s); await db.SaveChangesAsync(); _security = s.Id;
    }
    public Task DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private MetricDraft Metric(string period = "2025", MetricType type = MetricType.Revenue, decimal value = 123.45m) => new(null, _security, period, PeriodType.Annual, type, value, "CNY");
    private SourceDraft Source() => new(null, _security, "年度报告", "测试公司", null, "https://example.com/report", "C:\\original.pdf", SourceType.AnnualReport, 1, "来源备注", "提取全文 unique-evidence");
    private Task<ImportOutcome> Csv(string text, bool validate = false)
    { var document = CsvImportService.Parse(text); return new FinancialCsvService(_store).ExecuteAsync(_security, document, document.Headers.ToDictionary(x => x, x => x), validate); }
    [Fact]
    public async Task ResearchSourceAndMetrics_PersistAcrossReopen()
    {
        await _store.SaveResearchAsync(_security, new(Overview: "商业概况", UserNotes: "个人笔记"), 0);
        var sourceId = await _store.SaveSourceAsync(Source()); await _store.SaveMetricAsync(Metric() with { SourceId = sourceId });
        var reopened = new ResearchStore(_factory, new(_factory)); var snapshot = await reopened.ReadAsync(_security);
        Assert.Equal("商业概况", snapshot.Research!.Overview); Assert.Equal("个人笔记", snapshot.Research.UserNotes);
        Assert.Equal(123.45m, Assert.Single(snapshot.Metrics).Value); Assert.Equal(sourceId, snapshot.Metrics[0].SourceId);
        Assert.Equal("提取全文 unique-evidence", Assert.Single(snapshot.Sources).ExtractedText);
        Assert.Equal("C:\\original.pdf", snapshot.Sources[0].LocalFilePath);
    }
    [Fact]
    public async Task ConcurrentStaleResearch_OnlyOneWriterWins()
    {
        async Task<bool> Save(string text) { try { await _store.SaveResearchAsync(_security, new(Overview: text), 0); return true; } catch (BusinessException) { return false; } }
        var results = await Task.WhenAll(Task.Run(() => Save("one")), Task.Run(() => Save("two")));
        Assert.Single(results, x => x); Assert.Equal(1, (await _store.ReadAsync(_security)).Research!.Revision);
    }
    [Fact]
    public async Task MetricCrudAndUniqueness()
    {
        await _store.SaveMetricAsync(Metric()); var metric = Assert.Single((await _store.ReadAsync(_security)).Metrics);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveMetricAsync(Metric()));
        await _store.SaveMetricAsync(Metric(value: 999) with { Id = metric.Id }); Assert.Equal(999, Assert.Single((await _store.ReadAsync(_security)).Metrics).Value);
        await _store.DeleteMetricAsync(metric.Id); Assert.Empty((await _store.ReadAsync(_security)).Metrics);
    }
    [Fact]
    public async Task SourceMustBelongToSameSecurity_AndDeletionIsRestricted()
    {
        var source = await _store.SaveSourceAsync(Source()); await _store.SaveMetricAsync(Metric() with { SourceId = source });
        await Assert.ThrowsAsync<BusinessException>(() => _store.DeleteSourceAsync(source));
        await using var db = _factory.CreateDbContext(); var other = new Security("OTHER", "其他", "XSHG", "CNY", SecurityType.Stock); db.Securities.Add(other); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveMetricAsync(Metric() with { SecurityId = other.Id, SourceId = source }));
        await _store.DeleteMetricAsync((await _store.ReadAsync(_security)).Metrics.Single().Id); await _store.DeleteSourceAsync(source); Assert.Empty((await _store.ReadAsync(_security)).Sources);
    }
    [Fact]
    public async Task FinancialCsv_PreviewCommitAndDuplicateAreAtomic()
    {
        const string text = "Period,PeriodType,MetricType,Value,Currency,IsEstimated,Notes\n2025,Annual,Revenue,125.50,CNY,false,实际\n2025,Annual,Capex,12.5,CNY,true,估算";
        Assert.True((await Csv(text, true)).Success); Assert.Empty((await _store.ReadAsync(_security)).Metrics);
        Assert.True((await Csv(text)).Success); Assert.Equal(2, (await _store.ReadAsync(_security)).Metrics.Count);
        Assert.False((await Csv(text)).Success); Assert.Equal(2, (await _store.ReadAsync(_security)).Metrics.Count);
    }
    [Theory]
    [InlineData("2025,Annual,Capex,-1,CNY")]
    [InlineData("2025-Q7,Quarterly,Revenue,1,CNY")]
    [InlineData("2025,Annual,Unknown,1,CNY")]
    [InlineData("2025,Annual,Revenue,20%,CNY")]
    [InlineData("2025,Annual,Revenue,2,CNY")]
    public async Task FinancialCsv_InvalidSecondRowDoesNotPartiallyImport(string bad)
    {
        var result = await Csv("Period,PeriodType,MetricType,Value,Currency\n2025,Annual,Revenue,1,CNY\n" + bad);
        Assert.False(result.Success); Assert.NotEmpty(result.Errors); Assert.Empty((await _store.ReadAsync(_security)).Metrics);
    }
    [Fact]
    public async Task FinancialCsv_CustomMappingAndSourceReference()
    {
        var id = await _store.SaveSourceAsync(Source());
        var document = CsvImportService.Parse($"期间,类型,指标,值,币种,来源\n2025-Q2,Quarterly,GrossMargin,0.45,CNY,{id}");
        var mapping = new Dictionary<string, string> { ["Period"] = "期间", ["PeriodType"] = "类型", ["MetricType"] = "指标", ["Value"] = "值", ["Currency"] = "币种", ["SourceId"] = "来源" };
        Assert.True((await new FinancialCsvService(_store).ExecuteAsync(_security, document, mapping, false)).Success);
        Assert.Equal(.45m, (await _store.ReadAsync(_security)).Metrics.Single().Value);
    }
    [Fact]
    public async Task ScoresUseUserFirstAndKeepAiClearlyMarked()
    {
        var scores = (await _store.ReadAsync(_security)).Scores.Select(s => new ScoreDraft(s.Dimension, s.Weight, null, "理由", "证据")).ToList();
        await _store.SaveScoresAsync(_security, scores);
        await using (var db = _factory.CreateDbContext()) { var score = await db.ResearchScores.FirstAsync(); db.Entry(score).Property(x => x.AIScore).CurrentValue = 90; await db.SaveChangesAsync(); }
        var ai = (await _store.ReadAsync(_security)).Scores.Single(x => x.AIScore.HasValue); Assert.Equal(90, ai.EffectiveScore); Assert.Contains("AI", ai.ScoreOrigin);
        await _store.SaveScoresAsync(_security, scores.Select(x => x.Dimension == ai.Dimension ? x with { UserScore = 0 } : x).ToList());
        var user = (await _store.ReadAsync(_security)).Scores.Single(x => x.Dimension == ai.Dimension); Assert.Equal(0, user.EffectiveScore); Assert.Equal(90, user.AIScore); Assert.Equal("用户评分", user.ScoreOrigin);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveScoresAsync(_security, scores.Select(x => x with { Weight = 1 }).ToList()));
        Assert.Equal(100, (await _store.ReadAsync(_security)).Scores.Sum(x => x.Weight));
    }
    [Fact]
    public async Task ThresholdsPersistAndInvalidValuesDoNotReplaceThem()
    {
        var settings = new RiskThresholds(GrowthGapWarning: .2m); await _store.SaveThresholdsAsync(settings);
        await Assert.ThrowsAsync<BusinessException>(() => _store.SaveThresholdsAsync(new(ConsecutivePeriods: 1)));
        Assert.Equal(settings, await new ResearchStore(_factory, new(_factory)).ReadThresholdsAsync());
    }
    [Fact]
    public async Task GlobalSearchCoversAllRequiredKindsAndFullText()
    {
        await _store.SaveResearchAsync(_security, new(UserNotes: "unique-evidence"), 0); await _store.SaveSourceAsync(Source());
        await using (var db = _factory.CreateDbContext()) { var s = await db.Securities.SingleAsync(x => x.Id == _security); s.Edit(s.Symbol, s.Name, s.Exchange, s.Currency, s.Type, Market.ChinaA, "", "", "", null, "unique-evidence"); db.WatchlistItems.Add(new WatchlistItem(s, "unique-evidence")); await db.SaveChangesAsync(); }
        var hits = await _store.SearchAsync("UNIQUE-evidence");
        Assert.Contains(hits, x => x.Kind == "Security / Notes"); Assert.Contains(hits, x => x.Kind == "Research / Notes"); Assert.Contains(hits, x => x.Kind == "ResearchSource"); Assert.Contains(hits, x => x.Kind == "Watchlist");
    }
    [Fact]
    public async Task ReferencedSecurityCannotBeDeleted()
    {
        await _store.SaveResearchAsync(_security, new(), 0);
        await Assert.ThrowsAsync<BusinessException>(() => new SecurityRepository(_factory, new(_factory)).DeleteAsync(_security));
        Assert.NotNull((await _store.ReadAsync(_security)).Research);
    }
    [Theory] [InlineData(".txt")] [InlineData(".md")] [InlineData(".csv")]
    public async Task TextImportsPreserveUnicode(string extension)
    {
        var path = Path.Combine(_paths.Root, "test" + extension); await File.WriteAllTextAsync(path, "中文资料\n第二行");
        Assert.Equal("中文资料\n第二行", await new ResearchFileReader(new PdfTextExtractor()).ReadAsync(path));
    }
    [Fact]
    public async Task RealPdfTextExtractionAndBlankPdfFailure()
    {
        var builder = new PdfDocumentBuilder(); var page = builder.AddPage(PageSize.A4); var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Research evidence 2025", 12, new PdfPoint(25, 700), font);
        var path = Path.Combine(_paths.Root, "report.pdf"); await File.WriteAllBytesAsync(path, builder.Build());
        var reader = new ResearchFileReader(new PdfTextExtractor()); Assert.Contains("Research evidence 2025", await reader.ReadAsync(path));
        var empty = new PdfDocumentBuilder(); empty.AddPage(PageSize.A4); await File.WriteAllBytesAsync(path, empty.Build());
        var error = await Assert.ThrowsAsync<BusinessException>(() => reader.ReadAsync(path)); Assert.Equal(ResearchFileReader.NoPdfTextMessage, error.Message);
        await File.WriteAllTextAsync(path, "not a pdf"); var invalid = await Assert.ThrowsAsync<BusinessException>(() => reader.ReadAsync(path)); Assert.Contains("损坏", invalid.Message);
    }
    [Fact] public void EmptyPdfTextIsNeverSilentlyAccepted() => Assert.Equal(ResearchFileReader.NoPdfTextMessage, Assert.Throws<BusinessException>(() => ResearchFileReader.ValidateText(" \r\n\t", true)).Message);
    [Fact]
    public async Task UpgradeFromPhase1_PreservesLedgerAndCreatesBackup()
    {
        var paths = new AppPaths(Path.Combine(_paths.Root, "phase1")); paths.EnsureDirectories();
        var factory = new Factory(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)).Options);
        await using (var db = factory.CreateDbContext()) await db.GetService<IMigrator>().MigrateAsync("20261002212028_PortfolioMvp");
        var writer = new DatabaseWriter(factory); var portfolio = new PortfolioStore(factory, writer);
        var account = await portfolio.SaveAccountAsync(new(null, "保留账户", "CNY", 500000, .1m, .4m, .4m, .1m, .1m));
        var security = new Security("PRESERVE", "保留证券", "XSHG", "CNY", SecurityType.Stock);
        await new SecurityRepository(factory, writer).SaveAsync(security, true);
        await portfolio.PostAsync(new(account, security.Id, DateTimeOffset.UtcNow, TransactionType.Buy, 100, 10, 0, 5, "CNY", "历史交易"));
        var before = await portfolio.ReadAsync(account);
        await new DatabaseInitializer(factory, paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var after = await portfolio.ReadAsync(account);
        Assert.Equal(before.TotalAssets, after.TotalAssets); Assert.Equal(before.Cash, after.Cash); Assert.Equal(before.Holdings, after.Holdings); Assert.Equal(before.Transactions, after.Transactions);
        Assert.Single(Directory.GetFiles(paths.BackupsDirectory, "*.db"));
        Assert.Empty((await new ResearchStore(factory, writer).ReadAsync(security.Id)).Metrics);
    }
    [Fact]
    public async Task InvalidTextEncodingAndUnsupportedFilesFailClearly()
    {
        var reader = new ResearchFileReader(new PdfTextExtractor()); var path = Path.Combine(_paths.Root, "invalid.txt");
        await File.WriteAllBytesAsync(path, [0xff, 0xff, 0xff]);
        Assert.Contains("UTF-8", (await Assert.ThrowsAsync<BusinessException>(() => reader.ReadAsync(path))).Message);
        Assert.Contains("仅支持", (await Assert.ThrowsAsync<BusinessException>(() => reader.ReadAsync("anything.exe"))).Message);
    }
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}
