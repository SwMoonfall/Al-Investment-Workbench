using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class ResearchScore : Entity
{
    private ResearchScore() { }
    public ResearchScore(Guid securityId, ScoreDimension dimension)
    {
        SecurityId = Guard.Id(securityId, nameof(securityId));
        if (!Enum.IsDefined(dimension)) throw new BusinessException("未知评分维度。");
        Dimension = dimension; Weight = DefaultWeights[dimension];
    }
    public static IReadOnlyDictionary<ScoreDimension, decimal> DefaultWeights { get; } = new Dictionary<ScoreDimension, decimal>
    {
        [ScoreDimension.BusinessModel] = 20, [ScoreDimension.CompetitiveAdvantage] = 15, [ScoreDimension.FinancialQuality] = 20,
        [ScoreDimension.ManagementCapitalAllocation] = 10, [ScoreDimension.GrowthPotential] = 15, [ScoreDimension.Valuation] = 15, [ScoreDimension.RiskUnderstandability] = 5
    };
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public ScoreDimension Dimension { get; private set; }
    public decimal Weight { get; private set; }
    public decimal? AIScore { get; private set; }
    public decimal? UserScore { get; private set; }
    public string Reason { get; private set; } = "";
    public string Evidence { get; private set; } = "";
    public decimal? EffectiveScore => UserScore ?? AIScore;
    public string ScoreOrigin => UserScore.HasValue ? "用户评分" : AIScore.HasValue ? "AI 评分（未经用户确认）" : "尚未评分";
    public void Edit(decimal weight, decimal? userScore, string reason, string evidence)
    {
        if (weight is < 0 or > 100 || userScore is < 0 or > 100) throw new BusinessException("权重与评分必须在 0 至 100 之间。");
        Weight = weight; UserScore = userScore; Reason = Guard.OptionalText(reason, 10000); Evidence = Guard.OptionalText(evidence, 10000); MarkUpdated();
    }
}
