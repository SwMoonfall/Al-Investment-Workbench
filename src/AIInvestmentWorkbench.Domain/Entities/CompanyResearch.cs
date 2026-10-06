using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed record ResearchContent(string Overview = "", string BusinessModel = "", string Industry = "",
    string Competition = "", string Management = "", string GrowthDrivers = "", string Catalysts = "",
    string Risks = "", string AccountingNotes = "", string UserNotes = "");

public sealed class CompanyResearch : Entity
{
    private CompanyResearch() { }
    public CompanyResearch(Guid securityId) => SecurityId = Guard.Id(securityId, nameof(securityId));
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public int Revision { get; private set; }
    public string Overview { get; private set; } = "";
    public string BusinessModel { get; private set; } = "";
    public string Industry { get; private set; } = "";
    public string Competition { get; private set; } = "";
    public string Management { get; private set; } = "";
    public string GrowthDrivers { get; private set; } = "";
    public string Catalysts { get; private set; } = "";
    public string Risks { get; private set; } = "";
    public string AccountingNotes { get; private set; } = "";
    public string UserNotes { get; private set; } = "";
    public ResearchContent Content => new(Overview, BusinessModel, Industry, Competition, Management, GrowthDrivers, Catalysts, Risks, AccountingNotes, UserNotes);
    public void Update(ResearchContent content, int expectedRevision)
    {
        if (Revision != expectedRevision) throw new BusinessException("研究内容已更新，请重新打开编辑器，避免覆盖已保存的内容。");
        var values = new[] { content.Overview, content.BusinessModel, content.Industry, content.Competition, content.Management,
            content.GrowthDrivers, content.Catalysts, content.Risks, content.AccountingNotes, content.UserNotes }.Select(x => Guard.OptionalText(x, 100000)).ToArray();
        Overview = values[0]; BusinessModel = values[1]; Industry = values[2]; Competition = values[3]; Management = values[4];
        GrowthDrivers = values[5]; Catalysts = values[6]; Risks = values[7]; AccountingNotes = values[8]; UserNotes = values[9];
        Revision++; MarkUpdated();
    }
}
