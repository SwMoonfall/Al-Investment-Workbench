using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;
namespace AIInvestmentWorkbench.Domain.Tests;

public sealed class PortfolioRiskRulesTests
{
    private static RiskPosition P(decimal weight, params RiskLabel[] tags) => new(Guid.NewGuid(), "证券", weight * 1000, weight, .2m, false, null, tags);
    [Fact] public void TagsAggregateAcrossSecuritiesAndDoNotSplitTheirWeight()
    {
        var result = PortfolioRiskRules.Aggregate([P(.2m, new RiskLabel(RiskTagCategory.Theme, "Semiconductor"), new RiskLabel(RiskTagCategory.Theme, "AI Capex")), P(.15m, new RiskLabel(RiskTagCategory.Theme, "Semiconductor")), P(.22m, new RiskLabel(RiskTagCategory.Theme, "AI Capex"))], .43m, "CNY");
        Assert.Equal(.35m, Assert.Single(result.Exposures, x => x.Name == "Semiconductor").Weight);
        Assert.Equal(.42m, Assert.Single(result.Exposures, x => x.Name == "AI Capex").Weight);
        Assert.Equal(2, Assert.Single(result.Exposures, x => x.Name == "AI Capex").SecurityCount);
    }
    [Fact] public void DuplicateLabelsCountOncePerSecurityIgnoringCase()
    {
        var result = PortfolioRiskRules.Aggregate([P(.4m, new RiskLabel(RiskTagCategory.Theme, " AI Capex "), new RiskLabel(RiskTagCategory.Theme, "ai capex"))], .6m, "CNY");
        Assert.Equal(.4m, Assert.Single(result.Exposures, x => x.Category == RiskTagCategory.Theme).Weight);
    }
    [Fact] public void SameNameDifferentCategoriesRemainsDistinct()
    {
        var result = PortfolioRiskRules.Aggregate([P(.3m, new RiskLabel(RiskTagCategory.Sector, "Technology"), new RiskLabel(RiskTagCategory.Theme, "Technology"))], .7m, "CNY");
        Assert.Equal(2, result.Exposures.Count(x => x.Name == "Technology"));
    }
    [Fact] public void LargestStockExcludesEtfButTopThreeIncludesIt()
    {
        var result = PortfolioRiskRules.Aggregate([P(.4m) with { SecurityType = Domain.Enums.SecurityType.Etf }, P(.2m)], .4m, "CNY");
        Assert.Equal(.4m, result.Concentration.LargestPosition); Assert.Equal(.2m, result.Concentration.LargestStockPosition); Assert.Equal(.6m, result.Concentration.Top3);
    }
    [Fact] public void TopThreeAndTopFiveAreSortedAndIncludeCashInDenominator()
    {
        var result = PortfolioRiskRules.Aggregate([P(.04m), P(.2m), P(.1m), P(.15m), P(.05m), P(.01m)], .45m, "CNY");
        Assert.Equal(.2m, result.Concentration.LargestPosition); Assert.Equal(.45m, result.Concentration.Top3); Assert.Equal(.54m, result.Concentration.Top5);
    }
    [Fact] public void SectorAndMarketConcentrationAggregateSharedLabels()
    {
        var labels = new[] { new RiskLabel(RiskTagCategory.Sector, "Technology"), new RiskLabel(RiskTagCategory.Market, "US") };
        var result = PortfolioRiskRules.Aggregate([P(.2m, labels), P(.35m, labels)], .45m, "CNY");
        Assert.Equal(.55m, result.Concentration.LargestSector); Assert.Equal(.55m, result.Concentration.LargestMarket);
    }
    [Fact] public void CurrencyIncludesCashWithoutCountingItAsSecurity()
    {
        var result = PortfolioRiskRules.Aggregate([P(.35m, new RiskLabel(RiskTagCategory.Currency, "USD"))], .65m, "USD");
        var usd = Assert.Single(result.Exposures); Assert.Equal(1, usd.Weight); Assert.Equal(1, usd.SecurityCount);
    }
    [Fact] public void CashOnlyAndZeroAssetsHaveZeroConcentration()
    {
        var cash = PortfolioRiskRules.Aggregate([], 1, "CNY"); Assert.Equal(new Concentration(0, 0, 0, 0, 0), cash.Concentration); Assert.Equal(1, Assert.Single(cash.Exposures).Weight);
        Assert.Empty(PortfolioRiskRules.Aggregate([], 0, "CNY").Exposures);
    }
    [Fact] public void MultipleTagsMayExceedOneInTotalWithoutInflatingPositionConcentration()
    {
        var r = PortfolioRiskRules.Aggregate([P(.7m, new RiskLabel(RiskTagCategory.Theme, "Growth"), new RiskLabel(RiskTagCategory.Theme, "AI Capex"))], .3m, "CNY");
        Assert.Equal(1.4m, r.Exposures.Where(x => x.Category == RiskTagCategory.Theme).Sum(x => x.Weight)); Assert.Equal(.7m, r.Concentration.LargestPosition);
    }
    [Fact] public void DuplicateSecuritiesAndOverallocatedInputsRejected()
    {
        var p = P(.2m); Assert.Throws<BusinessException>(() => PortfolioRiskRules.Aggregate([p, p], .6m, "CNY"));
        Assert.Throws<BusinessException>(() => PortfolioRiskRules.Aggregate([P(.9m)], .2m, "CNY"));
    }
    [Fact] public void PositionSizingUsesAllFourInputsWithoutSilentCap()
    {
        var result = PortfolioRiskRules.Size(new(.1m, .8m, 1.5m, .5m), .2m); Assert.Equal(.06m, result.SuggestedPosition); Assert.Equal(WeightWarning.Normal, result.Warning);
        var excess = PortfolioRiskRules.Size(new(.5m, 3, 1, 1), .2m); Assert.Equal(1.5m, excess.SuggestedPosition); Assert.True(excess.AboveFullPortfolio); Assert.Equal(WeightWarning.AboveMaximum, excess.Warning);
    }
    [Theory] [InlineData("0", WeightWarning.Normal)] [InlineData("0.179", WeightWarning.Normal)] [InlineData("0.18", WeightWarning.NearMaximum)] [InlineData("0.2", WeightWarning.NearMaximum)] [InlineData("0.201", WeightWarning.AboveMaximum)]
    public void MaxWeightUsesInclusiveNearBoundary(string weight, WeightWarning expected) => Assert.Equal(expected, PortfolioRiskRules.CheckMaximum(decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture), .2m));
    [Fact] public void ThresholdIsConfigurableAndZeroMaximumHandled()
    {
        Assert.Equal(WeightWarning.NearMaximum, PortfolioRiskRules.CheckMaximum(.15m, .2m, .75m));
        Assert.Equal(WeightWarning.Normal, PortfolioRiskRules.CheckMaximum(0, 0)); Assert.Equal(WeightWarning.AboveMaximum, PortfolioRiskRules.CheckMaximum(.01m, 0));
        Assert.Throws<BusinessException>(() => PortfolioRiskRules.CheckMaximum(.1m, .2m, 0));
    }
    [Fact] public void ZeroFactorProducesZeroAndInvalidFactorsFail()
    {
        Assert.Equal(0, PortfolioRiskRules.Size(new(.1m, 1, 1, 0), .2m).SuggestedPosition);
        Assert.Throws<BusinessException>(() => PortfolioRiskRules.Size(new(.1m, -1, 1, 1), .2m));
        Assert.Throws<BusinessException>(() => PortfolioRiskRules.Size(new(.1m, decimal.MaxValue, decimal.MaxValue, 1), .2m));
    }
    [Theory] [InlineData(DecisionKind.AddPosition, DecisionChoice.Add)] [InlineData(DecisionKind.AddPosition, DecisionChoice.NoAction)] [InlineData(DecisionKind.ExitPosition, DecisionChoice.Hold)] [InlineData(DecisionKind.ExitPosition, DecisionChoice.Trim)] [InlineData(DecisionKind.ExitPosition, DecisionChoice.Exit)] [InlineData(DecisionKind.ExitPosition, DecisionChoice.NoAction)]
    public void AllHumanOutcomesHaveValidCompleteChecklist(DecisionKind kind, DecisionChoice choice)
        => DecisionRules.Validate(kind, choice, DecisionRules.Questions(kind).Select(q => new DecisionAnswer(q.Key, ChecklistAnswer.Unknown)).ToArray(), "已核对并记录未确定因素");
    [Fact] public void IncompleteDuplicateAndWrongChecklistRejected()
    {
        var answers = DecisionRules.Questions(DecisionKind.AddPosition).Select(q => new DecisionAnswer(q.Key, ChecklistAnswer.Yes)).ToArray();
        Assert.Throws<BusinessException>(() => DecisionRules.Validate(DecisionKind.AddPosition, DecisionChoice.Exit, answers, "原因"));
        Assert.Throws<BusinessException>(() => DecisionRules.Validate(DecisionKind.AddPosition, DecisionChoice.Add, answers[..^1], "原因"));
        answers[0] = answers[1]; Assert.Throws<BusinessException>(() => DecisionRules.Validate(DecisionKind.AddPosition, DecisionChoice.Add, answers, "原因"));
    }
    [Fact] public void EmptyReasonAndInvalidTagCategoryRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => DecisionRules.Validate(DecisionKind.ExitPosition, DecisionChoice.Hold, DecisionRules.Questions(DecisionKind.ExitPosition).Select(q => new DecisionAnswer(q.Key, ChecklistAnswer.No)).ToArray(), " "));
        Assert.Throws<BusinessException>(() => new RiskTag("Custom", (RiskTagCategory)99)); Assert.Equal("AI CAPEX", new RiskTag(" AI Capex ", RiskTagCategory.Theme).NormalizedName);
    }
}

