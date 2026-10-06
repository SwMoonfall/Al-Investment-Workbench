using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Valuation;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;

internal static class Phase4Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var security = (await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(x => x.Symbol == "TEST");
        var store = services.GetRequiredService<IValuationStore>(); var dialogs = services.GetRequiredService<ValuationDialogs>();
        var reopened = (await store.ListAsync(security.Id)).Count > 0; var today = DateOnly.FromDateTime(DateTime.Today);
        if (!reopened)
        {
            foreach (var type in Enum.GetValues<ValuationModelType>())
            {
                Guid id = Guid.Empty; var form = dialogs.ModelEditor(security, null, x => id = x);
                Fill(form, ("Name", "验证 · " + type), ("Type", type.ToString()), ("Revenue", "1000"), ("Debt", "100"), ("Shares", "100"), ("Price", "10"), ("Source", "虚构数据，仅软件验收"));
                if (type == ValuationModelType.PE) await Render(form, "ValuationModelEditor.png");
                await Save(form); var model = await Get(id);
                if (type == ValuationModelType.SimplifiedDCF)
                {
                    var invalid = dialogs.ScenarioEditor(model, ScenarioKind.Base); Fill(invalid, ("Discount", "0.01")); await invalid.SaveCommand.ExecuteAsync(null);
                    if (invalid.Error.Length == 0 || (await Get(id)).Revision != model.Revision) throw new InvalidOperationException("Invalid scenario was not rejected safely.");
                }
                form = dialogs.ScenarioEditor(model, ScenarioKind.Base);
                if (type == ValuationModelType.PE) Fill(form, ("EPS", "2"), ("Multiple", "10"), ("Cash", "500"), ("Holding", "2"));
                if (type == ValuationModelType.EV_EBITDA) Fill(form, ("EBITDA", "300"), ("Multiple", "8"));
                if (type == ValuationModelType.EV_FCF) Fill(form, ("FCF", "150"), ("Multiple", "10"));
                if (type == ValuationModelType.ReverseValuation) Fill(form, ("Target", "RevenueCagr"), ("Lower", "-0.5"), ("Upper", "1"));
                Fill(form, ("Notes", "用户设定的测试假设；所有估值运算由代码完成。"));
                if (type == ValuationModelType.SimplifiedDCF) await Render(form, "DCFScenarioEditor.png"); await Save(form);
                model = await Get(id); await store.CopyBaseAsync(id, model.Revision, ScenarioKind.Bear, today); model = await Get(id); await store.CopyBaseAsync(id, model.Revision, ScenarioKind.Bull, today);
                model = await Get(id); form = dialogs.ScenarioEditor(model, ScenarioKind.Bull);
                if (type == ValuationModelType.PE) Fill(form, ("Multiple", "12"));
                else if (type is ValuationModelType.EV_EBITDA or ValuationModelType.EV_FCF) Fill(form, ("Multiple", "12"));
                else if (type == ValuationModelType.SimplifiedDCF) Fill(form, ("Growth", "0.1"), ("Margin", "0.2"));
                else Fill(form, ("Target", "OperatingMargin"), ("Lower", "0"), ("Upper", "0.8"));
                await Save(form);
                if (type == ValuationModelType.ReverseValuation)
                {
                    model = await Get(id); form = dialogs.ScenarioEditor(model, ScenarioKind.Bear); Fill(form, ("Lower", "0.9"), ("Upper", "1")); await Save(form);
                }
            }
        }
        var models = await store.ListAsync(security.Id); if (models.Count != 5) throw new InvalidOperationException("Valuation model count mismatch.");
        var calculator = services.GetRequiredService<ValuationService>();
        foreach (var model in models)
        {
            var result = calculator.Compare(model).Single(x => x.Scenario.Kind == ScenarioKind.Base).Result;
            if (model.ModelType == ValuationModelType.PE && result.ImpliedPrice != 25) throw new InvalidOperationException("PE persistence failed.");
            if (model.ModelType == ValuationModelType.EV_EBITDA && result.ImpliedPrice != 23) throw new InvalidOperationException("EBITDA persistence failed.");
            if (model.ModelType == ValuationModelType.EV_FCF && result.ImpliedPrice != 14) throw new InvalidOperationException("FCF persistence failed.");
            if (model.ModelType == ValuationModelType.SimplifiedDCF && result.Forecast?.Count != 5) throw new InvalidOperationException("DCF annual rows missing.");
            if (model.ModelType == ValuationModelType.ReverseValuation)
            {
                var all = calculator.Compare(model); if (result.Reverse?.Status != SolveStatus.Solved || all.Single(x => x.Scenario.Kind == ScenarioKind.Bull).Result.Reverse?.Status != SolveStatus.Solved || all.Single(x => x.Scenario.Kind == ScenarioKind.Bear).Result.Reverse?.Status != SolveStatus.NoBracket)
                    throw new InvalidOperationException("Reverse valuation outcomes mismatch.");
            }
        }
        var main = services.GetRequiredService<MainViewModel>(); services.GetRequiredService<ResearchSelection>().SecurityId = security.Id;
        await main.NavigateCommand.ExecuteAsync("Valuation"); var vm = (ValuationViewModel)main.Navigation.Current!;
        foreach (var model in vm.Models)
        {
            vm.SelectedModel = model; vm.SelectedKind = ScenarioKind.Base; await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            if (vm.Comparison.Count < 8) throw new InvalidOperationException("Scenario comparison missing."); SmokeTest.SaveRender(window, Path.Combine(paths.Root, $"Valuation-{model.ModelType}.png"));
        }
        var originalWidth = window.Width; var originalHeight = window.Height; window.Width = 1100; window.Height = 680;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle); SmokeTest.SaveRender(window, Path.Combine(paths.Root, "Valuation-Minimum.png")); window.Width = originalWidth; window.Height = originalHeight;
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "valuation-restart.json" : "valuation-flow.json"), JsonSerializer.Serialize(new { Passed = true, Reopened = reopened, Models = models.Select(m => new { m.Name, ModelType = m.ModelType.ToString(), m.Revision, Results = calculator.Compare(m) }), VerifiedAt = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
        async Task<ValuationModelDto> Get(Guid id) => (await store.ListAsync(security.Id)).Single(x => x.Id == id);
        static void Fill(EditorViewModel form, params (string Key, string Value)[] values) { foreach (var (key, value) in values) form.Fields.Single(x => x.Key == key).Value = value; }
        static async Task Save(EditorViewModel form) { await form.SaveCommand.ExecuteAsync(null); if (form.Error.Length > 0) throw new InvalidOperationException(form.Error); }
        async Task Render(EditorViewModel form, string name) { var editor = new EditorWindow(form) { Owner = window }; editor.Show(); await window.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ContextIdle); SmokeTest.SaveRender(editor, Path.Combine(paths.Root, name)); editor.Close(); }
    }
}
