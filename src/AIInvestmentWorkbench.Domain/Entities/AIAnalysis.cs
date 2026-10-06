using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public enum AnalysisType { CompanyOverview, BusinessModelAnalysis, FinancialQuality, AccountingRisk, CompetitiveAdvantage, BullCase, BearCase, ThesisChallenge, ValuationAssumptionReview, QuarterlyReview, PortfolioRiskReview, JournalReview, GrowthDrivers, BaseCase, MonitoringKPI }
public enum AIAnalysisStatus { Running, Completed, InvalidOutput, Cancelled, TimedOut, Failed, Interrupted }
public sealed class PromptTemplate : Entity
{
    private PromptTemplate() { }
    public PromptTemplate(string name, AnalysisType type, string body, bool builtIn = false) { AnalysisType = type; IsBuiltIn = builtIn; Edit(name, body, 1); Revision = 1; }
    public string Name { get; private set; } = "";
    public AnalysisType AnalysisType { get; private set; }
    public bool IsBuiltIn { get; private set; }
    public string Body { get; private set; } = "";
    public int Revision { get; private set; } = 1;
    public void Edit(string name, string body, int revision)
    {
        if (Revision != revision) throw new BusinessException("模板已更新，请刷新后编辑。");
        if (!Enum.IsDefined(AnalysisType)) throw new BusinessException("未知分析类型。");
        Name = Guard.Text(name, "模板名称", 150); Body = Guard.Text(body, "提示词", 16000); Revision++; MarkUpdated();
    }
}
public sealed class AIAnalysis : Entity
{
    private AIAnalysis() { }
    public AIAnalysis(Guid? securityId, AnalysisType type, Guid templateId, string prompt, string model, string provider, string sources, string context, string settingsSnapshot, Guid? accountId = null)
    { PortfolioAccountId = accountId; SecurityId = securityId; AnalysisType = type; PromptTemplateId = templateId; PromptSnapshot = prompt; Model = model; Provider = provider; SourceReferences = sources; ContextSnapshot = context; SettingsSnapshot = settingsSnapshot; }
    public Guid? PortfolioAccountId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public Security? Security { get; private set; }
    public AnalysisType AnalysisType { get; private set; }
    public Guid PromptTemplateId { get; private set; }
    public PromptTemplate PromptTemplate { get; private set; } = null!;
    public string PromptSnapshot { get; private set; } = "";
    public string ContextSnapshot { get; private set; } = "";
    public string SettingsSnapshot { get; private set; } = "";
    public string Model { get; private set; } = "";
    public string Provider { get; private set; } = "";
    public string SourceReferences { get; private set; } = "";
    public string StructuredOutput { get; private set; } = "";
    public string RawOutput { get; private set; } = "";
    public string ResponseEnvelope { get; private set; } = "";
    public string ResponseModel { get; private set; } = "";
    public string ResponseId { get; private set; } = "";
    public AIAnalysisStatus Status { get; private set; } = AIAnalysisStatus.Running;
    public string ErrorMessage { get; private set; } = "";
    public DateTimeOffset? FinishedAt { get; private set; }
    public void Finish(AIAnalysisStatus status, string raw, string structured, string error, string envelope = "", string responseId = "", string responseModel = "")
    {
        if (Status != AIAnalysisStatus.Running || status == AIAnalysisStatus.Running) throw new BusinessException("分析已结束，不能覆盖结果。");
        Status = status; RawOutput = raw; StructuredOutput = structured; ErrorMessage = error; ResponseEnvelope = envelope; ResponseId = responseId; ResponseModel = responseModel; FinishedAt = DateTimeOffset.UtcNow; MarkUpdated();
    }
}
public sealed class AIResearchReport : Entity
{
    private AIResearchReport() { }
    public AIResearchReport(Guid securityId, string title) { SecurityId = Guard.Id(securityId, "证券"); Title = Guard.Text(title, "报告", 300); }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string SectionIdsJson { get; private set; } = "[]";
    public string ReportMarkdown { get; private set; } = "";
    public AIAnalysisStatus Status { get; private set; } = AIAnalysisStatus.Running;
    public int CompletedSections { get; private set; }
    public void Update(string ids, string report, int completed, AIAnalysisStatus status) { if (Status != AIAnalysisStatus.Running) throw new BusinessException("报告已结束。"); SectionIdsJson = ids; ReportMarkdown = report; CompletedSections = completed; Status = status; MarkUpdated(); }
}
