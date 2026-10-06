using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class SecurityRepository(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : ISecurityRepository
{
    public async Task<IReadOnlyList<Security>> SearchAsync(string query, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Securities.AsNoTracking().OrderBy(x => x.Symbol).ToListAsync(ct);
        return rows.Where(x => string.Join(" ", x.Symbol, x.Name, x.Exchange, x.Sector, x.Industry, x.ISIN).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public async Task<Security?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Securities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    }
    public async Task SaveAsync(Security security, bool isNew, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            if (await db.Securities.AnyAsync(x => x.Id != security.Id && x.Symbol == security.Symbol && x.Exchange == security.Exchange, ct))
                throw new BusinessException("同一交易所已存在该证券代码。");
            if (isNew) db.Securities.Add(security);
            else
            {
                var current = await db.Securities.SingleOrDefaultAsync(x => x.Id == security.Id, ct) ?? throw new BusinessException("证券已不存在。");
                if (current.Currency != security.Currency && (current.LatestPrice is not null || await db.ValuationModels.AnyAsync(x => x.SecurityId == current.Id, ct) || await db.Transactions.AnyAsync(x => x.SecurityId == current.Id, ct) || await db.Positions.AnyAsync(x => x.SecurityId == current.Id, ct)))
                    throw new BusinessException("已有报价或投资记录的证券不能更改币种。");
                current.Edit(security.Symbol, security.Name, security.Exchange, security.Currency, security.Type, security.Market, security.Sector, security.Industry, security.Country, security.ISIN, security.Notes);
            }
            return true;
        }, ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await writer.ExecuteAsync(async db =>
        {
            if (await db.InvestmentJournals.AnyAsync(x => x.SecurityId == id, ct) || await db.InvestmentReviews.AnyAsync(x => x.SecurityId == id, ct)) throw new BusinessException("此证券存在日志或复盘历史，不能删除。");
            if (await db.AIAnalyses.AnyAsync(x => x.SecurityId == id, ct) || await db.AIResearchReports.AnyAsync(x => x.SecurityId == id, ct)) throw new BusinessException("此证券存在 AI 审计记录或研究报告，不能删除。");
            if (await db.SecurityRiskTags.AnyAsync(x => x.SecurityId == id, ct) || await db.DecisionRecords.AnyAsync(x => x.SecurityId == id, ct)) throw new BusinessException("此证券有关联风险标签或决策历史，不能直接删除。");
            if (await db.ValuationModels.AnyAsync(x => x.SecurityId == id, ct)) throw new BusinessException("此证券有关联估值，请先删除估值模型。");
            if (await db.Theses.AnyAsync(x => x.SecurityId == id, ct)) throw new BusinessException("此证券有关联 Thesis 或历史版本，不能删除。");
            if (await db.CompanyResearches.AnyAsync(x => x.SecurityId == id, ct) || await db.FinancialMetrics.AnyAsync(x => x.SecurityId == id, ct)
                || await db.ResearchSources.AnyAsync(x => x.SecurityId == id, ct) || await db.ResearchScores.AnyAsync(x => x.SecurityId == id, ct))
                throw new BusinessException("此证券已有研究、财务、来源或评分资料，不能直接删除。");
            if (await db.Transactions.AnyAsync(x => x.SecurityId == id, ct) || await db.Positions.AnyAsync(x => x.SecurityId == id, ct) || await db.WatchlistItems.AnyAsync(x => x.SecurityId == id, ct))
                throw new BusinessException("此证券被交易、持仓或观察清单引用，无法删除。请先移除观察记录；投资历史不会被自动删除。");
            var entity = await db.Securities.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("证券已不存在。");
            db.Securities.Remove(entity); return true;
        }, ct);
    }
}

