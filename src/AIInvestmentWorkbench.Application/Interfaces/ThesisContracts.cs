using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record AssumptionDto(Guid Id, AssumptionContent Content, AssumptionStatus Status, DateTimeOffset? LastReviewedAt);
public sealed record KillDto(Guid Id, KillContent Content, KillConditionStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? LastReviewedAt)
{
    public string Assessment => Content.CurrentValue.HasValue ? Status.ToString() : "未评估";
}
public sealed record ThesisDetail(Guid Id, Guid SecurityId, string SecurityName, ThesisContent Content, ThesisStatus Status,
    DateTimeOffset CreatedDate, DateOnly? ReviewDate, DateTimeOffset? LastReviewedAt, int Version, bool Archived, bool IsCurrent,
    DateTimeOffset UpdatedAt, IReadOnlyList<AssumptionDto> Assumptions, IReadOnlyList<KillDto> KillConditions)
{
    public string Label => $"{Content.Title} · v{Version} · {Status}" + (IsCurrent ? " · 当前" : "") + (Archived ? " · 已归档" : "");
    public int TriggeredCount => KillConditions.Count(x => x.Status == KillConditionStatus.Triggered);
    public int WarningCount => KillConditions.Count(x => x.Status == KillConditionStatus.Warning);
    public int UnknownCount => KillConditions.Count(x => !x.Content.CurrentValue.HasValue);
    public string Signal => Status == ThesisStatus.Invalidated || TriggeredCount > 0 ? "Red" : Status == ThesisStatus.Warning || WarningCount > 0 ? "Amber"
        : Status == ThesisStatus.Active && KillConditions.Count > 0 && UnknownCount == 0 ? "Green" : "Neutral";
    public string ConditionNotice => KillConditions.Count == 0 ? "尚无 Kill 条件，无法据此判断逻辑安全。"
        : TriggeredCount > 0 ? $"存在触发条件：{TriggeredCount} 项。系统仅提示，是否 Invalidated 由你确认。"
        : WarningCount > 0 ? $"有 {WarningCount} 项预警条件，请核实证据。"
        : UnknownCount > 0 ? $"{UnknownCount} 项尚无当前值，未评估。" : "已录入条件当前均未触发；不代表投资无风险。";
}
public sealed record ThesisHistoryEntry(int Version, DateTimeOffset ChangedAt, string ChangeReason, ThesisActor Actor, ThesisDetail Snapshot);
public sealed record ThesisIndicator(Guid Id, Guid SecurityId, string Security, string Title, string Status, bool IsCurrent, DateOnly? ReviewDate, bool NeedsReview, string Signal, string Notice)
{
    public string StatusLabel => Status + (IsCurrent ? " · 当前" : "");
    public string ReviewLabel => Status is "Invalidated" or "Closed" ? "已结束" : NeedsReview ? $"需要复盘 · {ReviewDate:yyyy-MM-dd}" : ReviewDate.HasValue ? $"复盘 {ReviewDate:yyyy-MM-dd}" : "未安排复盘";
}
public interface IThesisReader
{
    Task<IReadOnlyList<ThesisDetail>> ListAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<ThesisHistoryEntry>> HistoryAsync(Guid thesisId, CancellationToken ct = default);
    Task<IReadOnlyList<ThesisIndicator>> IndicatorsAsync(DateOnly today, CancellationToken ct = default);
}
// Keep mutation capability separate from reads. The AI module must receive readers/suggestion DTOs only.
public interface IThesisUserCommands
{
    Task<Guid> CreateAsync(Guid securityId, ThesisContent content, DateOnly? reviewDate, ThesisActor actor, string reason = "用户创建 Thesis", CancellationToken ct = default);
    Task EditAsync(Guid id, int version, ThesisContent content, DateOnly? reviewDate, string reason, ThesisActor actor, CancellationToken ct = default);
    Task SaveAssumptionAsync(Guid id, int version, Guid? assumptionId, AssumptionContent content, string reason, ThesisActor actor, CancellationToken ct = default);
    Task DeleteAssumptionAsync(Guid id, int version, Guid assumptionId, string reason, ThesisActor actor, CancellationToken ct = default);
    Task SaveKillAsync(Guid id, int version, Guid? conditionId, KillContent content, string reason, ThesisActor actor, CancellationToken ct = default);
    Task DeleteKillAsync(Guid id, int version, Guid conditionId, string reason, ThesisActor actor, CancellationToken ct = default);
    Task ChangeStatusAsync(Guid id, int version, ThesisStatus status, string reason, ThesisActor actor, CancellationToken ct = default);
    Task CompleteReviewAsync(Guid id, int version, DateOnly nextReview, DateOnly today, string evidence, ThesisActor actor, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, int version, string reason, ThesisActor actor, CancellationToken ct = default);
}
