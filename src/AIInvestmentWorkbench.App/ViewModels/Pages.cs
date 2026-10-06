using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Theme;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Infrastructure.Storage;
namespace AIInvestmentWorkbench.App.ViewModels;
public abstract class PageViewModel(string key, string title, string subtitle) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public virtual Task LoadAsync() => Task.CompletedTask;
}public sealed class SettingsViewModel : PageViewModel
{
    private string _status = "外观偏好保存在本机。";
    private AppTheme _selectedTheme;
    private readonly ISettingsService _settings;
    private readonly ThemeService _theme;
    public SettingsViewModel(ISettingsService settings, ThemeService theme, AppPaths paths, IErrorHandler errors, AISettingsViewModel ai, MaintenanceViewModel maintenance)
        : base("Settings", "设置", "调整工作台外观，了解本地数据的保存位置。")
    {
        _settings = settings; _theme = theme; AI = ai; Maintenance = maintenance;
        DatabasePath = paths.DatabasePath; LogsPath = paths.LogsDirectory;
        ChangeThemeCommand = new AsyncCommand(async parameter =>
        {
            var next = Enum.Parse<AppTheme>((string)parameter!);
            await _settings.SaveThemeAsync(next);
            _theme.Apply(next); SelectedTheme = next;
            Status = $"已保存{(next == AppTheme.Light ? "浅色" : "深色")}主题，下次启动自动恢复。";
        }, errors);
    }
    public AISettingsViewModel AI { get; }
    public MaintenanceViewModel Maintenance { get; }
    public override async Task LoadAsync() { await AI.LoadAsync(); await Maintenance.LoadAsync(); }
    public AppTheme SelectedTheme { get => _selectedTheme; private set => Set(ref _selectedTheme, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string DatabasePath { get; }
    public string LogsPath { get; }
    public AsyncCommand ChangeThemeCommand { get; }
    public async Task InitializeAsync()
    {
        SelectedTheme = await _settings.GetThemeAsync();
        _theme.Apply(SelectedTheme);
    }
}




