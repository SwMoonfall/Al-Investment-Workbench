using System.Collections.ObjectModel;
using System.Globalization;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Valuation;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed record ValuationComparisonRow(string Label, string Bear, string Base, string Bull);
public sealed record ValuationDetailRow(string Label, string Value);
public sealed class ValuationViewModel : PageViewModel
{
    private readonly ISecurityRepository _securities; private readonly IValuationStore _store;
    private readonly ResearchSelection _selection; private readonly IErrorHandler _errors; private readonly ValuationService _calculator;
    private Security? _security; private ValuationModelDto? _model; private ScenarioKind _kind = ScenarioKind.Base;
    private long _generation; private bool _busy;
    public ValuationViewModel(ISecurityRepository securities, IValuationStore store, ResearchSelection selection, ValuationService calculator, ValuationDialogs dialogs, ResearchDialogs windows, IErrorHandler errors)
        : base("Valuation", "估值 · Valuation", "独立假设，确定性计算。对照价格反推市场隐含预期。")
    {
        _securities = securities; _store = store; _selection = selection; _errors = errors; _calculator = calculator;
        ReloadCommand = new(async _ => await LoadAsync(), errors);
        CreateCommand = new(async _ => { var security = SelectedSecurity ?? throw new BusinessException("请先新增并选择证券。"); Guid? id = null; if (windows.Show(dialogs.ModelEditor(security, null, x => id = x))) await ReloadModels(id); }, errors);
        ActualsCommand = new(async _ => { var m = RequireModel(); if (windows.Show(dialogs.ModelEditor(SelectedSecurity!, m))) await ReloadModels(m.Id); }, errors);
        EditCommand = new(async parameter => { var m = RequireModel(); var kind = parameter is string text ? Enum.Parse<ScenarioKind>(text) : SelectedKind; if (windows.Show(dialogs.ScenarioEditor(m, kind))) await ReloadModels(m.Id); }, errors);
        CopyCommand = new(async parameter => { var m = RequireModel(); var kind = Enum.Parse<ScenarioKind>((string)parameter!); if (!windows.Confirm($"用 Base 假设覆盖 {kind}？来源数据不变，假设日期更新为今天。")) return; await _store.CopyBaseAsync(m.Id, m.Revision, kind, DateOnly.FromDateTime(DateTime.Today)); await ReloadModels(m.Id); }, errors);
        DeleteCommand = new(async _ => { var m = RequireModel(); if (!windows.Confirm($"删除估值“{m.Name}”及三个情景？")) return; await _store.DeleteAsync(m.Id, m.Revision); await ReloadModels(); }, errors);
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public ObservableCollection<ValuationModelDto> Models { get; } = [];
    public ObservableCollection<ValuationComparisonRow> Comparison { get; } = [];
    public ObservableCollection<ValuationDetailRow> Details { get; } = [];
    public ObservableCollection<ForecastYear> Forecast { get; } = [];
    public IReadOnlyList<ScenarioKind> Kinds { get; } = Enum.GetValues<ScenarioKind>();
    public Security? SelectedSecurity { get => _security; set { if (Set(ref _security, value)) { _selection.SecurityId = value?.Id; RefreshSelection(); } } }
    public ValuationModelDto? SelectedModel { get => _model; set { if (Set(ref _model, value)) { Recalculate(); Raise(nameof(HasModel)); Raise(nameof(ActualsText)); } } }
    public ScenarioKind SelectedKind { get => _kind; set { if (Set(ref _kind, value)) Recalculate(); } }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(HasModel)); } }
    public bool HasModel => SelectedModel is not null && !IsBusy;
    public string ActualsText => SelectedModel is not { } m ? "先选择证券并建立估值模型。" : $"Actual · 用户录入 / 未核验    {m.ModelType} · {m.Actuals.Currency} · 修订 {m.Revision}\n基期收入 {m.Actuals.Revenue:N2}    净债务 {m.Actuals.NetDebt:N2}    总股数 {m.Actuals.ShareCount:N0}    当前价格 {m.Actuals.CurrentPrice:N4}\n数据来源日期 {m.Actuals.DataSourceDate:yyyy-MM-dd}    价格日期 {m.Actuals.PriceDate:yyyy-MM-dd}\n来源：{m.Actuals.Source}";
    public AsyncCommand ReloadCommand { get; } public AsyncCommand CreateCommand { get; } public AsyncCommand ActualsCommand { get; }
    public AsyncCommand EditCommand { get; } public AsyncCommand CopyCommand { get; } public AsyncCommand DeleteCommand { get; }
    public override async Task LoadAsync()
    {
        var rows = await _securities.SearchAsync(""); var id = _selection.SecurityId;
        Securities.Clear(); foreach (var row in rows) Securities.Add(row);
        _security = Securities.FirstOrDefault(x => x.Id == id) ?? Securities.FirstOrDefault(); _selection.SecurityId = _security?.Id; Raise(nameof(SelectedSecurity));
        await ReloadModels(SelectedModel?.Id);
    }
    private async void RefreshSelection() { try { await ReloadModels(); } catch (Exception e) { _errors.Report(e, "无法读取估值，请刷新重试。"); } }
    public async Task ReloadModels(Guid? preferred = null)
    {
        var generation = ++_generation; IsBusy = true;
        try { var rows = _security is null ? [] : await _store.ListAsync(_security.Id); if (generation != _generation) return; Models.Clear(); foreach (var row in rows) Models.Add(row); SelectedModel = Models.FirstOrDefault(x => x.Id == preferred) ?? Models.FirstOrDefault(); }
        finally { if (generation == _generation) IsBusy = false; }
    }
    private ValuationModelDto RequireModel() => HasModel ? SelectedModel! : throw new BusinessException("请选择估值模型，或等待读取完成。");
    private static string N(decimal? x) => x?.ToString("N4", CultureInfo.CurrentCulture) ?? "不适用";
    private static string P(decimal? x) => x?.ToString("P2", CultureInfo.CurrentCulture) ?? "不适用";
    private static string ReverseRate(ScenarioCalculation x, ReverseTarget target, decimal original)
        => x.Result.Reverse is not { } reverse || x.Scenario.Inputs.ReverseTarget != target ? P(original)
            : reverse.Status == SolveStatus.Solved ? $"Calculated {P(reverse.Value)}" : "未求得有效值";
    private void Recalculate()
    {
        Comparison.Clear(); Details.Clear(); Forecast.Clear(); if (_model is null) return;
        var results = _calculator.Compare(_model); var dcf = _model.ModelType is ValuationModelType.SimplifiedDCF or ValuationModelType.ReverseValuation;
        void Row(string label, Func<ScenarioCalculation, string> format) => Comparison.Add(new(label, format(results.Single(x => x.Scenario.Kind == ScenarioKind.Bear)), format(results.Single(x => x.Scenario.Kind == ScenarioKind.Base)), format(results.Single(x => x.Scenario.Kind == ScenarioKind.Bull))));
        Row("Assumption · 假设日期", x => x.Scenario.AssumptionDate.ToString("yyyy-MM-dd"));
        Row("Assumption · 收入增长率", x => dcf ? ReverseRate(x, ReverseTarget.RevenueCagr, x.Scenario.Inputs.RevenueGrowth) : "不适用");
        Row("Assumption · 利润率", x => dcf ? ReverseRate(x, ReverseTarget.OperatingMargin, x.Scenario.Inputs.OperatingMargin) : _model.ModelType == ValuationModelType.EV_EBITDA ? x.Scenario.Inputs.EBITDA.HasValue ? "直接输入 EBITDA" : P(x.Scenario.Inputs.EBITDAMargin) : _model.ModelType == ValuationModelType.EV_FCF ? x.Scenario.Inputs.FCF.HasValue ? "直接输入 FCFF" : P(x.Scenario.Inputs.FCFMargin) : "不适用");
        Row("Assumption · 倍数", x => dcf ? "不适用" : N(x.Scenario.Inputs.TargetMultiple));
        Row("Calculated · 每股估值", x => N(x.Result.ImpliedPrice));
        Row(dcf ? "Calculated · 现值价差" : "Calculated · 预期回报（价差）", x => P(x.Result.UpsideDownside));
        Row("Calculated · 年化回报", x => P(x.Result.AnnualizedReturn));
        Row(dcf ? "Assumption · 预测年数" : "Assumption · 比较期限（年）", x => dcf ? x.Scenario.Inputs.ForecastYears.ToString() : N(x.Scenario.Inputs.HoldingYears));
        if (_model.ModelType == ValuationModelType.ReverseValuation) { Row("反推目标", x => x.Scenario.Inputs.ReverseTarget.ToString()); Row("Calculated · 求解结果", x => x.Result.Reverse!.Status.ToString()); }
        var selected = results.Single(x => x.Scenario.Kind == SelectedKind); var value = selected.Result;
        Details.Add(new("口径", value.Notice)); Details.Add(new("Assumption · 依据", selected.Scenario.Inputs.Notes));
        Details.Add(new("Calculated · 企业价值", N(value.EnterpriseValue))); Details.Add(new("Calculated · 股权价值", N(value.EquityValue)));
        if (dcf) { Details.Add(new("预测 FCF 现值合计", N(value.PVForecastFCF))); Details.Add(new("终值（预测期末）", N(value.TerminalValue))); Details.Add(new("终值现值", N(value.PVTerminalValue))); }
        if (value.Reverse is { } reverse) { Details.Add(new("求解说明", reverse.Message)); Details.Add(new("反推值 / 迭代次数 / 价格残差", $"{P(reverse.Value)} / {reverse.Iterations} / {N(reverse.PriceResidual)}")); Details.Add(new("边界 / 价格容忍度 / 迭代上限", $"[{P(selected.Scenario.Inputs.LowerBound)}, {P(selected.Scenario.Inputs.UpperBound)}] / {selected.Scenario.Inputs.Tolerance} / {selected.Scenario.Inputs.MaxIterations}")); }
        foreach (var row in value.Forecast ?? []) Forecast.Add(row);
    }
}

