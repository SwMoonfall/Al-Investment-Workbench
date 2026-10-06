using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Navigation;
using Microsoft.Extensions.Logging;

namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class MainViewModel
{
    private readonly System.Windows.Threading.DispatcherTimer _calendarTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public MainViewModel(NavigationService navigation, WorkspaceContext workspace, WorkbenchDialogs dialogs, IErrorHandler errors, ILogger<MainViewModel> logger, ResearchSelection researchSelection, AIInvestmentWorkbench.Infrastructure.Storage.AppPaths paths)
    {
        Navigation = navigation; Workspace = workspace; WorkspaceLabel = paths.IsDemo ? "DEMO · 虚构数据 / 非实时行情" : "本地优先 · 由你决策";
        NavigateCommand = new AsyncCommand(async parameter =>
        {
            Navigation.Navigate((string)parameter!);
            logger.LogInformation("Navigation changed to {Page}.", Navigation.Current!.Key);
            await Navigation.Current.LoadAsync();
        }, errors);
        RefreshCommand = new AsyncCommand(async _ =>
        {
            if (Navigation.Current is { } page) await page.LoadAsync();
        }, errors);
        NewAccountCommand = new AsyncCommand(async _ => { if (await dialogs.EditAccountAsync() is { } id) { await Workspace.ReloadAsync(id); if (Navigation.Current is { } page) await page.LoadAsync(); } }, errors);
        workspace.AccountChanged += (_, _) => RefreshCommand.Execute(null);
        researchSelection.NavigationRequested += key => NavigateCommand.Execute(key);
        var day = DateOnly.FromDateTime(DateTime.Today);
        _calendarTimer.Tick += (_, _) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (today == day) return;
            day = today; RefreshCommand.Execute(null);
        };
        _calendarTimer.Start();
    }
    public string WorkspaceLabel { get; }
    public NavigationService Navigation { get; }
    public WorkspaceContext Workspace { get; }
    public AsyncCommand NewAccountCommand { get; }
    public AsyncCommand NavigateCommand { get; }
    public AsyncCommand RefreshCommand { get; }
}

