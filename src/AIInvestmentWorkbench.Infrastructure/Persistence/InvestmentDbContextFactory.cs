using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIInvestmentWorkbench.Infrastructure.Persistence;

public sealed class InvestmentDbContextFactory : IDesignTimeDbContextFactory<InvestmentDbContext>
{
    public InvestmentDbContext CreateDbContext(string[] args)
    {
        var paths = new AppPaths();
        paths.EnsureDirectories();
        return new InvestmentDbContext(new DbContextOptionsBuilder<InvestmentDbContext>().UseSqlite(ConnectionString(paths)).Options);
    }
    public static string ConnectionString(AppPaths paths) => new SqliteConnectionStringBuilder
    {
        DataSource = paths.DatabasePath,
        ForeignKeys = true,
        DefaultTimeout = 15
    }.ToString();
}
