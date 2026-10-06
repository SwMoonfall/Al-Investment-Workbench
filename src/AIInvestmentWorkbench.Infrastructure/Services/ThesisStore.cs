using System.Text.Json;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class ThesisStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IThesisReader, IThesisUserCommands
{
    private static IQueryable<Thesis> Query(InvestmentDbContext db) => db.Theses.Include(x => x.Security).Include(x => x.Assumptions).Include(x => x.KillConditions).AsSplitQuery();
    private static ThesisDetail Detail(Thesis t) => new(t.Id, t.SecurityId, $"{t.Security.Symbol} · {t.Security.Name}", t.Content, t.Status, t.CreatedDate, t.ReviewDate,
        t.LastReviewedAt, t.Version, t.Archived, t.IsCurrent, t.UpdatedAt,
        t.Assumptions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new AssumptionDto(x.Id, new(x.Description, x.Metric, x.Baseline, x.ExpectedValue, x.WarningThreshold, x.KillThreshold, x.CurrentValue, x.Unit, x.Direction, x.Evidence), x.Status, x.LastReviewedAt)).ToArray(),
        t.KillConditions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new KillDto(x.Id, new(x.Title, x.Description, x.Metric, x.WarningThreshold, x.TriggerThreshold, x.CurrentValue, x.Unit, x.Direction, x.Evidence), x.Status, x.CreatedAt, x.LastReviewedAt)).ToArray());
    public async Task<IReadOnlyList<ThesisDetail>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await Query(db).AsNoTracking().Where(x => x.SecurityId == securityId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.IsCurrent).ThenBy(x => x.Archived).ThenByDescending(x => x.CreatedDate).Select(Detail).ToList();
    }
    public async Task<IReadOnlyList<ThesisHistoryEntry>> HistoryAsync(Guid thesisId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var versions = await db.ThesisVersions.AsNoTracking().Where(x => x.ThesisId == thesisId).OrderByDescending(x => x.Version).ToListAsync(ct);
        return versions.Select(x => new ThesisHistoryEntry(x.Version, x.CreatedAt, x.ChangeReason, x.Actor,
            JsonSerializer.Deserialize<ThesisDetail>(x.SnapshotJson) ?? throw new BusinessException("无法读取历史快照。"))).ToList();
    }
    public async Task<IReadOnlyList<ThesisIndicator>> IndicatorsAsync(DateOnly today, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await Query(db).AsNoTracking().Where(x => !x.Archived).ToListAsync(ct);
        return rows.Select(Detail).Select(x => new ThesisIndicator(x.Id, x.SecurityId, x.SecurityName, x.Content.Title, x.Status.ToString(), x.IsCurrent, x.ReviewDate,
            ThesisRules.ReviewDue(x.ReviewDate, today, x.Status, x.Archived), x.Signal, x.ConditionNotice)).OrderByDescending(x => x.NeedsReview).ThenByDescending(x => x.IsCurrent).ThenBy(x => x.ReviewDate).ToList();
    }
    public async Task<Guid> CreateAsync(Guid securityId, ThesisContent content, DateOnly? reviewDate, ThesisActor actor, string reason = "用户创建 Thesis", CancellationToken ct = default)
    {
        ThesisRules.RequireUser(actor);
        return await writer.ExecuteAsync(async db =>
        {
            var security = await db.Securities.SingleOrDefaultAsync(x => x.Id == securityId, ct) ?? throw new BusinessException("证券已不存在。");
            var thesis = new Thesis(securityId, content, reviewDate, actor); db.Theses.Add(thesis);
            // Explicitly load navigation for the immutable, self-contained initial snapshot.
            db.Entry(thesis).Reference(x => x.Security).CurrentValue = security;
            Snapshot(db, thesis, reason, actor); return thesis.Id;
        }, ct);
    }
    private static void Snapshot(InvestmentDbContext db, Thesis thesis, string reason, ThesisActor actor)
        => db.ThesisVersions.Add(new(thesis.Id, thesis.Version, JsonSerializer.Serialize(Detail(thesis)), reason, actor));
    private async Task Change(Guid id, int version, string reason, ThesisActor actor, Func<InvestmentDbContext, Thesis, Task> mutation, CancellationToken ct)
    {
        ThesisRules.RequireUser(actor); reason = Guard.Text(reason, nameof(reason), 2000);
        await writer.ExecuteAsync(async db =>
        {
            var thesis = await Query(db).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("Thesis 已不存在。");
            if (thesis.Version != version) throw new BusinessException("Thesis 已被更新，请刷新后重新编辑。此次修改未保存。");
            await mutation(db, thesis); thesis.Reevaluate();
            if (thesis.IsCurrent && await db.Theses.AnyAsync(x => x.SecurityId == thesis.SecurityId && x.Id != thesis.Id && x.IsCurrent, ct))
                throw new BusinessException("该证券已有当前 Thesis。请先明确关闭或确认失效，再激活新的逻辑；不会自动替换。");
            thesis.AdvanceVersion(version); await db.SaveChangesAsync(ct); Snapshot(db, thesis, reason, actor); return true;
        }, ct);
    }
    public Task EditAsync(Guid id, int version, ThesisContent content, DateOnly? reviewDate, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "编辑正文：" + reason, actor, (_, t) => { t.Edit(content, reviewDate, actor); return Task.CompletedTask; }, ct);
    public Task SaveAssumptionAsync(Guid id, int version, Guid? assumptionId, AssumptionContent content, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "保存假设：" + reason, actor, (db, t) =>
        {
            t.EnsureEditable(actor);
            if (assumptionId is null) t.Assumptions.Add(new(t.Id, content, actor));
            else (t.Assumptions.SingleOrDefault(x => x.Id == assumptionId) ?? throw new BusinessException("假设已不存在或不属于此 Thesis。")).Edit(content, actor);
            return Task.CompletedTask;
        }, ct);
    public Task DeleteAssumptionAsync(Guid id, int version, Guid assumptionId, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "删除假设：" + reason, actor, (db, t) =>
        {
            t.EnsureEditable(actor); var item = t.Assumptions.SingleOrDefault(x => x.Id == assumptionId) ?? throw new BusinessException("假设已不存在。");
            t.Assumptions.Remove(item); db.ThesisAssumptions.Remove(item); return Task.CompletedTask;
        }, ct);
    public Task SaveKillAsync(Guid id, int version, Guid? conditionId, KillContent content, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "用户保存 Kill 条件：" + reason, actor, (db, t) =>
        {
            t.EnsureEditable(actor);
            if (conditionId is null) t.KillConditions.Add(new(t.Id, content, actor));
            else (t.KillConditions.SingleOrDefault(x => x.Id == conditionId) ?? throw new BusinessException("条件已不存在或不属于此 Thesis。")).Edit(content, actor);
            return Task.CompletedTask;
        }, ct);
    public Task DeleteKillAsync(Guid id, int version, Guid conditionId, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "用户删除 Kill 条件：" + reason, actor, (db, t) =>
        {
            t.EnsureEditable(actor); var item = t.KillConditions.SingleOrDefault(x => x.Id == conditionId) ?? throw new BusinessException("条件已不存在。");
            t.KillConditions.Remove(item); db.KillConditions.Remove(item); return Task.CompletedTask;
        }, ct);
    public Task ChangeStatusAsync(Guid id, int version, ThesisStatus status, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, $"用户确认状态 {status}：" + reason, actor, (_, t) => { t.ChangeStatus(status, actor); return Task.CompletedTask; }, ct);
    public Task CompleteReviewAsync(Guid id, int version, DateOnly nextReview, DateOnly today, string evidence, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "用户完成复盘：" + evidence, actor, (_, t) => { t.CompleteReview(nextReview, today, actor); return Task.CompletedTask; }, ct);
    public Task ArchiveAsync(Guid id, int version, string reason, ThesisActor actor, CancellationToken ct = default)
        => Change(id, version, "用户归档：" + reason, actor, (_, t) => { if (t.Archived) throw new BusinessException("此逻辑已经归档。"); t.Archive(actor); return Task.CompletedTask; }, ct);
}
