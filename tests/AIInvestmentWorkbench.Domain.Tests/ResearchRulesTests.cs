using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Xunit;
namespace AIInvestmentWorkbench.Domain.Tests;

public sealed class ResearchRulesTests
{
    private static readonly Guid Security = Guid.NewGuid();
    private static FinancialMetric M(string period, MetricType type, decimal value, PeriodType periodType = PeriodType.Annual, bool estimated = false, string currency = "CNY")
        => new(Security, period, periodType, type, value, currency, isEstimated: estimated);
    [Theory]
    [InlineData(100, 30, 70)] [InlineData(-10, 5, -15)] [InlineData(0, 0, 0)]
    public void Fcf_SubtractsPositiveCapex(decimal ocf, decimal capex, decimal expected) => Assert.Equal(expected, FinancialCalculations.FreeCashFlow(ocf, capex));
    [Fact] public void MissingInput_IsNotZero() { Assert.Null(FinancialCalculations.FreeCashFlow(10, null)); Assert.Null(FinancialCalculations.Growth(null, 10)); Assert.Null(FinancialCalculations.Ratio(null, 100)); }
    [Theory] [InlineData(0)] [InlineData(-1)]
    public void NonpositiveGrowthBase_IsUndefined(decimal prior) => Assert.Null(FinancialCalculations.Growth(100, prior));
    [Fact] public void Growth_UsesFraction() => Assert.Equal(.25m, FinancialCalculations.Growth(125, 100));
    [Fact]
    public void QuarterlyYearOverYear_UsesSameQuarterNotPreviousQuarter()
    {
        FinancialMetric[] metrics = [M("2024-Q2", MetricType.Revenue, 100, PeriodType.Quarterly), M("2025-Q1", MetricType.Revenue, 1000, PeriodType.Quarterly), M("2025-Q2", MetricType.Revenue, 120, PeriodType.Quarterly)];
        Assert.Equal(.2m, FinancialCalculations.Derived(metrics, "2025-Q2", MetricType.RevenueGrowth));
        Assert.Null(FinancialCalculations.Derived(metrics, "2025-Q1", MetricType.RevenueGrowth));
    }
    [Fact]
    public void DeterministicMarginsDebtAndFcf()
    {
        FinancialMetric[] metrics = [M("2025", MetricType.Revenue, 200), M("2025", MetricType.GrossProfit, 80), M("2025", MetricType.OperatingIncome, 30), M("2025", MetricType.OperatingCashFlow, 25), M("2025", MetricType.Capex, 10), M("2025", MetricType.Debt, 50), M("2025", MetricType.Cash, 60)];
        Assert.Equal(.4m, FinancialCalculations.Derived(metrics, "2025", MetricType.GrossMargin));
        Assert.Equal(.15m, FinancialCalculations.Derived(metrics, "2025", MetricType.OperatingMargin));
        Assert.Equal(15m, FinancialCalculations.Derived(metrics, "2025", MetricType.FreeCashFlow));
        Assert.Equal(-10m, FinancialCalculations.Derived(metrics, "2025", MetricType.NetDebt));
    }
    [Fact] public void MixedCurrency_IsRejected() => Assert.Throws<BusinessException>(() => FinancialCalculations.Derived([M("2025", MetricType.Revenue, 1), M("2025", MetricType.GrossProfit, 2, currency: "USD")], "2025", MetricType.GrossMargin));
    [Theory] [InlineData("25", PeriodType.Annual)] [InlineData("2025-Q0", PeriodType.Quarterly)] [InlineData("2025-Q5", PeriodType.TTM)] [InlineData("2025", PeriodType.TTM)] [InlineData("2025-Q1", PeriodType.Annual)]
    public void InvalidPeriods_AreRejected(string period, PeriodType type) => Assert.Throws<BusinessException>(() => M(period, MetricType.Revenue, 1, type));
    [Fact] public void QuarterPrevious_RollsYear() => Assert.Equal("2024-Q4", FinancialPeriod.Previous("2025-Q1", PeriodType.Quarterly));
    [Fact] public void TrendPeriodsIncludeGaps() => Assert.Equal(new[] { "2024-Q4", "2025-Q1", "2025-Q2" }, FinancialPeriod.ContinuousRange(["2024-Q4", "2025-Q2"], PeriodType.Quarterly));
    [Fact]
    public void FcfThresholdIsConfigurable()
    {
        var data = new[] { M("2023", MetricType.FreeCashFlow, 5), M("2024", MetricType.FreeCashFlow, 8), M("2025", MetricType.FreeCashFlow, 7) };
        var finding = AccountingRiskEngine.Evaluate(data, PeriodType.Annual, "CNY", new(FcfWarning: 10, FcfHigh: 0)).Single(x => x.Rule == "FCF 持续偏弱");
        Assert.Equal(AttentionLevel.Warning, finding.Level);
    }
    [Fact] public void CapexCannotBeNegative() => Assert.Throws<BusinessException>(() => M("2025", MetricType.Capex, -1));
    [Theory] [InlineData(1)] [InlineData(6)]
    public void SourceLevel_ValidBoundaries(int level) => Assert.Equal(level, new ResearchSource(Security, "资料", "发布者", null, "", "", SourceType.AnnualReport, level).ReliabilityLevel);
    [Theory] [InlineData(0)] [InlineData(7)]
    public void SourceLevel_InvalidBoundaries(int level) => Assert.Throws<BusinessException>(() => new ResearchSource(Security, "资料", "", null, "", "", SourceType.PDF, level));
    [Fact] public void Source_RejectsUnsafeUrl() => Assert.Throws<BusinessException>(() => new ResearchSource(Security, "资料", "", null, "file:///secret", "", SourceType.News, 3));
    [Fact]
    public void Research_StaleRevisionDoesNotOverwrite()
    {
        var r = new CompanyResearch(Security); r.Update(new(Overview: "first", UserNotes: "note"), 0);
        Assert.Throws<BusinessException>(() => r.Update(new(Overview: "stale"), 0));
        Assert.Equal("first", r.Overview); Assert.Equal("note", r.UserNotes); Assert.Equal(1, r.Revision);
    }
    [Fact]
    public void UserScoreIsNullableAndZeroIsARealScore()
    {
        var score = new ResearchScore(Security, ScoreDimension.BusinessModel);
        Assert.Null(score.EffectiveScore); score.Edit(20, 0, "reason", "evidence"); Assert.Equal(0, score.EffectiveScore); Assert.Equal("用户评分", score.ScoreOrigin);
        Assert.Throws<BusinessException>(() => score.Edit(20, 101, "", "")); Assert.Equal(100m, ResearchScore.DefaultWeights.Values.Sum());
    }
    private static List<FinancialMetric> RiskData()
    {
        var result = new List<FinancialMetric>();
        for (var i = 0; i < 3; i++)
        {
            var year = (2023 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.AddRange([M(year, MetricType.Revenue, 100 + i * 10), M(year, MetricType.AccountsReceivable, 10 + i * i * 15),
                M(year, MetricType.Inventory, 10 + i * i * 15), M(year, MetricType.Debt, 100 + i * i * 100),
                M(year, MetricType.ShareCount, 100 + i * 10), M(year, MetricType.OperatingCashFlow, 1), M(year, MetricType.NetIncome, 10), M(year, MetricType.Capex, 3), M(year, MetricType.StockBasedCompensation, 30)]);
        }
        return result;
    }
    [Fact]
    public void AllSevenRules_ReturnEvidenceAndHighAttention()
    {
        var result = AccountingRiskEngine.Evaluate(RiskData(), PeriodType.Annual, "CNY", new());
        Assert.Equal(7, result.Count); Assert.All(result, r => { Assert.True(r.Assessed); Assert.Equal(AttentionLevel.HighAttention, r.Level); Assert.Contains("2025", r.Evidence); });
    }
    [Fact]
    public void RiskThresholdsAreConfigurable()
    {
        var result = AccountingRiskEngine.Evaluate(RiskData(), PeriodType.Annual, "CNY", new(10, 20, .01m, .001m, 10, 20, 10, 20, 10, 20, 4));
        Assert.All(result.Where(x => x.Assessed), x => Assert.Equal(AttentionLevel.Normal, x.Level));
        Assert.False(result.Single(x => x.Rule == "股票数量持续增加").Assessed);
    }
    [Fact]
    public void MissingCurrencyPeriodOrEstimatedData_AreNotReportedAsHealthy()
    {
        var rows = new[] { M("2025", MetricType.Revenue, 100, estimated: true) };
        Assert.All(AccountingRiskEngine.Evaluate(rows, PeriodType.Annual, "CNY", new()), x => Assert.False(x.Assessed));
        Assert.All(AccountingRiskEngine.Evaluate(RiskData(), PeriodType.Quarterly, "USD", new()), x => Assert.False(x.Assessed));
    }
    [Fact]
    public void ShareGrowthRequiresConsecutivePeriodsAndEveryPeriodIncreasing()
    {
        var rows = new[] { M("2023", MetricType.ShareCount, 100), M("2024", MetricType.ShareCount, 90), M("2025", MetricType.ShareCount, 120) };
        var finding = AccountingRiskEngine.Evaluate(rows, PeriodType.Annual, "CNY", new(DilutionWarning: 0)).Single(x => x.Rule == "股票数量持续增加");
        Assert.True(finding.Assessed); Assert.Equal(AttentionLevel.Normal, finding.Level);
        Assert.False(AccountingRiskEngine.Evaluate([rows[0], rows[2]], PeriodType.Annual, "CNY", new()).Single(x => x.Rule == "股票数量持续增加").Assessed);
    }
    [Fact] public void InvalidThresholdsRejected() => Assert.Throws<BusinessException>(() => new RiskThresholds(GrowthGapWarning: .5m, GrowthGapHigh: .1m).Validate());
    [Fact]
    public void RuleWarningBoundaryIsInclusive()
    {
        var r = AccountingRiskEngine.Evaluate([M("2025", MetricType.OperatingCashFlow, 8), M("2025", MetricType.NetIncome, 10)], PeriodType.Annual, "CNY", new()).Single(x => x.Rule == "经营现金流弱于净利润");
        Assert.Equal(AttentionLevel.Warning, r.Level);
    }
}
