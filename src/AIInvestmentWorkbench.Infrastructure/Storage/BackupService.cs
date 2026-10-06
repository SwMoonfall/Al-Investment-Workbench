using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
namespace AIInvestmentWorkbench.Infrastructure.Storage;

public static class DatabaseFiles
{
    public static InvestmentDbContext Context(string path) => new(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(Connection(path)).Options);
    public static string Connection(string path, bool readOnly = false) => new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 15 }.ToString();
    public static async Task ValidateAsync(string path, CancellationToken ct = default, bool allowEmpty = false)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到备份文件。", path);
        await using var db = Context(path);
        await using var c = new SqliteConnection(Connection(path, true)); await c.OpenAsync(ct);
        await using var command = c.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(await command.ExecuteScalarAsync(ct) as string, "ok", StringComparison.Ordinal)) throw new BusinessException("数据库完整性检查失败；当前数据未替换。");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) == 0) { if (allowEmpty) return; throw new BusinessException("备份没有工作台数据，不能恢复。"); }
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='__EFMigrationsHistory'";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) == 0) throw new DatabaseCompatibilityException();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        var applied = new List<string>(); await using (var reader = await command.ExecuteReaderAsync(ct)) while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));
        var known = db.Database.GetMigrations().ToArray();
        if (applied.Count == 0 || !applied.SequenceEqual(known.Take(applied.Count)) || applied.Count > known.Length) throw new DatabaseCompatibilityException();
        command.CommandText = "PRAGMA foreign_key_check"; await using var fk = await command.ExecuteReaderAsync(ct);
        if (await fk.ReadAsync(ct)) throw new BusinessException("数据库包含不完整的关联记录，不能恢复。");
    }
    public static async Task CopyAsync(string source, string target, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var from = new SqliteConnection(Connection(source, true)); await from.OpenAsync(ct);
        await using var to = new SqliteConnection(Connection(target)); await to.OpenAsync(ct);
        from.BackupDatabase(to); ct.ThrowIfCancellationRequested();
        await using var standalone = to.CreateCommand(); standalone.CommandText = "PRAGMA journal_mode=DELETE"; await standalone.ExecuteNonQueryAsync(ct);
    }
    public static async Task CheckpointAsync(string path, CancellationToken ct = default)
    {
        await using var c = new SqliteConnection(Connection(path)); await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        await using var r = await cmd.ExecuteReaderAsync(ct); if (await r.ReadAsync(ct) && r.GetInt32(0) != 0) throw new BusinessException("数据库仍在使用，无法安全替换。请关闭其他窗口后重试。");
    }
    // Called only during startup before opening repositories, under the workspace process mutex.
    public static async Task ReplaceAsync(string staged, string destination, CancellationToken ct = default)
    {
        await CheckpointAsync(staged, ct); await ValidateAsync(staged, ct); SqliteConnection.ClearAllPools();
        if (File.Exists(destination))
        {
            await CheckpointAsync(destination, ct);
            File.Replace(staged, destination, null);
        }
        else File.Move(staged, destination);
    }
}

public sealed class BackupService(AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string PendingPath => Path.Combine(paths.DatabaseDirectory, "restore.pending.db");
    public async Task<string> CreateAsync(bool automatic = false, DateOnly? day = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await Task.Run(async () =>
            {
                paths.EnsureDirectories(); var today = day ?? DateOnly.FromDateTime(DateTime.Today);
                var target = Path.Combine(paths.BackupsDirectory, automatic ? $"daily-{today:yyyy-MM-dd}.db" : $"manual-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
                if (automatic && File.Exists(target)) { await DatabaseFiles.ValidateAsync(target, ct); return target; }
                var temp = target + ".partial";
                try { await DatabaseFiles.CopyAsync(paths.DatabasePath, temp, ct); await DatabaseFiles.ValidateAsync(temp, ct); File.Move(temp, target); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                if (automatic) foreach (var old in Directory.GetFiles(paths.BackupsDirectory, "daily-????-??-??.db").OrderDescending(StringComparer.Ordinal).Skip(7)) File.Delete(old);
                return target;
            }, ct);
        }
        finally { _gate.Release(); }
    }
    public async Task StageRestoreAsync(string backup, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(async () =>
            {
                if (Path.GetFullPath(backup).Equals(paths.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new BusinessException("请选择备份文件，而不是当前数据库。");
                await DatabaseFiles.ValidateAsync(backup, ct);
                var temp = PendingPath + ".partial";
                try { await DatabaseFiles.CopyAsync(backup, temp, ct); await DatabaseFiles.ValidateAsync(temp, ct); File.Move(temp, PendingPath, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }, ct);
        }
        finally { _gate.Release(); }
    }
}

