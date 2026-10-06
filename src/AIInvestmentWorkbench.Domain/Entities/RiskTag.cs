using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class RiskTag : Entity
{
    private RiskTag() { }
    public RiskTag(string name, RiskTagCategory category, bool builtIn = false)
    {
        if (!Enum.IsDefined(category)) throw new BusinessException("未知标签分类。");
        Name = Guard.Text(name, "标签", 100); NormalizedName = PortfolioRiskRules.Normalize(Name); Category = category; IsBuiltIn = builtIn;
    }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public RiskTagCategory Category { get; private set; }
    public bool IsBuiltIn { get; private set; }
}
public sealed class SecurityRiskTag : Entity
{
    private SecurityRiskTag() { }
    public SecurityRiskTag(Guid securityId, Guid riskTagId) { SecurityId = Guard.Id(securityId, "证券"); RiskTagId = Guard.Id(riskTagId, "标签"); }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public Guid RiskTagId { get; private set; }
    public RiskTag RiskTag { get; private set; } = null!;
}
