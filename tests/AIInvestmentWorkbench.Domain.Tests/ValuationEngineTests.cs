using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Valuation;
using Xunit;
namespace AIInvestmentWorkbench.Domain.Tests;

public sealed class ValuationEngineTests
{
    private static ValuationActuals A => new(1000, 100, 100, 10, "CNY", new(2026, 1, 1), new(2026, 10, 1), "年报，完整金额");
    private static ValuationInputs S => new();
    private static void Near(decimal expected, decimal? actual, decimal tolerance = .00000001m) { Assert.NotNull(actual); Assert.InRange(actual.Value, expected - tolerance, expected + tolerance); }
    [Fact] public void PeUsesPerShareEarningsAndTotalCashAdjustment()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.PE, A, S with { FutureEPS = 2, TargetMultiple = 10, NetCashAdjustment = 500, HoldingYears = 2 });
        Assert.Equal(25, r.ImpliedPrice); Assert.Equal(2500, r.EquityValue); Assert.Equal(1.5m, r.UpsideDownside); Near(.58113883008419m, r.AnnualizedReturn); Assert.Null(r.EnterpriseValue);
    }
    [Fact] public void PeDoesNotSubtractDebtTwice() => Assert.Equal(15, ValuationEngine.Calculate(ValuationModelType.PE, A with { NetDebt = 5000 }, S).ImpliedPrice);
    [Fact] public void EbitdaDerivedFromRevenueAndMargin()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.EV_EBITDA, A, S with { EBITDAMargin = .2m, TargetMultiple = 8 });
        Assert.Equal(1600, r.EnterpriseValue); Assert.Equal(1500, r.EquityValue); Assert.Equal(15, r.ImpliedPrice);
    }
    [Fact] public void ExplicitEbitdaTakesPrecedence()
        => Assert.Equal(23, ValuationEngine.Calculate(ValuationModelType.EV_EBITDA, A, S with { EBITDA = 300, EBITDAMargin = .9m, TargetMultiple = 8 }).ImpliedPrice);
    [Fact] public void EvFcfUsesEnterpriseCashFlow()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.EV_FCF, A, S with { FCF = 150, TargetMultiple = 10 });
        Assert.Equal(1500, r.EnterpriseValue); Assert.Equal(1400, r.EquityValue); Assert.Equal(14, r.ImpliedPrice);
    }
    [Fact] public void FcfMarginAndZeroOverrideAreDistinct()
    {
        Assert.Equal(14, ValuationEngine.Calculate(ValuationModelType.EV_FCF, A, S with { FCFMargin = .1m }).ImpliedPrice);
        Assert.Equal(-1, ValuationEngine.Calculate(ValuationModelType.EV_FCF, A, S with { FCF = 0 }).ImpliedPrice);
    }
    [Fact] public void NetCashIncreasesEquity() => Assert.Equal(16, ValuationEngine.Calculate(ValuationModelType.EV_FCF, A with { NetDebt = -100 }, S with { FCF = 100 }).ImpliedPrice);
    [Fact] public void NegativeFcfIsNotClamped()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.EV_FCF, A, S with { FCF = -100 });
        Assert.Equal(-1500, r.EnterpriseValue); Assert.Equal(-16, r.ImpliedPrice); Assert.Null(r.AnnualizedReturn); Assert.Equal(-2.6m, r.UpsideDownside);
    }
    [Fact] public void ZeroPriceHasMinusOneAnnualReturn() => Assert.Equal(-1, ValuationEngine.Calculate(ValuationModelType.PE, A, S with { FutureEPS = 0 }).AnnualizedReturn);
    [Fact] public void DcfOneYearHasIndependentHandCalculatedCashFlowAndTerminal()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, S with { RevenueGrowth = .1m, OperatingMargin = .2m, TaxRate = .25m, DepreciationAssumption = .03m, CapexAssumption = .05m, WorkingCapitalAssumption = .2m, ForecastYears = 1, DiscountRate = .1m, TerminalGrowth = .02m });
        var y = Assert.Single(r.Forecast!);
        Assert.Equal(1100, y.Revenue); Assert.Equal(220, y.OperatingIncome); Assert.Equal(55, y.Tax); Assert.Equal(33, y.Depreciation); Assert.Equal(55, y.Capex); Assert.Equal(20, y.ChangeWorkingCapital); Assert.Equal(123, y.FCF);
        // Terminal revenue=1122, NOPAT=168.3, D&A=33.66, capex=56.1, delta NWC=4.4 -> FCF=141.46.
        Assert.Equal(1768.25m, r.TerminalValue); Near(123m / 1.1m, r.PVForecastFCF); Near(1768.25m / 1.1m, r.PVTerminalValue);
        Near((123m + 1768.25m) / 1.1m, r.EnterpriseValue); Near(((123m + 1768.25m) / 1.1m - 100m) / 100m, r.ImpliedPrice); Assert.Null(r.AnnualizedReturn);
    }
    [Fact] public void ConstantPerpetuityMatchesAnalyticValue()
    {
        var s = S with { RevenueGrowth = 0, OperatingMargin = .2m, TaxRate = 0, DepreciationAssumption = 0, CapexAssumption = 0, WorkingCapitalAssumption = 0, TerminalGrowth = 0, DiscountRate = .1m };
        var r = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, s); Near(2000, r.EnterpriseValue); Near(19, r.ImpliedPrice); Assert.Equal(5, r.Forecast!.Count);
    }
    [Fact] public void ReinvestmentAndLossesRetainNegativeFcfWithoutTaxBenefit()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, S with { OperatingMargin = -.1m, CapexAssumption = .4m });
        Assert.All(r.Forecast!, x => { Assert.True(x.FCF < 0); Assert.Equal(0, x.Tax); }); Assert.True(r.TerminalValue < 0); Assert.Contains("负 FCF", r.Notice);
    }
    [Fact] public void FallingRevenueReleasesWorkingCapital()
    {
        var r = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, S with { RevenueGrowth = -.1m });
        Assert.Equal(-10, r.Forecast![0].ChangeWorkingCapital);
    }
    [Theory] [InlineData(ReverseTarget.RevenueCagr)] [InlineData(ReverseTarget.OperatingMargin)]
    public void ReverseRecoversKnownInputAndMatchesPrice(ReverseTarget target)
    {
        var s = S with { RevenueGrowth = .12m, OperatingMargin = .22m, ReverseTarget = target, LowerBound = 0, UpperBound = .5m };
        var price = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, s).ImpliedPrice!.Value;
        var r = ValuationEngine.Calculate(ValuationModelType.ReverseValuation, A with { CurrentPrice = price }, s);
        Assert.Equal(SolveStatus.Solved, r.Reverse!.Status); Near(target == ReverseTarget.RevenueCagr ? .12m : .22m, r.Reverse.Value, .000001m);
        Near(price, r.ImpliedPrice, s.Tolerance); Assert.InRange(r.Reverse.Iterations, 1, s.MaxIterations); Assert.Null(r.AnnualizedReturn);
    }
    [Fact] public void ReverseAcceptsRootAtBoundary()
    {
        var s = S with { RevenueGrowth = .1m, LowerBound = .1m, UpperBound = .2m };
        var price = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, s).ImpliedPrice!.Value;
        var r = ValuationEngine.Reverse(A with { CurrentPrice = price }, s); Assert.Equal(0, r.Reverse!.Iterations); Assert.Equal(.1m, r.Reverse.Value);
    }
    [Fact] public void ReverseNoBracketIsExplicitAndHasNoInventedPrice()
    {
        var r = ValuationEngine.Reverse(A with { CurrentPrice = 100000 }, S with { LowerBound = 0, UpperBound = .1m });
        Assert.Equal(SolveStatus.NoBracket, r.Reverse!.Status); Assert.Null(r.ImpliedPrice); Assert.Null(r.Reverse.Value);
    }
    [Fact] public void ReverseIterationCapIsEnforced()
    {
        var s = S with { RevenueGrowth = .13m, MaxIterations = 1, LowerBound = 0, UpperBound = .5m };
        var price = ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, s).ImpliedPrice!.Value;
        var r = ValuationEngine.Reverse(A with { CurrentPrice = price }, s); Assert.Equal(SolveStatus.IterationLimit, r.Reverse!.Status); Assert.Equal(1, r.Reverse.Iterations); Assert.Null(r.Reverse.Value);
    }
    [Fact] public void ReverseOverflowIsExplicit() => Assert.Equal(SolveStatus.NumericalFailure, ValuationEngine.Reverse(A with { Revenue = decimal.MaxValue }, S).Reverse!.Status);
    [Theory] [InlineData(ValuationModelType.PE)] [InlineData(ValuationModelType.EV_EBITDA)] [InlineData(ValuationModelType.EV_FCF)] [InlineData(ValuationModelType.SimplifiedDCF)]
    public void ZeroSharesRejected(ValuationModelType type) => Assert.Throws<BusinessException>(() => ValuationEngine.Calculate(type, A with { ShareCount = 0 }, S));
    [Fact] public void ReverseZeroSharesReturnsInvalidInput() => Assert.Equal(SolveStatus.InvalidInput, ValuationEngine.Reverse(A with { ShareCount = 0 }, S).Reverse!.Status);
    [Theory] [InlineData(0)] [InlineData(-1)] public void NonpositiveCurrentPriceRejected(int price) => Assert.Throws<BusinessException>(() => ValuationEngine.Calculate(ValuationModelType.PE, A with { CurrentPrice = price }, S));
    [Fact] public void ExtremeMultiplicationFailsClearly() => Assert.Throws<BusinessException>(() => ValuationEngine.Calculate(ValuationModelType.PE, A, S with { FutureEPS = decimal.MaxValue }));
    public static IEnumerable<object[]> InvalidDcfInputs()
    {
        yield return [S with { DiscountRate = .02m, TerminalGrowth = .02m }]; yield return [S with { DiscountRate = .01m }]; yield return [S with { DiscountRate = 0 }];
        yield return [S with { TerminalGrowth = -1 }]; yield return [S with { RevenueGrowth = -1 }]; yield return [S with { ForecastYears = 0 }]; yield return [S with { ForecastYears = 31 }];
        yield return [S with { TaxRate = 1.1m }]; yield return [S with { CapexAssumption = -.1m }]; yield return [S with { WorkingCapitalAssumption = 2 }]; yield return [S with { HoldingYears = 0 }];
    }
    [Theory] [MemberData(nameof(InvalidDcfInputs))] public void InvalidDcfRejected(ValuationInputs s) => Assert.Throws<BusinessException>(() => ValuationEngine.Calculate(ValuationModelType.SimplifiedDCF, A, s));
    [Fact] public void InvalidSolverBoundsAndLimitsReturnResult()
    {
        foreach (var s in new[] { S with { LowerBound = 1, UpperBound = 0 }, S with { MaxIterations = 0 }, S with { MaxIterations = 1001 }, S with { Tolerance = 0 }, S with { UpperBound = 6 }, S with { ReverseTarget = ReverseTarget.OperatingMargin, UpperBound = 2 } })
            Assert.Equal(SolveStatus.InvalidInput, ValuationEngine.Reverse(A, s).Reverse!.Status);
    }
    [Fact] public void EntityMaintainsRevisionAndInputsSnapshot()
    {
        var model = new ValuationModel(Guid.NewGuid(), "模型", ValuationModelType.PE, A); Assert.Equal(1, model.Revision);
        model.Update("新模型", A, 1); Assert.Equal(2, model.Revision); Assert.Throws<BusinessException>(() => model.Touch(1));
        var scenario = new ValuationScenario(model.Id, ScenarioKind.Base, S, new(2026, 10, 3)); Assert.Equal(S, scenario.Inputs); Assert.NotEqual(Guid.Empty, scenario.Id);
    }
}
