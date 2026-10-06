using System.Text.Json;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class DecisionRecord : Entity
{
    private DecisionRecord() { }
    public DecisionRecord(Guid accountId, Guid securityId, DecisionKind kind, DecisionChoice choice, IReadOnlyList<DecisionAnswer> answers, string reason, string contextJson, SizingInputs? sizing)
    {
        DecisionRules.Validate(kind, choice, answers, reason); PortfolioAccountId = Guard.Id(accountId, "账户"); SecurityId = Guard.Id(securityId, "证券");
        Kind = kind; Choice = choice; Reason = reason.Trim(); AnswersJson = JsonSerializer.Serialize(answers);
        ContextJson = Guard.Text(contextJson, "决策上下文", 2000000); SizingJson = sizing is null ? null : JsonSerializer.Serialize(sizing);
    }
    public Guid PortfolioAccountId { get; private set; }
    public PortfolioAccount PortfolioAccount { get; private set; } = null!;
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public DecisionKind Kind { get; private set; }
    public DecisionChoice Choice { get; private set; }
    public string Reason { get; private set; } = "";
    public string AnswersJson { get; private set; } = "";
    public string ContextJson { get; private set; } = "";
    public string? SizingJson { get; private set; }
}
