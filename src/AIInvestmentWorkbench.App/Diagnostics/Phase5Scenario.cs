using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;

internal static class Phase5Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var securities = await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST"); var security = securities.Single(x => x.Symbol == "TEST");
        var account = services.GetRequiredService<WorkspaceContext>().SelectedAccount!; var store = services.GetRequiredService<IPortfolioRiskStore>();
        var dialogs = services.GetRequiredService<PortfolioRiskDialogs>(); var main = services.GetRequiredService<MainViewModel>();
        services.GetRequiredService<ResearchSelection>().SecurityId = security.Id; await main.NavigateCommand.ExecuteAsync("Risk"); var vm = (RiskViewModel)main.Navigation.Current!; await vm.SelectionLoadTask;
        var reopened = (await store.DecisionsAsync(account.Id)).Count > 0;
        if (!reopened)
        {
            var tagForm = dialogs.TagEditor(); Fill(tagForm, ("Name", "Customer Concentration"), ("Category", "Custom")); await Save(tagForm); await vm.LoadAsync();
            foreach (var name in new[] { "Semiconductor", "AI Capex", "Customer Concentration" }) { vm.SelectedTag = vm.Tags.Single(x => x.Name == name); await vm.AssignTagCommand.ExecuteAsync(null); }
            vm.BasePosition = "0.05"; vm.ConfidenceFactor = "0.8"; vm.ValuationFactor = "1.5"; vm.RiskFactor = "0.5"; await vm.CalculateCommand.ExecuteAsync(null);
            if (!vm.SizingText.Contains(PortfolioRiskRules.SizingNotice)) throw new InvalidOperationException("Sizing notice missing.");
            var sizing = new SizingInputs(.05m, .8m, 1.5m, .5m);
            foreach (var pair in new[] { (DecisionKind.AddPosition, DecisionChoice.Add), (DecisionKind.AddPosition, DecisionChoice.NoAction), (DecisionKind.ExitPosition, DecisionChoice.Hold), (DecisionKind.ExitPosition, DecisionChoice.Trim), (DecisionKind.ExitPosition, DecisionChoice.Exit), (DecisionKind.ExitPosition, DecisionChoice.NoAction) })
            {
                var preview = await store.PreviewAsync(account.Id, security.Id); var form = dialogs.DecisionEditor(preview, pair.Item1, pair.Item1 == DecisionKind.AddPosition ? sizing : null);
                if (pair.Item2 == DecisionChoice.Add)
                {
                    await form.SaveCommand.ExecuteAsync(null); if (form.Error.Length == 0 || (await store.DecisionsAsync(account.Id)).Count != 0) throw new InvalidOperationException("Incomplete checklist should not persist.");
                    form = dialogs.DecisionEditor(preview, pair.Item1, sizing);
                }
                foreach (var q in DecisionRules.Questions(pair.Item1)) Fill(form, (q.Key, "Unknown"));
                Fill(form, ("Choice", pair.Item2.ToString()), ("Reason", $"软件验收：用户选择 {pair.Item2}。已核实存在触发条件；保留未知问题，不执行交易。"));
                if (pair.Item1 == DecisionKind.AddPosition) Fill(form, ("ThesisActive", "No"), ("KillTriggered", "Yes"));
                if (pair.Item2 is DecisionChoice.Add or DecisionChoice.Exit) await Render(form, pair.Item2 + "Checklist.png");
                await Save(form);
            }
        }
        var risk = await store.ReadAsync(account.Id); var records = await store.DecisionsAsync(account.Id);
        if (records.Count != 6 || risk.TriggeredCount != 1 || risk.ThesisWarningCount != 1 || risk.HighAttention.Count == 0) throw new InvalidOperationException("Risk signals or decisions failed.");
        var holding = risk.Positions.Single(x => x.SecurityId == security.Id);
        if (risk.Exposure.Exposures.Single(x => x.Name == "Semiconductor").Weight != holding.Weight || records.Any(x => x.Context.TriggeredCount != 1)) throw new InvalidOperationException("Risk tag aggregation / snapshot failed.");
        var portfolio = await services.GetRequiredService<IPortfolioStore>().ReadAsync(account.Id);
        if (portfolio.TotalAssets != 501187 || portfolio.Transactions.Count != 6) throw new InvalidOperationException("Decision unexpectedly changed ledger.");
        await vm.LoadAsync(); vm.BasePosition = "0.05"; vm.ConfidenceFactor = "0.8"; vm.ValuationFactor = "1.5"; vm.RiskFactor = "0.5"; await vm.CalculateCommand.ExecuteAsync(null);
        if (vm.Records.Count != 6 || vm.Alerts.Count == 0 || !vm.RecordText.Contains("当时系统快照")) throw new InvalidOperationException("Risk view state failed.");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle); await Phase2Scenario.CaptureTabsAsync(window, paths.Root, "RiskFlow", "Light");
        var width = window.Width; var height = window.Height; window.Width = 1100; window.Height = 680;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle); await Phase2Scenario.CaptureTabsAsync(window, paths.Root, "RiskMinimum", "Light"); window.Width = width; window.Height = height;
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "risk-restart.json" : "risk-flow.json"), JsonSerializer.Serialize(new { Passed = true, Reopened = reopened, risk.TotalAssets, risk.TriggeredCount, risk.ThesisWarningCount, HighAttentionCount = risk.HighAttention.Count, Decisions = records.Select(x => new { x.Id, x.Choice, x.Kind, x.Context.CurrentWeight, x.Context.TriggeredCount }), Exposures = risk.Exposure, VerifiedAt = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        static void Fill(EditorViewModel form, params (string Key, string Value)[] values) { foreach (var (key, value) in values) form.Fields.Single(x => x.Key == key).Value = value; }
        static async Task Save(EditorViewModel form) { await form.SaveCommand.ExecuteAsync(null); if (form.Error.Length > 0) throw new InvalidOperationException(form.Error); }
        async Task Render(EditorViewModel form, string name) { var editor = new EditorWindow(form) { Owner = window }; editor.Show(); await window.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ContextIdle); SmokeTest.SaveRender(editor, Path.Combine(paths.Root, name)); editor.Close(); }
    }
}
