using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Persistence;

public sealed class DatabaseWriter(IDbContextFactory<InvestmentDbContext> factory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<T> ExecuteAsync<T>(Func<InvestmentDbContext, Task<T>> action, CancellationToken ct = default, bool validateOnly = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action(db);
            await db.SaveChangesAsync(ct);
            if (validateOnly) await transaction.RollbackAsync(ct); else await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException) { throw new BusinessException("数据保存失败：存在重复代码或被引用的记录。请刷新后检查；本次操作未写入。"); }
        finally { _gate.Release(); }
    }
}
