using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.Win32;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed record TrendPoint(string Period, decimal? Value);
public sealed record FormulaRow(string Period, decimal? RevenueGrowth, decimal? GrossMargin, decimal? OperatingMargin, decimal? FreeCashFlow, decimal? ReportedFreeCashFlow);
public sealed record RiskRow(string Rule, string Status, string Evidence);
public sealed class FinancialViewModel : SecurityPageViewModel
{
    private PeriodType _periodType;
    private string _currency = "CNY", _period = "全部", _query = "";
    private FinancialMetric? _metric;
    private RiskThresholds _thresholds = new();
    public FinancialViewModel(ISecurityRepository securities, IResearchStore store, ResearchSelection selection, ResearchDialogs dialogs,
        IErrorHandler errors, FinancialCsvService csv) : base("Financial", "财务分析", "比较同口径的期间与币种，以确定性公式计算趋势和风险提示。", securities, store, selection, dialogs, errors)
    {
        Import = new(csv, errors, async () => await RefreshAsync());
        AddCommand = new(async _ => { if (Dialogs.Show(Dialogs.MetricEditor(RequireSecurity(), SelectedCurrency, Snapshot.Sources))) await RefreshAsync(); }, errors);
        EditCommand = new(async _ => { var id = RequireSecurity(); if (SelectedMetric is not null && Dialogs.Show(Dialogs.MetricEditor(id, SelectedCurrency, Snapshot.Sources, SelectedMetric))) await RefreshAsync(); }, errors);
        DeleteCommand = new(async _ => { RequireSecurity(); if (SelectedMetric is { } item && Dialogs.Confirm($"删除 {item.Period} {item.MetricType}？")) { await Store.DeleteMetricAsync(item.Id); await RefreshAsync(); } }, errors);
        ConfigureRiskCommand = new(async _ => { if (Dialogs.Show(Dialogs.ThresholdsEditor(await store.ReadThresholdsAsync()))) { _thresholds = await store.ReadThresholdsAsync(); Rebuild(); } }, errors);
    }
    public FinancialImportViewModel Import { get; }
    public IReadOnlyList<PeriodType> PeriodTypes { get; } = Enum.GetValues<PeriodType>();
    public ObservableCollection<string> Currencies { get; } = [];
    public ObservableCollection<string> Periods { get; } = [];
    public PeriodType SelectedPeriodType { get => _periodType; set { if (Set(ref _periodType, value)) { _period = "全部"; Raise(nameof(SelectedPeriod)); Rebuild(); } } }
    public string SelectedCurrency { get => _currency; set { if (value is not null && Set(ref _currency, value)) Rebuild(); } }
    public string SelectedPeriod { get => _period; set { if (value is not null && Set(ref _period, value)) FilterMetrics(); } }
    public string Query { get => _query; set { if (Set(ref _query, value)) FilterMetrics(); } }
    public FinancialMetric? SelectedMetric { get => _metric; set => Set(ref _metric, value); }
    public ObservableCollection<FinancialMetric> Metrics { get; } = [];
    public ObservableCollection<FormulaRow> Formulas { get; } = [];
    public ObservableCollection<RiskRow> Risks { get; } = [];
    public IReadOnlyList<TrendPoint> RevenueTrend { get; private set; } = [];
    public IReadOnlyList<TrendPoint> GrossMarginTrend { get; private set; } = [];
    public IReadOnlyList<TrendPoint> OperatingMarginTrend { get; private set; } = [];
    public IReadOnlyList<TrendPoint> FcfTrend { get; private set; } = [];
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand ConfigureRiskCommand { get; }
    public override async Task LoadAsync() { _thresholds = await Store.ReadThresholdsAsync(); await base.LoadAsync(); }
    protected override void ApplySnapshot()
    {
        var currencies = Snapshot.Metrics.Select(x => x.Currency).Append(SelectedSecurity?.Currency ?? "CNY").Distinct().Order().ToArray();
        var selected = currencies.Contains(_currency) ? _currency : currencies.First();
        Currencies.Clear(); foreach (var currency in currencies) Currencies.Add(currency); _currency = selected; Raise(nameof(SelectedCurrency));
        Import.SetSecurity(SelectedSecurity?.Id); Rebuild();
    }
    private List<FinancialMetric> Series() => Snapshot.Metrics.Where(x => x.PeriodType == SelectedPeriodType && x.Currency == SelectedCurrency).ToList();
    private void FilterMetrics()
    {
        Metrics.Clear(); foreach (var m in Series().Where(x => (SelectedPeriod == "全部" || x.Period == SelectedPeriod) && (x.MetricType + " " + x.Notes).Contains(Query, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Period).ThenBy(x => x.MetricType)) Metrics.Add(m);
        SelectedMetric = Metrics.FirstOrDefault();
    }
    private void Rebuild()
    {
        var raw = Series(); var series = raw.Where(x => !x.IsEstimated).ToList();
        var periods = FinancialPeriod.ContinuousRange(raw.Select(x => x.Period), SelectedPeriodType);
        Periods.Clear(); Periods.Add("全部"); foreach (var period in periods.Reverse()) Periods.Add(period);
        if (!Periods.Contains(_period)) _period = "全部"; Raise(nameof(SelectedPeriod));
        FilterMetrics(); Formulas.Clear();
        foreach (var p in periods) Formulas.Add(new(p, FinancialCalculations.Derived(series, p, MetricType.RevenueGrowth), FinancialCalculations.Derived(series, p, MetricType.GrossMargin),
            FinancialCalculations.Derived(series, p, MetricType.OperatingMargin), FinancialCalculations.Derived(series, p, MetricType.FreeCashFlow), FinancialCalculations.Value(series, p, MetricType.FreeCashFlow)));
        RevenueTrend = periods.Select(p => new TrendPoint(p, FinancialCalculations.Value(series, p, MetricType.Revenue))).ToList();
        GrossMarginTrend = periods.Select(p => new TrendPoint(p, FinancialCalculations.Derived(series, p, MetricType.GrossMargin) ?? FinancialCalculations.Value(series, p, MetricType.GrossMargin))).ToList();
        OperatingMarginTrend = periods.Select(p => new TrendPoint(p, FinancialCalculations.Derived(series, p, MetricType.OperatingMargin) ?? FinancialCalculations.Value(series, p, MetricType.OperatingMargin))).ToList();
        FcfTrend = periods.Select(p => new TrendPoint(p, FinancialCalculations.Derived(series, p, MetricType.FreeCashFlow) ?? FinancialCalculations.Value(series, p, MetricType.FreeCashFlow))).ToList();
        Raise(nameof(RevenueTrend)); Raise(nameof(GrossMarginTrend)); Raise(nameof(OperatingMarginTrend)); Raise(nameof(FcfTrend));
        Risks.Clear(); foreach (var r in AccountingRiskEngine.Evaluate(raw, SelectedPeriodType, SelectedCurrency, _thresholds)) Risks.Add(new(r.Rule, r.Assessed ? r.Level.ToString() : "未评估", r.Evidence));
    }
}

public sealed class FinancialImportViewModel : ObservableObject
{
    private readonly FinancialCsvService _service;
    private readonly Func<Task> _refresh;
    private CsvDocument? _document;
    private Guid? _securityId;
    private int _revision;
    private bool _canImport, _busy;
    private string _file = "未选择文件", _report = "选择 UTF-8 CSV → 预览 → 字段映射 → 验证 → 导入。";
    private DataView? _preview;
    public FinancialImportViewModel(FinancialCsvService service, IErrorHandler errors, Func<Task> refresh)
    {
        _service = service; _refresh = refresh;
        SelectCommand = new(async _ => { var d = new OpenFileDialog { Filter = "CSV|*.csv" }; if (d.ShowDialog() == true) await LoadFileAsync(d.FileName); }, errors);
        ValidateCommand = new(async _ => await ExecuteAsync(true), errors);
        CommitCommand = new(async _ => await ExecuteAsync(false), errors);
        ReportCommand = new(async _ => { var d = new SaveFileDialog { FileName = "financial-import-report.txt", Filter = "文本|*.txt" }; if (d.ShowDialog() == true) await File.WriteAllTextAsync(d.FileName, Report); }, errors);
    }
    public void SetSecurity(Guid? id) { if (_securityId == id) return; _securityId = id; Invalidate(); Report = "证券已改变，请重新验证导入内容。"; }
    private void Invalidate() { _revision++; CanImport = false; }
    public bool CanImport { get => _canImport; private set => Set(ref _canImport, value); }
    public bool IsIdle => !_busy;
    public string FileName { get => _file; private set => Set(ref _file, value); }
    public string Report { get => _report; private set => Set(ref _report, value); }
    public DataView? Preview { get => _preview; private set => Set(ref _preview, value); }
    public ObservableCollection<FieldMapping> Mappings { get; } = [];
    public AsyncCommand SelectCommand { get; }
    public AsyncCommand ValidateCommand { get; }
    public AsyncCommand CommitCommand { get; }
    public AsyncCommand ReportCommand { get; }
    public async Task LoadFileAsync(string path)
    {
        Invalidate(); _document = null; Mappings.Clear(); Preview = null; FileName = path;
        if (new FileInfo(path).Length > 5_000_000) throw new BusinessException("CSV 超过 5 MB，请拆分。");
        string text;
        try { text = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException) { throw new BusinessException("请提供 UTF-8 编码的 CSV。"); }
        _document = CsvImportService.Parse(text);
        var table = new DataTable(); foreach (var h in _document.Headers) table.Columns.Add(h);
        foreach (var row in _document.Rows.Take(100)) table.Rows.Add(row.Cast<object>().ToArray()); Preview = table.DefaultView;
        foreach (var field in FinancialCsvService.Fields) Mappings.Add(new(field, FinancialCsvService.Required.Contains(field), _document.Headers, Invalidate));
        Report = $"共 {_document.Rows.Count} 行，预览前 100 行。所有记录关联当前证券。";
    }
    public async Task ExecuteAsync(bool validateOnly)
    {
        if (_busy) return;
        if (_document is null || _securityId is null) throw new BusinessException("请先选择证券与 CSV。");
        if (!validateOnly && !CanImport) throw new BusinessException("请先通过整批验证。");
        var revision = _revision; var id = _securityId.Value; var document = _document;
        var mapping = Mappings.ToDictionary(x => x.Field, x => x.Header);
        _busy = true; Raise(nameof(IsIdle)); CanImport = false;
        try
        {
            var result = await Task.Run(() => _service.ExecuteAsync(id, document, mapping, validateOnly));
            if (revision != _revision) { Report = "证券或映射已改变，请重新验证。"; return; }
            CanImport = validateOnly && result.Success;
            Report = result.Success ? validateOnly ? $"{result.RowCount} 行全部通过，尚未写入。可确认导入。" : $"已完整导入 {result.RowCount} 行。" : "整批未导入：\n" + string.Join("\n", result.Errors);
            if (!validateOnly && result.Success) await _refresh();
        }
        finally { _busy = false; Raise(nameof(IsIdle)); }
    }
}
