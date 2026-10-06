using System.IO;
using System.Text.Json;
using System.Windows;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;

// Registered only with explicit --smoke-test and isolated --data-root. Never makes HTTP calls.
internal sealed class SmokeAIProvider : IAIProvider
{
    public string Name => "Offline smoke fake";
    public string Mode { get; set; } = "Valid";
    public TaskCompletionSource Started { get; set; } = new();
    public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => StructuredCompleteAsync(request, cancellationToken);
    public async Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        Started.TrySetResult();
        if (Mode == "Wait") await Task.Delay(Timeout.Infinite, cancellationToken);
        if (Mode == "Invalid") return new("OFFLINE TEST: invalid JSON retained", "offline-envelope", "offline-invalid", "offline-test-model");
        if (Mode == "Error") throw new AIProviderException(AIErrorKind.RateLimit);
        using var context = JsonDocument.Parse(request.ContextJson);
        var source = context.RootElement.GetProperty("SourceReferences").EnumerateArray().FirstOrDefault().GetProperty("Id").GetString()!;
        var json = JsonSerializer.Serialize(StructuredAnalysis.Sections.ToDictionary(x => x, x => new[] { new { text = x switch { "FACTS" => "离线验收样例：读取选定证券快照。", "STRONGEST_COUNTERARGUMENTS" => "离线样例：从竞争对手角度检验当前优势是否可持续。", "POTENTIAL_KILL_SIGNALS" => "离线样例：建议核实关键假设；不修改 KillCondition。", _ => "离线验收输出，仅用于检验界面、保存与审计，不代表真实 AI 分析。" }, source_ids = new[] { source } } }));
        return new(json, "offline-envelope", "offline-response", "offline-test-model");
    }
}
internal static class Phase6Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var main = services.GetRequiredService<MainViewModel>(); var store = services.GetRequiredService<IAIStore>(); var fake = (SmokeAIProvider)services.GetRequiredService<IAIProvider>();
        var security = (await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(x => x.Symbol == "TEST");
        services.GetRequiredService<ResearchSelection>().SecurityId = security.Id;
        var account = services.GetRequiredService<WorkspaceContext>().SelectedAccount!; var portfolio = services.GetRequiredService<IPortfolioStore>(); var before = await portfolio.ReadAsync(account.Id);
        await main.NavigateCommand.ExecuteAsync("AIResearch"); var vm = (AIResearchViewModel)main.Navigation.Current!;
        vm.SelectedTemplate = vm.Templates.Single(x => x.AnalysisType == AnalysisType.ThesisChallenge && x.IsBuiltIn); await vm.RefreshCommand.ExecuteAsync();
        var reopened = vm.Analyses.Count > 0;
        if (!reopened)
        {
            await store.SaveSettingsAsync(new("OpenAI", "offline-test-model", 1, 1024));
            await vm.PreviewCommand.ExecuteAsync(); if (!vm.Preview.Contains("CurrentThesis")) throw new InvalidOperationException("Thesis context missing.");
            await vm.RunCommand.ExecuteAsync(); if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.Completed) throw new InvalidOperationException("AI single failed.");
            await vm.FullCommand.ExecuteAsync(); if (vm.SelectedReport?.CompletedSections != 10) throw new InvalidOperationException("AI workflow failed.");
            fake.Mode = "Invalid"; await vm.RunCommand.ExecuteAsync(); if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.InvalidOutput || !vm.SelectedAnalysis.RawOutput.Contains("invalid JSON retained")) throw new InvalidOperationException("AI invalid JSON missing.");
            fake.Mode = "Wait"; fake.Started = new(); var pending = vm.RunCommand.ExecuteAsync(); await fake.Started.Task; vm.CancelCommand.Execute(null); await pending;
            if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.Cancelled || vm.Busy) throw new InvalidOperationException("AI cancellation failed.");
            await vm.RunCommand.ExecuteAsync(); if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.TimedOut) throw new InvalidOperationException("AI timeout failed.");
            fake.Mode = "Error"; await vm.RunCommand.ExecuteAsync(); if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.Failed) throw new InvalidOperationException("AI error isolation failed.");
            fake.Mode = "Valid"; await store.SaveSettingsAsync(new());
        }
        await vm.LoadAsync(); var analyses = await store.AnalysesAsync(security.Id); var report = (await store.ReportsAsync(security.Id)).Single();
        if (analyses.Count != 15 || report.Status != AIAnalysisStatus.Completed || report.CompletedSections != 10) throw new InvalidOperationException("AI audit persistence failed.");
        vm.SelectedAnalysis = analyses.First(x => x.Status == AIAnalysisStatus.Completed);
        var after = await portfolio.ReadAsync(account.Id); if (before.Cash != after.Cash || before.TotalAssets != after.TotalAssets || before.Transactions.Count != after.Transactions.Count) throw new InvalidOperationException("AI changed ledger.");
        await File.WriteAllTextAsync(Path.Combine(paths.Root, "phase6-report.md"), report.ReportMarkdown);
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "phase6-restart.json" : "phase6-flow.json"), JsonSerializer.Serialize(new { Passed = true, Reopened = reopened, Provider = "OFFLINE FAKE; no network/API charge", Analyses = analyses.Count, ReportSections = report.CompletedSections, Statuses = analyses.GroupBy(x => x.Status).ToDictionary(g => g.Key.ToString(), g => g.Count()), before.TotalAssets, before.Cash, Templates = (await store.TemplatesAsync()).Count }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
