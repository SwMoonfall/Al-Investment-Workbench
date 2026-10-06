namespace AIInvestmentWorkbench.Domain.Valuation;

public enum ValuationModelType { PE, EV_EBITDA, EV_FCF, SimplifiedDCF, ReverseValuation }
public enum ScenarioKind { Bear, Base, Bull }
public enum ReverseTarget { RevenueCagr, OperatingMargin }
public enum SolveStatus { Solved, NoBracket, IterationLimit, InvalidInput, NumericalFailure }

// Amounts and shares use whole units, never millions. Rates are fractions (0.10 = 10%).
public sealed record ValuationActuals(decimal Revenue, decimal NetDebt, decimal ShareCount, decimal CurrentPrice,
    string Currency, DateOnly DataSourceDate, DateOnly PriceDate, string Source);
public sealed record ValuationInputs
{
    public int SchemaVersion { get; init; } = 1;
    public decimal FutureEPS { get; init; } = 1;
    public decimal TargetMultiple { get; init; } = 15;
    public decimal NetCashAdjustment { get; init; }
    public decimal RevenueGrowth { get; init; } = .05m;
    public decimal OperatingMargin { get; init; } = .15m;
    public decimal EBITDAMargin { get; init; } = .2m;
    public decimal? EBITDA { get; init; }
    public decimal? FCF { get; init; }
    public decimal FCFMargin { get; init; } = .1m;
    public decimal TaxRate { get; init; } = .25m;
    public decimal DepreciationAssumption { get; init; } = .03m;
    public decimal CapexAssumption { get; init; } = .04m;
    public decimal WorkingCapitalAssumption { get; init; } = .1m;
    public decimal DiscountRate { get; init; } = .1m;
    public decimal TerminalGrowth { get; init; } = .02m;
    public int ForecastYears { get; init; } = 5;
    public decimal HoldingYears { get; init; } = 3;
    public ReverseTarget ReverseTarget { get; init; }
    public decimal LowerBound { get; init; } = -.5m;
    public decimal UpperBound { get; init; } = 1m;
    public decimal Tolerance { get; init; } = .000001m;
    public int MaxIterations { get; init; } = 200;
    public string Notes { get; init; } = "";
}
public sealed record ForecastYear(int Year, decimal Revenue, decimal OperatingIncome, decimal Tax,
    decimal Depreciation, decimal Capex, decimal ChangeWorkingCapital, decimal FCF, decimal DiscountFactor, decimal PresentValue);
public sealed record ReverseResult(SolveStatus Status, decimal? Value, int Iterations, decimal? PriceResidual, string Message);
public sealed record ValuationResult(decimal? EnterpriseValue, decimal? EquityValue, decimal? ImpliedPrice,
    decimal? UpsideDownside, decimal? AnnualizedReturn, decimal? PVForecastFCF = null, decimal? TerminalValue = null,
    decimal? PVTerminalValue = null, IReadOnlyList<ForecastYear>? Forecast = null, ReverseResult? Reverse = null,
    string Notice = "");
