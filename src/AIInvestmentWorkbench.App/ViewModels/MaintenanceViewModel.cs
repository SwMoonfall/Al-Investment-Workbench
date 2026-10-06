using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Win32;
namespace AIInvestmentWorkbench.App.ViewModels;
public sealed class MaintenanceViewModel : ObservableObject
{
    private readonly ISecurityRepository _securities; private readonly DailyBackupWorker _daily;
    private string _status = "备份只包含数据库；外部附件和加密密钥需另行保管。", _automatic = "";
    public MaintenanceViewModel(AppPaths paths, BackupService backup, DailyBackupWorker daily, DemoService demo, ExportService export, WorkspaceContext workspace, ISecurityRepository securities, IErrorHandler errors)
    {
        _securities = securities; _daily = daily; BackupsPath = paths.BackupsDirectory;
        About = $"AI Investment Workbench {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · Windows x64\n.NET {Environment.Version}\n{(paths.IsDemo ? "DEMO · 虚构数据 / 非实时行情" : "正式本地工作空间")}";
        DemoAllowed = !paths.IsDemo;
        BackupCommand = new(async _ => { Status = "正在创建一致性备份…"; var file = await backup.CreateAsync(); Status = "备份已验证并保存：" + file; }, errors);
        OpenBackupCommand = new(_ => { Directory.CreateDirectory(BackupsPath); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { BackupsPath }, UseShellExecute = true }); return Task.CompletedTask; }, errors);
        RestoreCommand = new(async _ =>
        {
            var picker = new OpenFileDialog { Filter = "SQLite 备份|*.db", InitialDirectory = BackupsPath, Title = "选择要恢复的备份" };
            if (picker.ShowDialog() != true) return;
            if (MessageBox.Show("恢复将替换当前工作空间数据库，并退出应用。下次启动先自动备份当前数据库，再验证、升级和恢复所选备份。\n\n请确认已保存正在编辑的内容。是否继续？", "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await backup.StageRestoreAsync(picker.FileName); MessageBox.Show("备份已验证，程序将关闭。请重新启动以完成恢复。", "恢复已准备"); System.Windows.Application.Current.Shutdown();
        }, errors);
        ExportCommand = new(async _ =>
        {
            var account = workspace.SelectedAccount?.Id ?? throw new BusinessException("请先选择账户。");
            var extension = Kind.ToString().EndsWith("Csv", StringComparison.Ordinal) ? "csv" : Kind == ExportKind.ResearchJson ? "json" : "md";
            var picker = new SaveFileDialog { Filter = $"Export|*.{extension}", FileName = $"{Kind}-{DateTime.Today:yyyyMMdd}.{extension}" };
            if (picker.ShowDialog() != true) return;
            Status = "正在导出…"; await export.ExportAsync(Kind, account, SelectedSecurity?.Id, picker.FileName); Status = "已导出：" + picker.FileName;
        }, errors);
        DemoCommand = new(async _ => { var root = await demo.PrepareAsync(); Launch(root); Status = "已打开独立 DEMO 窗口。"; }, errors);
        ResetDemoCommand = new(async _ => { if (Confirm("重置 Demo 会删除其编辑内容并重新生成示例，正式数据库不受影响。请先关闭 DEMO 窗口。")) { await demo.PrepareAsync(true); Status = "Demo 已重置；可点击打开。"; } }, errors);
        DeleteDemoCommand = new(async _ => { if (Confirm("删除独立 Demo 数据，操作前保存 Demo 数据库备份，正式数据库不受影响。请先关闭 DEMO 窗口。")) { await demo.DeleteAsync(); Status = "Demo 已删除。"; } }, errors);
    }
    private static bool Confirm(string message) => MessageBox.Show(message, "确认 Demo 操作", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private static void Launch(string root) => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AIInvestmentWorkbench.App.exe")) { ArgumentList = { "--data-root", root, "--demo" }, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    public string About { get; } public string BackupsPath { get; } public bool DemoAllowed { get; }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string AutomaticStatus { get => _automatic; private set => Set(ref _automatic, value); }
    public ExportKind[] Kinds { get; } = Enum.GetValues<ExportKind>(); public ExportKind Kind { get; set; }
    public ObservableCollection<Security> Securities { get; } = []; public Security? SelectedSecurity { get; set; }
    public AsyncCommand BackupCommand { get; } public AsyncCommand OpenBackupCommand { get; } public AsyncCommand RestoreCommand { get; } public AsyncCommand ExportCommand { get; } public AsyncCommand DemoCommand { get; } public AsyncCommand ResetDemoCommand { get; } public AsyncCommand DeleteDemoCommand { get; }
    public async Task LoadAsync() { AutomaticStatus = _daily.Status; var id = SelectedSecurity?.Id; var rows = await Task.Run(() => _securities.SearchAsync("")); Securities.Clear(); foreach (var s in rows) Securities.Add(s); SelectedSecurity = rows.FirstOrDefault(x => x.Id == id); Raise(nameof(SelectedSecurity)); }
}
