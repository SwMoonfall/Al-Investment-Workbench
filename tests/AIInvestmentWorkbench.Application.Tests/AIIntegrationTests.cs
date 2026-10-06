using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace AIInvestmentWorkbench.Application.Tests;

public sealed class FakeAIProvider : IAIProvider
{
    public string Name => "Fake";
    public List<AIRequest> Requests { get; } = [];
    public Func<AIRequest, CancellationToken, Task<AIProviderResult>> Respond { get; set; } = (_, _) => Task.FromResult(new AIProviderResult(ValidJson()));
    public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Respond(request, cancellationToken); }
    public Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => CompleteAsync(request, cancellationToken);
    public static string ValidJson(string? factSource = null) => JsonSerializer.Serialize(StructuredAnalysis.Sections.ToDictionary(x => x, x => x == "FACTS" && factSource is not null ? new[] { new { text = "测试事实", source_ids = new[] { factSource } } } : x == "QUESTIONS" ? [new { text = "需要进一步核实。", source_ids = Array.Empty<string>() }] : []));
}

public sealed class AIIntegrationTests : IAsyncLifetime
{
    private readonly AppPaths _paths = new(Path.Combine(Path.GetTempPath(), "Workbench.AI.Tests", Guid.NewGuid().ToString("N")));
    private Factory _factory = null!; private DatabaseWriter _writer = null!; private AIStore _store = null!; private AIContextBuilder _context = null!;
    private Guid _security, _other, _account; private FakeAIProvider _fake = null!; private AIAnalysisService _service = null!;
    public async Task InitializeAsync()
    {
        _paths.EnsureDirectories(); _factory = new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(InvestmentDbContextFactory.ConnectionString(_paths)).Options);
        await new DatabaseInitializer(_factory, _paths, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(); _writer = new(_factory);
        _store = new(_factory, _writer); await _store.InitializeAsync(); await _store.SaveSettingsAsync(new("OpenAI", "offline-test-model", 2, 1024));
        var risk = new PortfolioRiskStore(_factory, _writer); await risk.InitializeDefaultsAsync();
        _context = new(_factory, new ResearchStore(_factory, _writer), risk, new ValuationStore(_factory, _writer), new ValuationService(), new JournalReviewStore(_factory, _writer, new PortfolioStore(_factory, _writer), risk, new ThesisStore(_factory, _writer), new ValuationStore(_factory, _writer), new ValuationService(), new ResearchStore(_factory, _writer)));
        _fake = new(); _service = new(_store, _context, _fake);
        _account = await new PortfolioStore(_factory, _writer).SaveAccountAsync(new(null, "本地账户", "CNY", 500000, .1m, .4m, .4m, .1m, .25m));
        await using var db = _factory.CreateDbContext(); var s = new Security("AI", "选定证券", "XSHG", "CNY", SecurityType.Stock); var other = new Security("PRIVATE", "不应外发的其他证券", "XSHG", "CNY", SecurityType.Stock); _security = s.Id; _other = other.Id; db.AddRange(s, other);
        db.ResearchSources.Add(new(s.Id, "选定年报", "用户", null, "", "C:\\private-file.pdf", SourceType.AnnualReport, 1, extractedText: new string('文', 4000)));
        db.FinancialMetrics.AddRange(new FinancialMetric(s.Id, "2025", PeriodType.Annual, MetricType.OperatingCashFlow, 100, "CNY"), new FinancialMetric(s.Id, "2025", PeriodType.Annual, MetricType.Capex, 30, "CNY"));
        db.CompanyResearches.Add(new(other.Id)); await db.SaveChangesAsync();
    }
    public Task DisposeAsync() { _service.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(_paths.Root, true); return Task.CompletedTask; }
    private async Task<PromptTemplate> Template(AnalysisType type = AnalysisType.ThesisChallenge) => (await _store.TemplatesAsync()).Single(x => x.AnalysisType == type && x.IsBuiltIn);
    private async Task<AnalysisOutcome> Run(CancellationToken ct = default) => await _service.RunAsync((await Template()).Id, new(_security, _account, AnalysisType.ThesisChallenge), ct);
    [Fact] public async Task TemplatesEditableCopyableRestorableAndSnapshotsImmutable()
    {
        var t = await Template(); Assert.Equal(15, (await _store.TemplatesAsync()).Count); await _store.InitializeAsync(); Assert.Equal(15, (await _store.TemplatesAsync()).Count);
        var outcome = await Run(); var copy = await _store.CopyTemplateAsync(t.Id); await _store.EditTemplateAsync(t.Id, t.Revision, "修改模板", "用户定制问题");
        Assert.NotEqual(t.Id, copy); Assert.False((await _store.TemplatesAsync()).Single(x => x.Id == copy).IsBuiltIn);
        var changed = await Template(); Assert.Equal("用户定制问题", changed.Body); await Assert.ThrowsAsync<BusinessException>(() => _store.EditTemplateAsync(t.Id, t.Revision, "过期", "不会覆盖"));
        await _store.RestoreTemplateAsync(changed.Id, changed.Revision); Assert.Equal(PromptCatalog.Body(t.AnalysisType), (await Template()).Body);
        var audit = Assert.Single(await _store.AnalysesAsync(_security)); Assert.Equal(outcome.Id, audit.Id); Assert.Contains(t.Body, audit.PromptSnapshot); Assert.DoesNotContain("用户定制问题", audit.PromptSnapshot);
        await Assert.ThrowsAsync<BusinessException>(() => _store.FinishAsync(audit.Id, AIAnalysisStatus.Failed, "overwrite", "", ""));
    }
    [Fact] public async Task PromptHasMandatoryChallengeAndNoMutationTools()
    {
        await Run(); var request = Assert.Single(_fake.Requests); Assert.Contains("做空者", request.Instructions); Assert.Contains("竞争对手", request.Instructions); Assert.Contains("保守型基金经理", request.Instructions); Assert.Contains("不得修改 Thesis", request.Instructions); Assert.Contains("不要充当估值计算器", request.Instructions); Assert.Equal(StructuredAnalysis.Schema, request.JsonSchema);
        await using var db = _factory.CreateDbContext(); Assert.Empty(db.Theses); Assert.Empty(db.Transactions); Assert.Empty(db.KillConditions);
    }
    [Fact] public async Task ContextSelectsOnlyTaskDataAndComputesFcfInCode()
    {
        var overview = await _context.BuildAsync(new(_security, _account, AnalysisType.CompanyOverview));
        Assert.DoesNotContain(_other.ToString(), overview.Json); Assert.DoesNotContain("Financials", overview.Json); Assert.DoesNotContain("private-file", overview.Json); Assert.DoesNotContain("Portfolio", overview.Json);
        var financial = await _context.BuildAsync(new(_security, _account, AnalysisType.FinancialQuality)); using var doc = JsonDocument.Parse(financial.Json);
        Assert.Equal(70, doc.RootElement.GetProperty("CalculatedFCF")[0].GetProperty("Value").GetDecimal()); Assert.True(doc.RootElement.GetProperty("ResearchSources")[0].GetProperty("Excerpt").GetString()!.Length < 2100);
        var portfolio = await _context.BuildAsync(new(null, _account, AnalysisType.PortfolioRiskReview)); using var p = JsonDocument.Parse(portfolio.Json); Assert.Equal(500000, p.RootElement.GetProperty("Portfolio").GetProperty("TotalAssets").GetDecimal()); Assert.False(p.RootElement.TryGetProperty("Financials", out _));
        var journal = await _context.BuildAsync(new(null, null, AnalysisType.JournalReview, "我的复盘")); using var j = JsonDocument.Parse(journal.Json); Assert.True(j.RootElement.GetProperty("Journal").GetProperty("Available").GetBoolean()); Assert.Equal(0, j.RootElement.GetProperty("Journal").GetProperty("Items").GetArrayLength());
    }
    [Fact] public async Task InvalidJsonIsPreservedAlongWithEnvelopeAndError()
    {
        _fake.Respond = (_, _) => Task.FromResult(new AIProviderResult("not-json <raw>", "envelope", "resp_123", "returned-model"));
        Assert.Equal(AIAnalysisStatus.InvalidOutput, (await Run()).Status); var row = Assert.Single(await new AIStore(_factory, _writer).AnalysesAsync(_security));
        Assert.Equal("not-json <raw>", row.RawOutput); Assert.Equal("envelope", row.ResponseEnvelope); Assert.Equal("returned-model", row.ResponseModel); Assert.Equal("resp_123", row.ResponseId); Assert.NotEmpty(row.ErrorMessage); Assert.NotEmpty(row.ContextSnapshot); Assert.NotEmpty(row.SourceReferences);
    }
    [Theory] [InlineData(AIErrorKind.Authentication)] [InlineData(AIErrorKind.Quota)] [InlineData(AIErrorKind.RateLimit)] [InlineData(AIErrorKind.Network)] [InlineData(AIErrorKind.Provider)]
    public async Task ProviderErrorsAreIsolatedAndPersisted(AIErrorKind kind)
    {
        _fake.Respond = (_, _) => throw new AIProviderException(kind); Assert.Equal(AIAnalysisStatus.Failed, (await Run()).Status);
        Assert.Equal(AIErrorMessages.For(kind), Assert.Single(await _store.AnalysesAsync(_security)).ErrorMessage);
        Assert.Equal(500000, (await new PortfolioStore(_factory, _writer).ReadAsync(_account)).TotalAssets);
    }
    [Fact] public async Task UnexpectedProviderMessageCannotLeakIntoAudit()
    { _fake.Respond = (_, _) => throw new InvalidOperationException("sk-secret-sensitive"); await Run(); var row = Assert.Single(await _store.AnalysesAsync(_security)); Assert.DoesNotContain("sk-secret", row.ErrorMessage); }
    [Fact] public async Task CancellationIsSavedUsingUncancelledDatabaseToken()
    {
        using var ct = new CancellationTokenSource(); var started = new TaskCompletionSource();
        _fake.Respond = async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return new(""); };
        var run = Run(ct.Token); await started.Task; ct.Cancel(); Assert.Equal(AIAnalysisStatus.Cancelled, (await run).Status); Assert.Equal(AIAnalysisStatus.Cancelled, Assert.Single(await _store.AnalysesAsync(_security)).Status);
    }
    [Fact] public async Task TimeoutIsBoundedEvenWhenProviderIgnoresToken()
    {
        await _store.SaveSettingsAsync(new("OpenAI", "offline", 1, 1024)); var pending = new TaskCompletionSource<AIProviderResult>(); _fake.Respond = (_, _) => pending.Task;
        var result = await Run().WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(AIAnalysisStatus.TimedOut, result.Status); pending.SetResult(new(FakeAIProvider.ValidJson()));
        Assert.Equal(AIAnalysisStatus.TimedOut, Assert.Single(await _store.AnalysesAsync(_security)).Status);
    }
    [Fact] public async Task FullWorkflowHasTenAuditsAndDurableUnifiedReport()
    {
        await _service.FullCompanyAsync(_security, _account, ""); var report = Assert.Single(await _store.ReportsAsync(_security)); Assert.Equal(10, report.CompletedSections); Assert.Equal(AIAnalysisStatus.Completed, report.Status);
        Assert.Equal(10, (await _store.AnalysesAsync(_security)).Count); Assert.Contains("Thesis Challenge", report.ReportMarkdown); Assert.Contains("Monitoring KPI", report.ReportMarkdown); Assert.Equal(10, _fake.Requests.Count);
    }
    [Fact] public async Task PartialWorkflowDoesNotPretendToBeComplete()
    {
        _fake.Respond = (_, _) => _fake.Requests.Count == 3 ? throw new AIProviderException(AIErrorKind.Network) : Task.FromResult(new AIProviderResult(FakeAIProvider.ValidJson()));
        await _service.FullCompanyAsync(_security, _account, ""); var report = Assert.Single(await _store.ReportsAsync(_security)); Assert.Equal(2, report.CompletedSections); Assert.Equal(AIAnalysisStatus.Failed, report.Status); Assert.Contains("2/10", report.ReportMarkdown); Assert.Equal(3, _fake.Requests.Count);
    }
    [Fact] public async Task InterruptedStartupNeverRetriesPaidRequest()
    {
        var t = await Template(); await _store.BeginAsync(new(_security, t.AnalysisType, t.Id, "prompt", "model", "OpenAI", "[]", "{}", "{}")); await _store.BeginReportAsync(_security, "pending"); await _store.InitializeAsync();
        Assert.Equal(AIAnalysisStatus.Interrupted, Assert.Single(await _store.AnalysesAsync(_security)).Status); Assert.Equal(AIAnalysisStatus.Interrupted, Assert.Single(await _store.ReportsAsync(_security)).Status); Assert.Empty(_fake.Requests);
    }
    [Fact] public async Task CancelledWorkflowRetainsPreviousSuccessfulSections()
    {
        using var cancellation = new CancellationTokenSource();
        _fake.Respond = (_, ct) => { if (_fake.Requests.Count == 3) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); } return Task.FromResult(new AIProviderResult(FakeAIProvider.ValidJson())); };
        await _service.FullCompanyAsync(_security, _account, "", ct: cancellation.Token);
        var report = Assert.Single(await _store.ReportsAsync(_security)); Assert.Equal(2, report.CompletedSections); Assert.Equal(AIAnalysisStatus.Cancelled, report.Status); Assert.Equal(3, (await _store.AnalysesAsync(_security)).Count);
    }
    [Fact] public async Task IncompleteResponseIsNotReportedAsSuccessful()
    {
        _fake.Respond = (_, _) => Task.FromResult(new AIProviderResult("partial text", "raw-envelope", Completed: false)); await Run();
        var row = Assert.Single(await _store.AnalysesAsync(_security)); Assert.Equal(AIAnalysisStatus.Failed, row.Status); Assert.Equal("partial text", row.RawOutput); Assert.Empty(row.StructuredOutput);
    }
    [Fact] public async Task DisabledProviderNeverRunsAndDoesNotAffectLocalPortfolio()
    {
        await _store.SaveSettingsAsync(new()); Assert.Equal(AIAnalysisStatus.Failed, (await Run()).Status); Assert.Empty(_fake.Requests);
        Assert.Equal(500000, (await new PortfolioStore(_factory, _writer).ReadAsync(_account)).Cash);
    }
    [Fact] public async Task TerminalAuditCannotBeAlteredOrDeletedThroughTrackedContext()
    {
        await Run(); await using var db = _factory.CreateDbContext(); var row = await db.AIAnalyses.SingleAsync(); db.Entry(row).Property(x => x.RawOutput).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact] public async Task EncryptedSecretRoundTripIsEndpointScopedAndNotInDatabase()
    {
        if (!OperatingSystem.IsWindows()) return; var secret = new WindowsSecretStore(_paths); const string value = "sk-fake-unit-test-only";
        await secret.WriteAsync("https://first.example/v1/", value); Assert.Equal(value, await secret.ReadAsync("https://first.example/v1/")); Assert.Null(await secret.ReadAsync("https://second.example/v1/"));
        Assert.DoesNotContain(value, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, "Secrets"), "*.dpapi")))));
        Assert.DoesNotContain(value, JsonSerializer.Serialize(await _store.ReadSettingsAsync())); await secret.DeleteAsync("https://first.example/v1/"); Assert.Null(await secret.ReadAsync("https://first.example/v1/"));
    }
    [Theory] [InlineData("{}")] [InlineData("[]")] [InlineData("```json\n{}\n```")] [InlineData("{invalid")]
    public void InvalidStructuredOutputRejected(string raw) => Assert.False(StructuredAnalysis.TryParse(raw, [], out _, out _));
    [Fact] public void UnknownReferencesRejectedAndKnownReferencesAccepted()
    {
        var raw = FakeAIProvider.ValidJson("source-1"); Assert.False(StructuredAnalysis.TryParse(raw, [], out _, out _)); Assert.True(StructuredAnalysis.TryParse(raw, [new("source-1", "AnnualReport", "Report", "2025")], out _, out _));
    }
    [Theory] [InlineData("http://example.com")] [InlineData("https://user:secret@example.com")] [InlineData("https://example.com?key=secret")] [InlineData("file:///x")]
    public void InsecureEndpointsRejected(string endpoint) => Assert.Throws<BusinessException>(() => new AISettings(BaseUrl: endpoint).Validate());
    private sealed class Factory(DbContextOptions<InvestmentDbContext> options) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => new(options); }
}


