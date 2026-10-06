using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;
internal static class Phase7Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var account = services.GetRequiredService<WorkspaceContext>().SelectedAccount!; var reader = services.GetRequiredService<IJournalReviewReader>(); var dialogs = services.GetRequiredService<JournalDialogs>();
        var portfolio = services.GetRequiredService<IPortfolioStore>(); var before = await portfolio.ReadAsync(account.Id); var main = services.GetRequiredService<MainViewModel>();
        var security = (await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(x => x.Symbol == "TEST");
        services.GetRequiredService<ResearchSelection>().SecurityId = security.Id; await main.NavigateCommand.ExecuteAsync("Journal"); var vm = (JournalViewModel)main.Navigation.Current!;
        var reopened = (await reader.JournalsAsync(account.Id)).Count > 0;
        var reviewAnchor = JournalRules.LastCompletedQuarter(DateOnly.FromDateTime(DateTime.Today)).End;
        vm.Kind = ReviewKind.Weekly; services.GetRequiredService<ResearchSelection>().QuarterlyReviewAnchor = reviewAnchor; await vm.LoadAsync();
        if (vm.Kind != ReviewKind.Quarterly || vm.Anchor != reviewAnchor.ToString("yyyy-MM-dd") || vm.SelectedTab != 1 || vm.SelectedSecurity?.Id != security.Id) throw new InvalidOperationException("Quarterly review navigation failed.");
        if (!reopened)
        {
            var decision = (await services.GetRequiredService<IPortfolioRiskStore>().DecisionsAsync(account.Id)).Single(x => x.Choice == DecisionChoice.Add);
            var form = await dialogs.JournalEditorAsync(account.Id, DecisionJournalMapper.Draft(decision));
            if ((await reader.JournalsAsync(account.Id)).Count != 0) throw new InvalidOperationException("Draft saved before user confirmation.");
            Fill(form, ("Price", "12"), ("Quantity", "100"), ("Horizon", "至少一年，等待经营证据"), ("Days", "365"), ("Confidence", "85"), ("Emotion", "谨慎"));
            await Render(form, "JournalEditor.png"); await Save(form);
            var journal = (await reader.JournalsAsync(account.Id)).Single().Journal;
            form = await dialogs.JournalEditorAsync(account.Id, new(account.Id, journal.SecurityId, journal.Content, journal.DecisionRecordId, journal.Id)); Fill(form, ("Reason", "用户补充证据；短期下跌不自动代表逻辑错误。"), ("Correction", "补充研究背景")); await Save(form);
            foreach (var kind in Enum.GetValues<ReviewKind>())
            {
                var today = DateOnly.FromDateTime(DateTime.Today); var anchor = kind == ReviewKind.Weekly ? today.AddDays(-7) : kind == ReviewKind.Monthly ? new DateOnly(today.Year, today.Month, 1).AddDays(-1) : JournalRules.LastCompletedQuarter(today).End;
                vm.Kind = kind; vm.Anchor = anchor.ToString("yyyy-MM-dd"); await vm.GenerateCommand.ExecuteAsync(); if (!vm.PreviewText.Contains("持仓数量变化")) throw new InvalidOperationException("Review preview not generated.");
                var evidence = await reader.PreviewAsync(account.Id, kind == ReviewKind.Quarterly ? security.Id : null, kind, anchor);
                form = dialogs.ReviewEditor(evidence); Fill(form, ("Actual", "软件验收：核对已有资料；没有期初历史数据的部分明确保留缺口。"), ("Conclusion", "用户复盘确认；无自动交易。"), ("Status", kind == ReviewKind.Quarterly ? "Draft" : "Completed"));
                if (kind == ReviewKind.Quarterly) { Fill(form, ("Decision", "ContinueResearch")); await Render(form, "QuarterlyReviewEditor.png"); }
                await Save(form);
                if (kind == ReviewKind.Quarterly)
                {
                    var draft = (await reader.ReviewsAsync(account.Id)).Single(x => x.Review.Kind == ReviewKind.Quarterly).Review;
                    form = dialogs.ReviewEditor(evidence, draft); Fill(form, ("Actual", "已核对本季度资料与缺失历史，继续研究。"), ("Conclusion", "由用户确认完成；不更改 Thesis 状态。"), ("Decision", "ContinueResearch"), ("Status", "Completed"));
                    foreach (var a in evidence.Assumptions) Fill(form, (a.Id.ToString(), "Unchanged"), (a.Id + "Evidence", "离线验收人工记录，保持未知风险提醒。"));
                    await Save(form);
                }
            }
            var aiStore = services.GetRequiredService<IAIStore>(); await aiStore.SaveSettingsAsync(new("OpenAI", "offline-phase7", 5, 1024));
            await vm.JournalAICommand.ExecuteAsync(); await aiStore.SaveSettingsAsync(new());
            if (vm.SelectedAnalysis?.Status != AIAnalysisStatus.Completed) throw new InvalidOperationException("Journal AI failed.");
        }
        await vm.LoadAsync(); var journals = await reader.JournalsAsync(account.Id, true); var reviews = await reader.ReviewsAsync(account.Id, true);
        if (journals.Count != 2 || journals.Select(x => x.Journal.Reason).Distinct().Count() != 2 || reviews.Count != 4 || (await reader.ReviewsAsync(account.Id)).Count(x => x.Review.Status == ReviewStatus.Completed) != 3) throw new InvalidOperationException("Journal/review persistence or history failed.");
        var dashboard = await reader.DashboardAsync(account.Id, DateOnly.FromDateTime(DateTime.Today)); if (dashboard.RecentJournals.Count != 1 || !dashboard.RecentAI.Any(x => x.AnalysisType == AnalysisType.JournalReview && x.Status == AIAnalysisStatus.Completed)) throw new InvalidOperationException("Journal dashboard failed.");
        var after = await portfolio.ReadAsync(account.Id); if (before.Cash != after.Cash || before.TotalAssets != after.TotalAssets || before.Transactions.Count != after.Transactions.Count) throw new InvalidOperationException("Journal changed portfolio.");
        var width = window.Width; var height = window.Height; window.Width = 1100; window.Height = 680; await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle); await Phase2Scenario.CaptureTabsAsync(window, paths.Root, "JournalMinimum", "Light"); window.Width = width; window.Height = height;
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "phase7-restart.json" : "phase7-flow.json"), JsonSerializer.Serialize(new { Passed = true, Reopened = reopened, JournalVersions = journals.Count, CurrentJournals = 1, ReviewVersions = reviews.Count, CompletedReviews = 3, AIReview = "Offline FakeAIProvider; no real network", LedgerUnchanged = true, after.TotalAssets, after.Cash }, new JsonSerializerOptions { WriteIndented = true }));
        async Task Render(EditorViewModel form, string filename)
        { var editor = new EditorWindow(form) { Owner = window }; editor.Show(); await window.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ContextIdle); SmokeTest.SaveRender(editor, Path.Combine(paths.Root, filename)); editor.Close(); }
    }
    private static void Fill(EditorViewModel form, params (string Key, string Value)[] fields) { foreach (var item in fields) form.Fields.Single(x => x.Key == item.Key).Value = item.Value; }
    private static async Task Save(EditorViewModel form) { await form.SaveCommand.ExecuteAsync(); if (form.Error.Length > 0) throw new InvalidOperationException("Journal editor validation failed: " + form.Error); }
}
