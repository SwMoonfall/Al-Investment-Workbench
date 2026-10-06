using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Valuation;

/// <summary>Pure deterministic arithmetic. No providers, network, persistence or AI dependency.</summary>
public static class ValuationEngine
{
    public static void ValidateActuals(ValuationActuals a)
    {
        if (a.Revenue < 0 || a.ShareCount <= 0 || a.CurrentPrice <= 0)
            throw new BusinessException("收入不可为负；股数和当前价格必须大于零。");
        Guard.Currency(a.Currency); Guard.Text(a.Source, "数据来源", 1000);
        if (a.DataSourceDate == default || a.PriceDate == default) throw new BusinessException("请提供数据来源日期与当前价格日期。");
    }
    public static void Validate(ValuationModelType type, ValuationActuals a, ValuationInputs s)
    {
        ValidateActuals(a);
        if (!Enum.IsDefined(type) || s.SchemaVersion != 1) throw new BusinessException("不支持的估值模型或输入版本。");
        if (s.HoldingYears <= 0 || s.HoldingYears > 100) throw new BusinessException("比较期限应大于 0 且不超过 100 年。");
        if (s.TargetMultiple < 0 || s.TargetMultiple > 1000) throw new BusinessException("估值倍数应在 0 至 1000 之间。");
        Guard.OptionalText(s.Notes, 4000);
        if (type is not (ValuationModelType.SimplifiedDCF or ValuationModelType.ReverseValuation)) return;
        if (a.Revenue <= 0) throw new BusinessException("DCF 基期收入必须大于零。");
        if (s.ForecastYears is < 1 or > 30) throw new BusinessException("预测期应为 1 至 30 年。");
        if (s.RevenueGrowth <= -1 || s.RevenueGrowth > 5) throw new BusinessException("收入增长率必须大于 -100% 且不超过 500%。");
        if (s.OperatingMargin is < -1 or > 1) throw new BusinessException("营业利润率应在 -100% 至 100% 之间。");
        if (s.TaxRate is < 0 or > 1 || s.DepreciationAssumption is < 0 or > 1 || s.CapexAssumption is < 0 or > 1 || s.WorkingCapitalAssumption is < 0 or > 1)
            throw new BusinessException("税率、折旧、资本开支及营运资本比例应在 0 至 100% 之间。");
        if (s.DiscountRate <= 0 || s.DiscountRate > 1 || s.TerminalGrowth <= -1 || s.DiscountRate <= s.TerminalGrowth)
            throw new BusinessException("折现率须在 0 至 100%（不含 0），且大于永续增长率；永续增长率须大于 -100%。");
        if (type == ValuationModelType.ReverseValuation)
        {
            if (!Enum.IsDefined(s.ReverseTarget) || s.MaxIterations is < 1 or > 1000 || s.Tolerance is < .0000000001m or > 1m || s.LowerBound >= s.UpperBound)
                throw new BusinessException("求解设置无效：迭代 1–1000 次，价格容忍度 0.0000000001–1，下界小于上界。");
            var low = s.ReverseTarget == ReverseTarget.RevenueCagr ? -.99m : -1m;
            var high = s.ReverseTarget == ReverseTarget.RevenueCagr ? 5m : 1m;
            if (s.LowerBound < low || s.UpperBound > high) throw new BusinessException("求解边界超出允许范围：CAGR -99% 至 500%；稳定营业利润率 -100% 至 100%。");
        }
    }
    public static ValuationResult Calculate(ValuationModelType type, ValuationActuals a, ValuationInputs s)
    {
        if (type == ValuationModelType.ReverseValuation) return Reverse(a, s);
        Validate(type, a, s);
        try
        {
            if (type == ValuationModelType.SimplifiedDCF) return Dcf(a, s);
            decimal? ev = null;
            decimal equity;
            if (type == ValuationModelType.PE) equity = s.FutureEPS * s.TargetMultiple * a.ShareCount + s.NetCashAdjustment;
            else
            {
                var flow = type == ValuationModelType.EV_EBITDA ? s.EBITDA ?? a.Revenue * s.EBITDAMargin : s.FCF ?? a.Revenue * s.FCFMargin;
                ev = flow * s.TargetMultiple; equity = ev.Value - a.NetDebt;
            }
            var price = equity / a.ShareCount;
            return new(ev, equity, price, price / a.CurrentPrice - 1, Annualize(price, a.CurrentPrice, s.HoldingYears),
                Notice: price < 0 ? "股权价值为负，保留原始结果；年化回报不适用。" : "倍数法按指定期限的目标值比较；不包含股息、税费与稀释变化。");
        }
        catch (OverflowException) { throw new BusinessException("数值超出计算范围，请检查金额单位、增长率和预测期。"); }
    }
    private static decimal? Annualize(decimal price, decimal current, decimal years)
    {
        if (price < 0) return null;
        var value = Math.Pow((double)(price / current), 1d / (double)years) - 1;
        return double.IsFinite(value) && value < (double)decimal.MaxValue ? (decimal)value : null;
    }
    private static ValuationResult Dcf(ValuationActuals a, ValuationInputs s)
    {
        var rows = new List<ForecastYear>(); var revenue = a.Revenue; var factor = 1m; var pv = 0m;
        for (var year = 1; year <= s.ForecastYears; year++)
        {
            var previous = revenue; revenue *= 1 + s.RevenueGrowth; factor /= 1 + s.DiscountRate;
            var row = Year(year, previous, revenue, factor, s); rows.Add(row); pv += row.PresentValue;
        }
        // Recompute year N+1 at terminal growth; using final forecast FCF*(1+g) would misstate ΔNWC.
        var terminalFcf = Year(s.ForecastYears + 1, revenue, revenue * (1 + s.TerminalGrowth), factor, s).FCF;
        var terminal = terminalFcf / (s.DiscountRate - s.TerminalGrowth); var pvTerminal = terminal * factor;
        var ev = pv + pvTerminal; var equity = ev - a.NetDebt; var price = equity / a.ShareCount;
        return new(ev, equity, price, price / a.CurrentPrice - 1, null, pv, terminal, pvTerminal, rows,
            Notice: "DCF 是今日现值；价差不是未来回报，年化回报不适用。亏损不计即时税盾。" + (rows.Any(x => x.FCF < 0) || terminalFcf < 0 ? " 存在负 FCF，未截断或归零。" : ""));
    }
    private static ForecastYear Year(int year, decimal prior, decimal revenue, decimal factor, ValuationInputs s)
    {
        var ebit = revenue * s.OperatingMargin; var tax = Math.Max(0, ebit) * s.TaxRate;
        var depreciation = revenue * s.DepreciationAssumption; var capex = revenue * s.CapexAssumption;
        var nwc = (revenue - prior) * s.WorkingCapitalAssumption;
        var fcf = ebit - tax + depreciation - capex - nwc;
        return new(year, revenue, ebit, tax, depreciation, capex, nwc, fcf, factor, fcf * factor);
    }
    public static ValuationResult Reverse(ValuationActuals a, ValuationInputs s)
    {
        ValuationResult Failure(SolveStatus status, string message, int iterations = 0, decimal? residual = null)
            => new(null, null, null, null, null, Reverse: new(status, null, iterations, residual, message), Notice: message);
        try { Validate(ValuationModelType.ReverseValuation, a, s); }
        catch (ArgumentException e) { return Failure(SolveStatus.InvalidInput, e.Message); }
        catch (BusinessException e) { return Failure(SolveStatus.InvalidInput, e.Message); }
        try
        {
            ValuationResult At(decimal value) => Dcf(a, s.ReverseTarget == ReverseTarget.RevenueCagr ? s with { RevenueGrowth = value } : s with { OperatingMargin = value });
            ValuationResult Success(decimal value, int iterations, ValuationResult result) => result with
            {
                Reverse = new(SolveStatus.Solved, value, iterations, result.ImpliedPrice!.Value - a.CurrentPrice, "已在指定价格容忍度内求解。固定其余假设；边界内不保证唯一根。"),
                Notice = "反推值是匹配当前价格所需的假设，不是预测；稳定营业利润率应用于全部预测年及终值。"
            };
            var lo = s.LowerBound; var hi = s.UpperBound; var left = At(lo); var right = At(hi);
            var fl = left.ImpliedPrice!.Value - a.CurrentPrice; var fr = right.ImpliedPrice!.Value - a.CurrentPrice;
            if (Math.Abs(fl) <= s.Tolerance) return Success(lo, 0, left);
            if (Math.Abs(fr) <= s.Tolerance) return Success(hi, 0, right);
            if (Math.Sign(fl) == Math.Sign(fr)) return Failure(SolveStatus.NoBracket, "给定边界未夹住解；调整边界或其他假设。此结果不表示全域无解。");
            decimal residual = 0;
            for (var i = 1; i <= s.MaxIterations; i++)
            {
                var mid = lo + (hi - lo) / 2; var result = At(mid); residual = result.ImpliedPrice!.Value - a.CurrentPrice;
                if (Math.Abs(residual) <= s.Tolerance) return Success(mid, i, result);
                if (mid == lo || mid == hi) return Failure(SolveStatus.NumericalFailure, "已达数值精度极限，仍未满足价格容忍度。", i, residual);
                if (Math.Sign(residual) == Math.Sign(fl)) { lo = mid; fl = residual; } else hi = mid;
            }
            return Failure(SolveStatus.IterationLimit, "达到最大迭代次数，尚未满足价格容忍度。", s.MaxIterations, residual);
        }
        catch (OverflowException) { return Failure(SolveStatus.NumericalFailure, "数值溢出，请收窄边界或降低金额、增长率和预测期。"); }
    }
}
