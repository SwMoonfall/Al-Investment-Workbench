using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class ThesisVersion : Entity
{
    private ThesisVersion() { }
    public ThesisVersion(Guid thesisId, int version, string snapshotJson, string reason, ThesisActor actor)
    {
        ThesisRules.RequireUser(actor); ThesisId = Guard.Id(thesisId, nameof(thesisId));
        if (version < 1) throw new BusinessException("版本号无效。");
        Version = version; SnapshotJson = Guard.Text(snapshotJson, nameof(snapshotJson), 10000000); ChangeReason = Guard.Text(reason, nameof(reason), 2000); Actor = actor;
    }
    public Guid ThesisId { get; private set; }
    public Thesis Thesis { get; private set; } = null!;
    public int Version { get; private set; }
    public string SnapshotJson { get; private set; } = "";
    public string ChangeReason { get; private set; } = "";
    public ThesisActor Actor { get; private set; }
}
