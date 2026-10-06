using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace AIInvestmentWorkbench.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IDbContextFactory<InvestmentDbContext> factory, AppPaths paths, ILogger<DatabaseInitializer> logger)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        paths.EnsureDirectories(); var backup = new BackupService(paths); var restore = File.Exists(backup.PendingPath);
        var existed = File.Exists(paths.DatabasePath); var source = restore ? backup.PendingPath : paths.DatabasePath;
        if (restore || existed) await DatabaseFiles.ValidateAsync(source, cancellationToken, allowEmpty: !restore);
        await using var inspection = await factory.CreateDbContextAsync(cancellationToken);
        var known = inspection.Database.GetMigrations().ToArray();
        var pending = known.Length;
        if (restore || existed) { await using var old = DatabaseFiles.Context(source); pending = (await old.Database.GetPendingMigrationsAsync(cancellationToken)).Count(); }
        if (restore || pending > 0)
        {
            var staged = Path.Combine(paths.DatabaseDirectory, $"upgrade-{Guid.NewGuid():N}.db");
            try
            {
                if (restore || existed) await DatabaseFiles.CopyAsync(source, staged, cancellationToken);
                await using (var next = DatabaseFiles.Context(staged)) await next.Database.MigrateAsync(cancellationToken);
                await DatabaseFiles.ValidateAsync(staged, cancellationToken);
                if (existed)
                {
                    var safety = Path.Combine(paths.BackupsDirectory, $"before-{(restore ? "restore" : "migration")}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
                    await DatabaseFiles.CopyAsync(paths.DatabasePath, safety, cancellationToken); await DatabaseFiles.ValidateAsync(safety, cancellationToken, allowEmpty: true);
                }
                await DatabaseFiles.ReplaceAsync(staged, paths.DatabasePath, cancellationToken);
                if (restore) File.Delete(backup.PendingPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Staged database preparation failed; original database retained.");
                throw new DatabasePreparationException(restore, ex);
            }
            finally { foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(staged + suffix)) File.Delete(staged + suffix); }
        }
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        logger.LogInformation("Database initialized; applied {MigrationCount} pending migrations. Restore={Restore}", pending, restore);
    }, cancellationToken);
}
public sealed class DatabaseCompatibilityException() : Exception("此数据库属于其他版本，无法直接打开。请使用对应版本，或通过经过验证的数据迁移流程导入。原数据库已保留。");
public sealed class DatabasePreparationException(bool restore, Exception inner) : Exception(restore ? "备份恢复未完成，当前数据库已保留。请检查磁盘空间与备份完整性；可在 Data 目录移走 restore.pending.db 后重新启动原数据库。" : "数据库升级未完成，原数据库已保留。请检查磁盘空间或使用上一版本，并查看日志。", inner);
