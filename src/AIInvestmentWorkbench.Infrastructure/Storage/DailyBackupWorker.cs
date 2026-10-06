using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace AIInvestmentWorkbench.Infrastructure.Storage;
public sealed class DailyBackupWorker(BackupService backup, ILogger<DailyBackupWorker> logger) : BackgroundService
{
    public string Status { get; private set; } = "等待首次自动备份。";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // App starts this service only after database initialization.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        do
        {
            try { await backup.CreateAsync(automatic: true, ct: stoppingToken); Status = "每日自动备份正常（保留最近 7 份）。"; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { Status = "自动备份失败，请检查磁盘空间、目录权限，并立即手动备份。"; logger.LogError(ex, "Daily backup failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
