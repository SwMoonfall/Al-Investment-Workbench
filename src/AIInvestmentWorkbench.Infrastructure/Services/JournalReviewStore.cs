using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed partial class JournalReviewStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer,
    IPortfolioStore portfolio, IPortfolioRiskStore risks, IThesisReader theses, IValuationStore valuations, ValuationService valuationCalculator, IResearchStore research) : IJournalReviewReader, IJournalReviewCommands
{
    public async Task<IReadOnlyList<JournalRow>> JournalsAsync(Guid accountId, bool history = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var rows = await db.InvestmentJournals.AsNoTracking().Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct);
        var names = await db.Securities.ToDictionaryAsync(x => x.Id, x => x.Symbol + " " + x.Name, ct);
        return (history ? rows : rows.GroupBy(x => x.RootId).Select(g => g.MaxBy(x => x.Version)!)).OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Select(x => new JournalRow(x, x.SecurityId is { } id ? names[id] : "账户日志")).ToArray();
    }
    public async Task<IReadOnlyList<ReviewRow>> ReviewsAsync(Guid accountId, bool history = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var rows = await db.InvestmentReviews.AsNoTracking().Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct);
        var names = await db.Securities.ToDictionaryAsync(x => x.Id, x => x.Symbol + " " + x.Name, ct);
        return (history ? rows : rows.GroupBy(x => x.RootId).Select(g => g.MaxBy(x => x.Version)!)).OrderByDescending(x => x.CreatedAt).Select(x => new ReviewRow(x, x.SecurityId is { } id ? names[id] : "账户复盘")).ToArray();
    }
    public Task<Guid> SaveJournalAsync(JournalDraft d, ThesisActor actor, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        ThesisRules.RequireUser(actor); var root = (Guid?)null; var version = 1;
        if (d.PreviousId is { } oldId)
        {
            var old = await db.InvestmentJournals.SingleAsync(x => x.Id == oldId, ct);
            if (old.PortfolioAccountId != d.AccountId || old.SecurityId != d.SecurityId || old.DecisionRecordId != d.DecisionId || await db.InvestmentJournals.AnyAsync(x => x.RootId == old.RootId && x.Version > old.Version, ct)) throw new BusinessException("日志已更新或关联对象改变，请刷新；更正不能改变账户、证券或来源决策。");
            root = old.RootId; version = old.Version + 1;
        }
        if (d.DecisionId is { } decision)
        {
            if (!await db.DecisionRecords.AnyAsync(x => x.Id == decision && x.PortfolioAccountId == d.AccountId && x.SecurityId == d.SecurityId, ct)) throw new BusinessException("来源决策不属于此账户与证券。");
            if (root is null && await db.InvestmentJournals.AnyAsync(x => x.DecisionRecordId == decision, ct)) throw new BusinessException("此决策已生成日志，请在日志页查看或追加更正。");
        }
        var journal = new InvestmentJournal(d.AccountId, d.SecurityId, d.Content, actor, d.DecisionId, root, version, d.CorrectionReason); db.InvestmentJournals.Add(journal); return journal.Id;
    }, ct);
    public Task<Guid> SaveReviewAsync(ReviewDraft d, ThesisActor actor, CancellationToken ct = default) => writer.ExecuteAsync(async db =>
    {
        ThesisRules.RequireUser(actor); var e = d.Evidence; var root = (Guid?)null; var version = 1;
        if (e.SchemaVersion != 1 || e.CapturedAt > DateTimeOffset.UtcNow || JournalRules.Period(e.Kind, e.Start) != (e.Start, e.End)) throw new BusinessException("复盘快照期间无效。");
        if (d.Assumptions.Count != e.Assumptions.Count || d.Assumptions.Any(x => !e.Assumptions.Any(a => a.Id == x.Id && a.Description == x.Description))) throw new BusinessException("请逐项评估快照中的原始假设，不能遗漏或替换。");
        var same = await db.InvestmentReviews.Where(x => x.PortfolioAccountId == e.AccountId && x.SecurityId == e.SecurityId && x.Kind == e.Kind && x.PeriodStart == e.Start).ToListAsync(ct);
        if (d.PreviousId is { } oldId)
        {
            var old = same.SingleOrDefault(x => x.Id == oldId) ?? throw new BusinessException("原复盘不存在或对象改变。");
            if (same.Any(x => x.Version > old.Version)) throw new BusinessException("复盘已更新，请刷新后继续。"); root = old.RootId; version = old.Version + 1;
        }
        else if (same.Count > 0) throw new BusinessException("该期间已有复盘，请选中历史记录继续；不会覆盖旧版。");
        var record = new InvestmentReview(e.AccountId, e.SecurityId, e.Kind, e.Start, e.End, JsonSerializer.Serialize(e), d.ActualResults, d.Assumptions, d.Decision, d.Conclusion, d.Status, actor, DateOnly.FromDateTime(DateTime.Today), root, version);
        db.InvestmentReviews.Add(record); return record.Id;
    }, ct);
    public async Task<JournalDashboard> DashboardAsync(Guid accountId, DateOnly today, CancellationToken ct = default)
    {
        if (accountId == Guid.Empty) return new([], [], [], []);
        var period = JournalRules.LastCompletedQuarter(today); await using var db = await factory.CreateDbContextAsync(ct);
        var transactions = await db.Transactions.AsNoTracking().Where(x => x.PortfolioAccountId == accountId && x.SecurityId != null).ToListAsync(ct);
        var eligible = transactions.GroupBy(x => x.SecurityId!.Value).Where(g => g.Any(x => LocalDate(x.OccurredAt) >= period.Start && LocalDate(x.OccurredAt) <= period.End)
            || g.Where(x => LocalDate(x.OccurredAt) < period.Start).Sum(x => x.Type == TransactionType.Buy ? x.Quantity : x.Type == TransactionType.Sell ? -x.Quantity : 0) > 0).Select(g => g.Key).ToHashSet();
        var openTheses = await db.Theses.AsNoTracking().Where(x => !x.Archived && x.Status != ThesisStatus.Closed && x.Status != ThesisStatus.Invalidated).ToListAsync(ct);
        foreach (var t in openTheses.Where(x => LocalDate(x.CreatedDate) <= period.End)) eligible.Add(t.SecurityId);
        var reviews = (await ReviewsAsync(accountId, ct: ct)).Where(x => x.Review.Kind == ReviewKind.Quarterly && x.Review.PeriodEnd == period.End).ToArray();
        var names = await db.Securities.ToDictionaryAsync(x => x.Id, x => x.Symbol + " " + x.Name, ct);
        var due = eligible.Where(id => JournalRules.QuarterlyDue(today, period.End, reviews.SingleOrDefault(x => x.Review.SecurityId == id)?.Review.Status)).Select(id => new QuarterlyDueItem(id, names[id], period.Start, period.End)).ToArray();
        var ai = await db.AIAnalyses.AsNoTracking().Where(x => x.PortfolioAccountId == accountId && (x.AnalysisType == AnalysisType.JournalReview || x.AnalysisType == AnalysisType.QuarterlyReview || x.AnalysisType == AnalysisType.PortfolioRiskReview)).ToListAsync(ct);
        return new(due, openTheses.Where(x => x.Status == ThesisStatus.Warning).Select(x => $"{names[x.SecurityId]} · {x.Title} · v{x.Version} · Warning").ToArray(), (await JournalsAsync(accountId, ct: ct)).Take(5).ToArray(), ai.OrderByDescending(x => x.CreatedAt).Take(5).ToArray());
    }
    private static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToLocalTime().DateTime);
    private static string Clip(string text, int size = 2000) => text.Length <= size ? text : text[..size] + " […]";
}

