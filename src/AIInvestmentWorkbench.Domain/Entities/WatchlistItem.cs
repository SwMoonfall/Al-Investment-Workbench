using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Enums;

namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class WatchlistItem : Entity
{
    private WatchlistItem() { }
    public WatchlistItem(Security security, string note = "")
    {
        ArgumentNullException.ThrowIfNull(security);
        Security = security; SecurityId = security.Id; UpdateNote(note);
    }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Note { get; private set; } = string.Empty;
    public string Notes => Note;
    public WatchlistStage Stage { get; private set; }
    public WatchlistPriority Priority { get; private set; } = WatchlistPriority.Normal;
    public string Reason { get; private set; } = "";
    public string NextAction { get; private set; } = "";
    public DateTimeOffset? LastResearchDate { get; private set; }
    public RiskStatus RiskStatus { get; private set; }
    public void Edit(WatchlistStage stage, WatchlistPriority priority, string reason, string nextAction,
        DateTimeOffset? lastResearchDate, RiskStatus riskStatus, string notes)
    {
        if (!Enum.IsDefined(stage) || !Enum.IsDefined(priority) || !Enum.IsDefined(riskStatus)) throw new BusinessException("无效的观察清单状态。");
        if (lastResearchDate > DateTimeOffset.UtcNow) throw new BusinessException("研究日期不能晚于当前时间。");
        Stage = stage; Priority = priority; Reason = Guard.OptionalText(reason); NextAction = Guard.OptionalText(nextAction);
        LastResearchDate = lastResearchDate?.ToUniversalTime(); RiskStatus = riskStatus; UpdateNote(notes);
    }
    public void UpdateNote(string note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.Length > 2000) throw new ArgumentException("备注最多 2000 字符。", nameof(note));
        Note = note.Trim(); MarkUpdated();
    }
}
