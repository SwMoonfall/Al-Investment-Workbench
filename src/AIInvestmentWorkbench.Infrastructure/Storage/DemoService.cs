using System.Security.Cryptography;
using System.Text;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace AIInvestmentWorkbench.Infrastructure.Storage;
public sealed class DemoService(AppPaths paths)
{
    public string Root => Path.Combine(paths.Root, "DemoWorkspace");
    public static string LockName(string root) => @"Local\AIInvestmentWorkbench-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..24];
    public Task<string> PrepareAsync(bool reset = false) => Task.Run(() =>
    {
        if (paths.IsDemo) throw new BusinessException("请返回正式工作空间管理 Demo。");
        using var guard = new Mutex(true, LockName(Root), out var owned);
        if (!owned) throw new BusinessException("请先关闭 DEMO 窗口，再重置或打开示例。");
        try
        {
            if (reset) DeleteCore();
            if (Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any() && !File.Exists(Path.Combine(Root, ".demo-workspace"))) throw new BusinessException("此目录不是已识别的 DEMO，操作已取消。");
            var demo = new AppPaths(Root, true); demo.EnsureDirectories();
            if (Directory.GetFiles(Root).Any() && !File.Exists(Path.Combine(Root, ".demo-workspace"))) throw new BusinessException("此目录不是已识别的 DEMO，操作已取消。");
            File.WriteAllText(Path.Combine(Root, ".demo-workspace"), "AIInvestmentWorkbench DEMO v1");
            var factory = new Factory(demo); new DatabaseInitializer(factory, demo, NullLogger<DatabaseInitializer>.Instance).InitializeAsync().GetAwaiter().GetResult();
            var store = new PortfolioStore(factory, new DatabaseWriter(factory));
            if (store.AccountsAsync().GetAwaiter().GetResult().Count == 0)
            {
                var account = store.SaveAccountAsync(new(null, "DEMO · 虚构示例组合", "CNY", 500000, .1m, .4m, .4m, .1m, .15m)).GetAwaiter().GetResult();
                using var db = factory.CreateDbContext();
                var items = new[] { new Security("DEMO-A", "DEMO 虚构科技", "DEMO", "CNY", SecurityType.Stock), new Security("DEMO-B", "DEMO 虚构消费", "DEMO", "CNY", SecurityType.Stock), new Security("DEMO-ETF", "DEMO 虚构指数", "DEMO", "CNY", SecurityType.Etf) };
                foreach (var s in items) { s.Edit(s.Symbol, s.Name, "DEMO", "CNY", s.Type, Market.Other, s.Type == SecurityType.Etf ? "示例指数" : "示例行业", "虚构", "DEMO", null, "DEMO：完全虚构，非实时行情，不能用于投资。" ); s.SetPrice(12, DateTimeOffset.UtcNow.AddDays(-1)); }
                db.Securities.AddRange(items); db.SaveChanges();
                foreach (var s in items)
                {
                    store.PostAsync(new(account, s.Id, DateTimeOffset.UtcNow.AddDays(-30), TransactionType.Buy, 1000, 10, 0, 5, "CNY", "DEMO 虚构成交")).GetAwaiter().GetResult();
                    store.SaveWatchlistAsync(new(s.Id, WatchlistStage.Researching, WatchlistPriority.Normal, "DEMO 演练", "学习研究流程", null, RiskStatus.Unknown, "虚构资料")).GetAwaiter().GetResult();
                }
            }
            return Root;
        }
        finally { guard.ReleaseMutex(); }
    });
    public Task DeleteAsync() => Task.Run(() =>
    {
        if (paths.IsDemo) throw new BusinessException("请返回正式工作空间管理 Demo。");
        using var guard = new Mutex(true, LockName(Root), out var owned);
        if (!owned) throw new BusinessException("请先关闭 DEMO 窗口。");
        try { DeleteCore(); } finally { guard.ReleaseMutex(); }
    });
    private void DeleteCore()
    {
        if (!Directory.Exists(Root)) return;
        if (!File.Exists(Path.Combine(Root, ".demo-workspace")) || !Path.GetFullPath(Root).StartsWith(paths.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories).Any(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)) throw new BusinessException("Demo 目录标记或路径不符合要求，未删除任何数据。");
        var db = Path.Combine(Root, "Data", "investment.db"); paths.EnsureDirectories();
        if (File.Exists(db)) DatabaseFiles.CopyAsync(db, Path.Combine(paths.BackupsDirectory, $"before-demo-delete-{Guid.NewGuid():N}.db")).GetAwaiter().GetResult();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Root, true);
    }
    private sealed class Factory(AppPaths p) : IDbContextFactory<InvestmentDbContext> { public InvestmentDbContext CreateDbContext() => DatabaseFiles.Context(p.DatabasePath); }
}


