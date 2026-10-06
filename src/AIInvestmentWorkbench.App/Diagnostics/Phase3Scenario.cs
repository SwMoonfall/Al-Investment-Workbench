using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Navigation;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;

internal static class Phase3Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var security = (await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(x => x.Symbol == "TEST");
        var reader = services.GetRequiredService<IThesisReader>(); var dialogs = services.GetRequiredService<ThesisDialogs>();
        var initial = await reader.ListAsync(security.Id); var reopened = initial.Count > 0;
        if (!reopened)
        {
            Guid oldId = Guid.Empty;
            var form = dialogs.ThesisEditor(security.Id, null, id => oldId = id);
            Fill(form, ("Title", "历史逻辑 · 扩产带来盈利提升"), ("Summary", "原假设：新增产能会提升利润率。该示例仅用于软件验证。"), ("Market", "市场可能低估产能利用率提升的速度。")); await Save(form);
            form = dialogs.StatusEditor(await Get(oldId)); Fill(form, ("Status", "Active"), ("Reason", "用户确认初始逻辑")); await Save(form);
            form = dialogs.KillEditor(await Get(oldId)); Condition(form, "30"); await Save(form);
            form = dialogs.ThesisEditor(security.Id, await Get(oldId)); Fill(form, ("Summary", "补充核实：扩产未必带来毛利率改善，必须跟踪营业利润率。"), ("Reason", "阅读新增披露后修订观点")); await Save(form);
            var old = await Get(oldId); form = dialogs.KillEditor(old, old.KillConditions.Single()); Fill(form, ("Current", "8"), ("Evidence", "虚构年报：营业利润率 8%。"), ("Reason", "用户核实触发条件")); await Save(form);
            form = dialogs.StatusEditor(await Get(oldId)); Fill(form, ("Status", "Invalidated"), ("Reason", "人工确认关键前提已被证伪")); await Save(form);
            form = dialogs.ArchiveEditor(await Get(oldId)); Fill(form, ("Reason", "保留历史，重新建立独立观点")); await Save(form);
            Guid currentId = Guid.Empty;
            form = dialogs.ThesisEditor(security.Id, null, id => currentId = id);
            Fill(form, ("Title", "当前逻辑 · 服务收入与现金转换"), ("Summary", "验证用股份的服务收入有望改善现金转换。必须以客户续约、营业现金流和利润率验证，不依赖单一估值叙事。"),
                ("Market", "市场可能把一次性设备收入放缓外推到长期服务业务；该判断需要续约与回款证据。"), ("Holding", "12–24 个月，每季度复盘"),
                ("Base", "服务收入稳定增长，利润率维持，现金流逐步改善。"), ("Bull", "高续约率与回款改善共同驱动增长，新增客户贡献超预期。"), ("Bear", "客户续约恶化，利润率跌破底线，原逻辑需要重新评估。"),
                ("Catalysts", "下季度续约披露；经营现金流数据；新产品客户验收。"), ("Risks", "回款拖延、客户集中、利润率下滑。示例数据不是投资建议。"), ("Review", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd")));
            await Render(form, "ThesisEditor.png"); await Save(form);
            form = dialogs.AssumptionEditor(await Get(currentId)); Fill(form, ("Description", "续约率保持在 90% 附近"), ("Metric", "RenewalRate"), ("Baseline", "90"), ("Expected", "92"), ("Warning", "85"), ("Kill", "80"), ("Current", "88"), ("Unit", "%"), ("Evidence", "虚构客户续约资料"), ("Reason", "把增长叙事转换为可验证假设")); await Save(form);
            form = dialogs.KillEditor(await Get(currentId)); Condition(form, "30"); await Render(form, "KillConditionEditor.png"); await Save(form);
            form = dialogs.StatusEditor(await Get(currentId)); Fill(form, ("Status", "Active"), ("Reason", "用户确认当前逻辑")); await Save(form);
            var current = await Get(currentId); form = dialogs.KillEditor(current, current.KillConditions.Single()); Fill(form, ("Current", "9"), ("Evidence", "虚构最新披露：营业利润率 9%。"), ("Reason", "核对新数据，条件触发")); await Save(form);
            form = dialogs.ThesisEditor(security.Id, await Get(currentId)); Fill(form, ("Risks", "营业利润率已触发关键条件，需核对一次性因素与持续性；在复盘中由用户决定是否失效。"), ("Reason", "记录触发后的待核实问题")); await Save(form);
        }
        var rows = await reader.ListAsync(security.Id); var active = rows.Single(x => x.IsCurrent); var archived = rows.Single(x => x.Archived);
        if (active.Version != 6 || active.Status != ThesisStatus.Warning || active.TriggeredCount != 1 || active.Assumptions.Count != 1 || archived.Version != 7 || archived.Status != ThesisStatus.Invalidated)
            throw new InvalidOperationException("Thesis lifecycle smoke verification failed.");
        var history = await reader.HistoryAsync(active.Id);
        if (history.Count != 6 || history.Single(x => x.Version == 3).Snapshot.KillConditions.Single().Content.CurrentValue != 30 || history[0].Snapshot.KillConditions.Single().Content.CurrentValue != 9)
            throw new InvalidOperationException("Thesis snapshots failed.");
        var main = services.GetRequiredService<MainViewModel>(); var navigation = services.GetRequiredService<NavigationService>();
        foreach (var page in new[] { "Dashboard", "Portfolio", "Watchlist" })
        {
            await main.NavigateCommand.ExecuteAsync(page);
            var panel = navigation.Current switch { DashboardViewModel d => d.ThesisPanel, PortfolioViewModel p => p.ThesisPanel, WatchlistViewModel w => w.ThesisPanel, _ => throw new InvalidOperationException() };
            if (!panel.Items.Any(x => x.Id == active.Id && x.NeedsReview && x.Status == "Warning" && x.Signal == "Red")) throw new InvalidOperationException("Review indicator missing from " + page);
        }
        var selection = services.GetRequiredService<ResearchSelection>(); selection.SecurityId = security.Id; selection.ThesisId = active.Id;
        await main.NavigateCommand.ExecuteAsync("Thesis"); var vm = (ThesisViewModel)navigation.Current!; await vm.HistoryLoadTask;
        if (vm.Selected?.Id != active.Id || vm.History.Count != 6 || !vm.ReviewNotice.Contains("需要复盘")) throw new InvalidOperationException("Thesis view state failed.");
        var width = window.Width; var height = window.Height;
        window.Width = 1100; window.Height = 680;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        await Phase2Scenario.CaptureTabsAsync(window, paths.Root, "ThesisMinimum", "Light");
        window.Width = width; window.Height = height;
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "thesis-restart.json" : "thesis-flow.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Reopened = reopened, CurrentStatus = active.Status.ToString(), active.IsCurrent, active.Version, active.TriggeredCount,
            HistoryVersions = history.Select(x => x.Version), ArchivedVersion = archived.Version, ReviewPanels = new[] { "Dashboard", "Portfolio", "Watchlist" }, VerifiedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
        async Task<ThesisDetail> Get(Guid id) => (await reader.ListAsync(security.Id)).Single(x => x.Id == id);
        static void Fill(EditorViewModel form, params (string Key, string Value)[] values) { foreach (var (key, value) in values) form.Fields.Single(x => x.Key == key).Value = value; }
        static void Condition(EditorViewModel form, string current) => Fill(form, ("Title", "营业利润率跌破关键底线"), ("Description", "盈利能力是现金转换逻辑的必要前提；触发后核对财报并由用户判断。"), ("Metric", "OperatingMargin"), ("Direction", "AtOrBelow"), ("Warning", "15"), ("Trigger", "10"), ("Current", current), ("Unit", "%"), ("Evidence", "虚构财报，用于软件验收"), ("Reason", "用户定义可证伪条件"));
        static async Task Save(EditorViewModel form) { await form.SaveCommand.ExecuteAsync(null); if (form.Error.Length > 0) throw new InvalidOperationException(form.Error); }
        async Task Render(EditorViewModel form, string name) { var editor = new EditorWindow(form) { Owner = window }; editor.Show(); await window.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ContextIdle); SmokeTest.SaveRender(editor, Path.Combine(paths.Root, name)); editor.Close(); }
    }
}
