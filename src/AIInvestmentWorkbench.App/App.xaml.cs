using System.IO;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.AI;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Navigation;
using AIInvestmentWorkbench.App.Theme;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Infrastructure.Logging;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIInvestmentWorkbench.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private FileLoggerProvider? _fileLog;
    private bool _smoke;
    private ILogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += OnUnobservedException;
        try
        {
            var startupWatch = System.Diagnostics.Stopwatch.StartNew();
            _smoke = e.Args.Contains("--smoke-test") || e.Args.Contains("--performance-test");
            var dataIndex = Array.IndexOf(e.Args, "--data-root");
            var dataRoot = dataIndex >= 0 && dataIndex + 1 < e.Args.Length ? e.Args[dataIndex + 1] : null;
            if (_smoke && dataRoot is null) throw new ArgumentException("Smoke test requires an isolated --data-root.");
            var paths = new AppPaths(dataRoot, e.Args.Contains("--demo"));
            var lockName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant())))[..24];
            _instanceMutex = new Mutex(true, @"Local\AIInvestmentWorkbench-" + lockName, out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("此数据目录的工作台已在运行，请使用已打开的窗口。", "AI 投资工作台");
                Shutdown(0); return;
            }
            paths.EnsureDirectories();
            _fileLog = new FileLoggerProvider(paths.LogsDirectory);
            _logger = _fileLog.CreateLogger("Startup");
            _logger.LogInformation("Application starting. Phase 8.");
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(_fileLog);
            // Avoid provider SQL/parameter logs; application logs database lifecycle explicitly.
            builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
            builder.Services.AddSingleton(paths);
            builder.Services.AddDbContextFactory<InvestmentDbContext>(options => options.UseSqlite(InvestmentDbContextFactory.ConnectionString(paths)));
            builder.Services.AddSingleton<DatabaseInitializer>();
            builder.Services.AddSingleton<BackupService>();
            builder.Services.AddSingleton<DailyBackupWorker>();
            builder.Services.AddSingleton<DemoService>();
            builder.Services.AddSingleton<ExportService>();
            builder.Services.AddSingleton<MaintenanceViewModel>();
            builder.Services.AddSingleton<DatabaseWriter>();
            builder.Services.AddSingleton<ISecurityRepository, SecurityRepository>();
            builder.Services.AddSingleton<IPortfolioStore, PortfolioStore>();
            builder.Services.AddSingleton<SecurityApplicationService>();
            builder.Services.AddSingleton<PortfolioApplicationService>();
            builder.Services.AddSingleton<CsvImportService>();
            builder.Services.AddSingleton<IResearchStore, ResearchStore>();
            builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
            builder.Services.AddSingleton<IResearchFileReader, ResearchFileReader>();
            builder.Services.AddSingleton<FinancialCsvService>();
            builder.Services.AddSingleton<ResearchSelection>();
            builder.Services.AddSingleton<ResearchDialogs>();
            builder.Services.AddSingleton<ThesisStore>();
            builder.Services.AddSingleton<IThesisReader>(sp => sp.GetRequiredService<ThesisStore>());
            builder.Services.AddSingleton<IThesisUserCommands>(sp => sp.GetRequiredService<ThesisStore>());
            builder.Services.AddSingleton<ThesisDialogs>();
            builder.Services.AddSingleton<IValuationStore, ValuationStore>();
            builder.Services.AddSingleton<IPortfolioRiskStore, PortfolioRiskStore>();
            builder.Services.AddSingleton<PortfolioRiskDialogs>();
            builder.Services.AddSingleton<PageViewModel, RiskViewModel>();
            builder.Services.AddSingleton<ValuationService>();
            builder.Services.AddSingleton<ValuationDialogs>();
            builder.Services.AddSingleton<PageViewModel, ValuationViewModel>();
            builder.Services.AddSingleton<PageViewModel, ThesisViewModel>();
            builder.Services.AddSingleton<PageViewModel, ResearchViewModel>();
            builder.Services.AddSingleton<PageViewModel, FinancialViewModel>();
            builder.Services.AddSingleton<PageViewModel, SearchViewModel>();
            builder.Services.AddSingleton<WorkspaceContext>();
            builder.Services.AddSingleton<WorkbenchDialogs>();
            builder.Services.AddSingleton<ISettingsService, SettingsService>();
            builder.Services.AddSingleton<IWorkbenchReader, WorkbenchReader>();
            builder.Services.AddSingleton<JournalDialogs>();
            builder.Services.AddSingleton<PageViewModel, JournalViewModel>();
            builder.Services.AddSingleton<JournalReviewStore>();
            builder.Services.AddSingleton<IJournalReviewReader>(sp => sp.GetRequiredService<JournalReviewStore>());
            builder.Services.AddSingleton<IJournalReviewCommands>(sp => sp.GetRequiredService<JournalReviewStore>());
            builder.Services.AddSingleton<IAIStore, AIStore>();
            builder.Services.AddSingleton<ISecretStore, WindowsSecretStore>();
            if (_smoke) builder.Services.AddSingleton<IAIProvider, Diagnostics.SmokeAIProvider>();
            else builder.Services.AddSingleton<IAIProvider, OpenAIProvider>();
            builder.Services.AddSingleton<IAIContextBuilder, AIContextBuilder>();
            builder.Services.AddSingleton<AIAnalysisService>();
            builder.Services.AddSingleton<AISettingsViewModel>();
            builder.Services.AddSingleton<PageViewModel, AIResearchViewModel>();
            builder.Services.AddSingleton<IErrorHandler, ErrorHandler>();
            builder.Services.AddSingleton<ThemeService>();
            builder.Services.AddSingleton<PageViewModel, DashboardViewModel>();
            builder.Services.AddSingleton<PageViewModel, PortfolioViewModel>();
            builder.Services.AddSingleton<PageViewModel, WatchlistViewModel>();
            builder.Services.AddSingleton<PageViewModel, SecuritiesViewModel>();
            builder.Services.AddSingleton<PageViewModel, ImportViewModel>();
            builder.Services.AddSingleton<SettingsViewModel>();
            builder.Services.AddSingleton<PageViewModel>(sp => sp.GetRequiredService<SettingsViewModel>());
            builder.Services.AddSingleton<NavigationService>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainWindow>();
            _host = builder.Build();
            await _host.StartAsync();
            await _host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            await _host.Services.GetRequiredService<IPortfolioRiskStore>().InitializeDefaultsAsync();
            await _host.Services.GetRequiredService<IAIStore>().InitializeAsync();
            await _host.Services.GetRequiredService<SettingsViewModel>().InitializeAsync();
            await _host.Services.GetRequiredService<WorkspaceContext>().ReloadAsync();
            var main = _host.Services.GetRequiredService<MainViewModel>();
            await main.NavigateCommand.ExecuteAsync("Dashboard");
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (paths.IsDemo) MainWindow.Title = "DEMO · 虚构数据 / 非实时行情 · AI Investment Workbench";
            MainWindow.Show();
            await _host.Services.GetRequiredService<DailyBackupWorker>().StartAsync(CancellationToken.None);
            _logger.LogInformation("Main window shown in {ElapsedMs} ms.", startupWatch.ElapsedMilliseconds);
            if (_smoke) await File.WriteAllTextAsync(Path.Combine(paths.Root, "startup-ms.txt"), startupWatch.ElapsedMilliseconds.ToString());
            var workspace = _host.Services.GetRequiredService<WorkspaceContext>();
            if (!_smoke && workspace.Accounts.Count == 0)
            {
                var created = await _host.Services.GetRequiredService<WorkbenchDialogs>().EditAccountAsync(initial: true);
                await workspace.ReloadAsync(created);
                await main.Navigation.Current!.LoadAsync();
            }
            if (_smoke)
            {
                if (e.Args.Contains("--performance-test")) await Diagnostics.PerformanceScenario.RunAsync(_host.Services, MainWindow, paths);
                else await Diagnostics.SmokeTest.RunAsync(_host.Services, MainWindow, paths);
                Shutdown(0);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogCritical(exception, "Startup or smoke validation failed.");
            if (!_smoke) MessageBox.Show(exception is DatabaseCompatibilityException or DatabasePreparationException ? exception.Message : "工作台无法启动。请检查本地数据目录是否可写、磁盘空间是否充足，并查看 Logs 目录。原有数据不会被自动删除。", "AI 投资工作台", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _logger?.LogCritical(e.Exception, "Unhandled UI exception; closing to protect application state.");
        if (!_smoke) MessageBox.Show("工作台遇到意外错误，需要关闭。请重新打开；如再次发生，请查看本地日志。", "AI 投资工作台", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
        => _logger?.LogCritical(e.ExceptionObject as Exception, "Fatal background exception; process terminating.");
    private void OnUnobservedException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved background task failure.");
        e.SetObserved();
        if (!_smoke) Dispatcher.BeginInvoke(() => MessageBox.Show("后台任务未能完成，请重试并查看本地日志。", "AI 投资工作台", MessageBoxButton.OK, MessageBoxImage.Warning));
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application exiting with code {ExitCode}.", e.ApplicationExitCode);
        try { _host?.Services.GetRequiredService<DailyBackupWorker>().StopAsync(CancellationToken.None).GetAwaiter().GetResult(); _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
        catch (Exception error) { _logger?.LogError(error, "Host shutdown failed."); }
        finally
        {
            _host?.Dispose(); _fileLog?.Dispose();
            if (_ownsMutex) _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        base.OnExit(e);
    }
}








