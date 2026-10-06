using System.Globalization;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Valuation;
namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class ValuationDialogs(IValuationStore store, IErrorHandler errors)
{
    private static string N(decimal x) => x.ToString(CultureInfo.InvariantCulture);
    private static FormField F(string key, string label, string value) => new(key, label, value);
    private static DateOnly Date(EditorViewModel vm, string key) => DateOnly.FromDateTime(vm.Date(key).Date);
    public EditorViewModel ModelEditor(Security security, ValuationModelDto? model, Action<Guid>? created = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var a = model?.Actuals ?? new(100000000, 0, 10000000, security.LatestPrice ?? 10, security.Currency, today, security.PriceDate.HasValue ? DateOnly.FromDateTime(security.PriceDate.Value.LocalDateTime) : today, "用户录入，请替换为真实来源");
        List<FormField> fields = [F("Name", "模型名称", model?.Name ?? "新估值"),
            new("Type", "模型类型（建立后固定）", (model?.ModelType ?? ValuationModelType.PE).ToString(), model is null ? Enum.GetValues<ValuationModelType>().Select(x => new FormOption(x.ToString(), x.ToString())).ToArray() : null, readOnly: model is not null),
            F("Revenue", "Actual · 基期收入（完整金额，不用万元/百万元）", N(a.Revenue)), F("Debt", "Actual · 净债务（负数表示净现金）", N(a.NetDebt)),
            F("Shares", "Actual · 总股数（完整股数）", N(a.ShareCount)), F("Price", "Actual · 当前每股价格", N(a.CurrentPrice)),
            new("Currency", "币种（与证券一致）", security.Currency, readOnly: true), F("DataDate", "数据来源日期 yyyy-MM-dd", a.DataSourceDate.ToString("yyyy-MM-dd")),
            F("PriceDate", "当前价格日期 yyyy-MM-dd", a.PriceDate.ToString("yyyy-MM-dd")), F("Source", "来源与口径说明", a.Source)];
        return new(model is null ? "建立估值模型" : "编辑基础数据", "Actual 表示用户录入的基础数据，未经系统核验。收入为基期值；倍数法可输入未来 EBITDA/FCF。新模型三情景初始相同，须逐一调整假设。所有金额须已换算到证券币种。", fields, async vm =>
        {
            var actuals = new ValuationActuals(vm.Number("Revenue"), vm.Number("Debt"), vm.Number("Shares"), vm.Number("Price"), security.Currency, Date(vm, "DataDate"), Date(vm, "PriceDate"), vm.Text("Source"));
            if (model is null) { var id = await store.CreateAsync(security.Id, vm.Text("Name"), vm.EnumValue<ValuationModelType>("Type"), actuals, new(), today); created?.Invoke(id); }
            else await store.UpdateActualsAsync(model.Id, model.Revision, vm.Text("Name"), actuals);
        }, errors);
    }
    public EditorViewModel ScenarioEditor(ValuationModelDto model, ScenarioKind kind)
    {
        var scenario = model.Scenarios.Single(x => x.Kind == kind); var s = scenario.Inputs;
        var fields = new List<FormField> { F("Date", "Assumption · 假设日期 yyyy-MM-dd", scenario.AssumptionDate.ToString("yyyy-MM-dd")) };
        void Add(string key, string label, decimal value) => fields.Add(F(key, label, N(value)));
        var dcf = model.ModelType is ValuationModelType.SimplifiedDCF or ValuationModelType.ReverseValuation;
        if (model.ModelType == ValuationModelType.PE) { Add("EPS", "未来 EPS（每股）", s.FutureEPS); Add("Cash", "额外净现金调整（总金额；避免与 EPS/PE 重复计入）", s.NetCashAdjustment); }
        if (model.ModelType == ValuationModelType.EV_EBITDA) { Add("EMargin", "EBITDA / 基期收入比例（0.2 = 20%）", s.EBITDAMargin); fields.Add(F("EBITDA", "未来 EBITDA 总额（空白按基期收入 × 上述比例）", s.EBITDA?.ToString(CultureInfo.InvariantCulture) ?? "")); }
        if (model.ModelType == ValuationModelType.EV_FCF) { Add("FMargin", "FCFF / 基期收入比例（0.1 = 10%）", s.FCFMargin); fields.Add(F("FCF", "未来企业自由现金流 FCFF（空白按收入 × 比例）", s.FCF?.ToString(CultureInfo.InvariantCulture) ?? "")); }
        if (!dcf) { Add("Multiple", "目标倍数", s.TargetMultiple); Add("Holding", "达到目标的比较期限（年）", s.HoldingYears); }
        if (dcf)
        {
            Add("Growth", "年收入增长率（0.05 = 5%；CAGR 反推时此项被求解值替代）", s.RevenueGrowth);
            Add("Margin", "稳定营业利润率（利润率反推时此项被求解值替代）", s.OperatingMargin);
            Add("Tax", "税率（亏损无即时税盾）", s.TaxRate); Add("DA", "折旧摊销 / 收入", s.DepreciationAssumption);
            Add("Capex", "资本开支 / 收入", s.CapexAssumption); Add("NWC", "营运资本 / 收入（现金流扣除其年度增量）", s.WorkingCapitalAssumption);
            Add("Discount", "折现率 WACC", s.DiscountRate); Add("Terminal", "永续增长率（必须低于折现率）", s.TerminalGrowth);
            Add("Years", "逐年预测期（整数 1–30）", s.ForecastYears);
        }
        if (model.ModelType == ValuationModelType.ReverseValuation)
        {
            fields.Add(new("Target", "反推变量", s.ReverseTarget.ToString(), [new("Revenue CAGR · 收入复合增速", "RevenueCagr"), new("稳定营业利润率（全部年份）", "OperatingMargin")]));
            Add("Lower", "求解下界（比例）", s.LowerBound); Add("Upper", "求解上界（比例）", s.UpperBound);
            Add("Tolerance", "价格误差容忍度（每股绝对金额）", s.Tolerance); Add("Iterations", "最大迭代次数（整数 1–1000）", s.MaxIterations);
        }
        fields.Add(new("Notes", "假设依据 / 证据", s.Notes, multiline: true));
        return new($"{kind} · 编辑假设", "Assumption · 比例全部用小数，例如 10% 填 0.10。只保存完整且有效的输入。Calculated 结果由确定性代码生成。", fields, vm =>
        {
            decimal Num(string key, decimal original) => fields.Any(x => x.Key == key) ? vm.Number(key) : original;
            int Integer(string key, int original) { var n = Num(key, original); if (n != decimal.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new Domain.Rules.BusinessException("年数与迭代次数须为整数。"); return (int)n; }
            decimal? Optional(string key, decimal? original) => !fields.Any(x => x.Key == key) ? original : vm.Text(key).Length == 0 ? null : vm.Number(key);
            var updated = s with { FutureEPS = Num("EPS", s.FutureEPS), NetCashAdjustment = Num("Cash", s.NetCashAdjustment),
                TargetMultiple = Num("Multiple", s.TargetMultiple), HoldingYears = Num("Holding", s.HoldingYears), EBITDAMargin = Num("EMargin", s.EBITDAMargin), EBITDA = Optional("EBITDA", s.EBITDA),
                FCFMargin = Num("FMargin", s.FCFMargin), FCF = Optional("FCF", s.FCF), RevenueGrowth = Num("Growth", s.RevenueGrowth), OperatingMargin = Num("Margin", s.OperatingMargin),
                TaxRate = Num("Tax", s.TaxRate), DepreciationAssumption = Num("DA", s.DepreciationAssumption), CapexAssumption = Num("Capex", s.CapexAssumption), WorkingCapitalAssumption = Num("NWC", s.WorkingCapitalAssumption),
                DiscountRate = Num("Discount", s.DiscountRate), TerminalGrowth = Num("Terminal", s.TerminalGrowth), ForecastYears = Integer("Years", s.ForecastYears),
                ReverseTarget = model.ModelType == ValuationModelType.ReverseValuation ? vm.EnumValue<ReverseTarget>("Target") : s.ReverseTarget,
                LowerBound = Num("Lower", s.LowerBound), UpperBound = Num("Upper", s.UpperBound), Tolerance = Num("Tolerance", s.Tolerance), MaxIterations = Integer("Iterations", s.MaxIterations), Notes = vm.Text("Notes") };
            return store.SaveScenarioAsync(model.Id, model.Revision, kind, updated, Date(vm, "Date"));
        }, errors);
    }
}
