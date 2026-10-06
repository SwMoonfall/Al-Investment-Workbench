using System.Collections.ObjectModel;
using System.Globalization;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Risk;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed record RiskHoldingRow(RiskPosition Position, string LimitStatus)
{ public string Tags => string.Join(" / ", Position.Tags.Select(x => x.Name)); public string PriceNotice => Position.EstimatedPrice ? "成本暂估" : Position.PriceDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "日期未知"; }
public sealed record RiskAlertRow(string Security, string Kind, string Detail);
public sealed class RiskViewModel : PageViewModel
{
    private readonly IPortfolioRiskStore _store; private readonly ISecurityRepository _securities; private readonly WorkspaceContext _workspace; private readonly ResearchSelection _selection; private readonly IErrorHandler _errors;
    private Security? _security; private PortfolioRiskSnapshot? _snapshot; private DecisionPreview? _preview; private DecisionRecordDto? _record;
    private string _base = "0.05", _confidence = "1", _valuation = "1", _risk = "1", _sizingText = "请输入参数并计算。\n" + PortfolioRiskRules.SizingNotice; private SizingInputs? _plan;
    private long _generation, _selectionGeneration; private bool _busy;
    public RiskViewModel(IPortfolioRiskStore store, ISecurityRepository securities, WorkspaceContext workspace, ResearchSelection selection, PortfolioRiskDialogs dialogs, ResearchDialogs windows, IErrorHandler errors, JournalDialogs journalDialogs)
        : base("Risk", "组合风险与决策", "看见重叠暴露，记录判断依据。每一个决定都由你完成。")
    {
        _store = store; _securities = securities; _workspace = workspace; _selection = selection; _errors = errors;
        ReloadCommand = new(async _ => await LoadAsync(), errors);
        JournalCommand = new(async _ => { var record = SelectedRecord ?? throw new BusinessException("请选择已保存决策。"); if (record.Context.Portfolio.AccountId != _workspace.SelectedAccount?.Id) throw new BusinessException("账户已改变，请刷新。"); windows.Show(await journalDialogs.JournalEditorAsync(record.Context.Portfolio.AccountId, DecisionJournalMapper.Draft(record))); }, errors);
        CreateTagCommand = new(async _ => { if (windows.Show(dialogs.TagEditor())) await LoadAsync(); }, errors);
        AssignTagCommand = new(async _ => { var security = RequireSecurity(); var tag = SelectedTag ?? throw new BusinessException("请选择待添加标签。"); await store.SetTagAsync(security.Id, tag.Id, true); await LoadAsync(); }, errors);
        RemoveTagCommand = new(async _ => { var security = RequireSecurity(); var tag = SelectedAssignedTag ?? throw new BusinessException("请选择已关联标签。"); await store.SetTagAsync(security.Id, tag.Id, false); await LoadAsync(); }, errors);
        ThresholdCommand = new(async _ => { if (windows.Show(dialogs.ThresholdEditor(Snapshot?.NearMaxRatio ?? .9m))) await LoadAsync(); }, errors);
        CalculateCommand = new(async _ => { var preview = RequirePreview(); var plan = new SizingInputs(Number(BasePosition), Number(ConfidenceFactor), Number(ValuationFactor), Number(RiskFactor)); SizingText = PortfolioRiskDialogs.SizingText(plan, preview.Context); _plan = plan; await Task.CompletedTask; }, errors);
        AddDecisionCommand = Decision(DecisionKind.AddPosition); ExitDecisionCommand = Decision(DecisionKind.ExitPosition);
        AsyncCommand Decision(DecisionKind kind) => new(async _ =>
        {
            var security = RequireSecurity(); var account = _workspace.SelectedAccount ?? throw new BusinessException("请先选择账户。");
            var preview = await Task.Run(() => store.PreviewAsync(account.Id, security.Id));
            if (windows.Show(dialogs.DecisionEditor(preview, kind, kind == DecisionKind.AddPosition ? _plan : null))) await LoadAsync();
        }, errors);
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public ObservableCollection<RiskTagDto> Tags { get; } = [];
    public ObservableCollection<RiskTagDto> AssignedTags { get; } = [];
    public ObservableCollection<RiskHoldingRow> Holdings { get; } = [];
    public ObservableCollection<RiskAlertRow> Alerts { get; } = [];
    public ObservableCollection<DecisionRecordDto> Records { get; } = [];
    public RiskTagDto? SelectedTag { get; set; } public RiskTagDto? SelectedAssignedTag { get; set; }
    public Security? SelectedSecurity { get => _security; set { if (Set(ref _security, value)) { _selection.SecurityId = value?.Id; SelectionLoadTask = RefreshSelection(); } } }
    public PortfolioRiskSnapshot? Snapshot { get => _snapshot; private set { Set(ref _snapshot, value); Raise(nameof(Summary)); } }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanDecide)); } }
    public bool CanDecide => !IsBusy && _preview is not null;
    public Task SelectionLoadTask { get; private set; } = Task.CompletedTask;
    public string ContextText => _preview is null ? "选择账户和证券后查看系统核对信息。" : PortfolioRiskDialogs.ContextText(_preview.Context);
    public string Summary => Snapshot is not { } p ? "请先创建并选择账户。证券标签可独立维护。" : $"总资产 {p.TotalAssets:N2} {p.Currency}   现金 {p.Cash:N2}   持仓 {p.Positions.Count}\n最大单证券 {p.Exposure.Concentration.LargestPosition:P2}（含 ETF） / 最大股票 {p.Exposure.Concentration.LargestStockPosition:P2}\nTop 3 {p.Exposure.Concentration.Top3:P2}   Top 5 {p.Exposure.Concentration.Top5:P2}\n最大行业暴露 {p.Exposure.Concentration.LargestSector:P2}   最大市场暴露 {p.Exposure.Concentration.LargestMarket:P2}\nTriggered Kill {p.TriggeredCount} 项   Thesis Warning {p.ThesisWarningCount} 条   会计 High Attention {p.HighAttention.Count} 项\n成本暂估 {p.EstimatedPriceCount} 项；会计检查数据不足 {p.UnassessedAccountingChecks} 项。缺数据不等于低风险。";
    public string BasePosition { get => _base; set { if (Set(ref _base, value)) InvalidatePlan(); } }
    public string ConfidenceFactor { get => _confidence; set { if (Set(ref _confidence, value)) InvalidatePlan(); } }
    public string ValuationFactor { get => _valuation; set { if (Set(ref _valuation, value)) InvalidatePlan(); } }
    public string RiskFactor { get => _risk; set { if (Set(ref _risk, value)) InvalidatePlan(); } }
    public string SizingText { get => _sizingText; private set => Set(ref _sizingText, value); }
    public DecisionRecordDto? SelectedRecord { get => _record; set { if (Set(ref _record, value)) Raise(nameof(RecordText)); } }
    public string RecordText => SelectedRecord is not { } r ? "选择记录查看当时的清单、理由和风险快照。" : $"{r.Label}\n理由：{r.Reason}\n\n" +
        string.Join("\n", r.Answers.Select(a => DecisionRules.Questions(r.Kind).Single(q => q.Key == a.Key).Text + "  " + a.Answer)) + "\n\n当时系统快照\n" + PortfolioRiskDialogs.ContextText(r.Context) +
        "\n\n当时组合暴露（可重叠）\n" + string.Join("\n", r.Context.Portfolio.Exposure.Exposures.Select(x => $"{x.Category} / {x.Name}：{x.Weight:P2}")) +
        "\n\n当时持仓\n" + string.Join("\n", r.Context.Portfolio.Positions.Select(x => $"{x.Security}：{x.Weight:P2} / 上限 {x.MaxWeight:P2} / 报价 {x.PriceDate:yyyy-MM-dd}")) +
        (r.Sizing is null ? "" : "\n\n当时仓位规划\n" + PortfolioRiskDialogs.SizingText(r.Sizing, r.Context));
    public AsyncCommand JournalCommand { get; }
    public AsyncCommand ReloadCommand { get; } public AsyncCommand CreateTagCommand { get; } public AsyncCommand AssignTagCommand { get; } public AsyncCommand RemoveTagCommand { get; }
    public AsyncCommand ThresholdCommand { get; } public AsyncCommand CalculateCommand { get; } public AsyncCommand AddDecisionCommand { get; } public AsyncCommand ExitDecisionCommand { get; }
    public override async Task LoadAsync()
    {
        var generation = ++_generation; IsBusy = true; var account = _workspace.SelectedAccount?.Id; var selected = _selection.SecurityId ?? _security?.Id;
        try
        {
            var securities = await _securities.SearchAsync(""); var tags = await _store.TagsAsync();
            var snapshot = account.HasValue ? await Task.Run(() => _store.ReadAsync(account.Value)) : null;
            var records = account.HasValue ? await Task.Run(() => _store.DecisionsAsync(account.Value)) : [];
            if (generation != _generation || account != _workspace.SelectedAccount?.Id) return;
            Securities.Clear(); foreach (var item in securities) Securities.Add(item); Tags.Clear(); foreach (var tag in tags) Tags.Add(tag);
            _security = Securities.FirstOrDefault(x => x.Id == selected) ?? Securities.FirstOrDefault(); _selection.SecurityId = _security?.Id; Raise(nameof(SelectedSecurity));
            Snapshot = snapshot; Holdings.Clear(); Alerts.Clear(); Records.Clear();
            if (snapshot is not null)
            {
                foreach (var p in snapshot.Positions) Holdings.Add(new(p, PortfolioRiskDialogs.WeightText(PortfolioRiskRules.CheckMaximum(p.Weight, p.MaxWeight, snapshot.NearMaxRatio))));
                foreach (var t in snapshot.Theses)
                {
                    if (t.Status == Domain.Enums.ThesisStatus.Warning) Alerts.Add(new(t.Security, "Thesis Warning", $"{t.Title} · v{t.Version}" + (t.IsCurrent ? " · 当前" : " · 非当前开放逻辑")));
                    foreach (var k in t.Conditions.Where(x => x.Status == Domain.Enums.KillConditionStatus.Triggered)) Alerts.Add(new(t.Security, "Triggered Kill", $"{t.Title} / {k.Title} · {k.Evidence}"));
                }
                foreach (var a in snapshot.HighAttention) Alerts.Add(new(a.Security, "High Attention", $"{a.PeriodType} / {a.Currency} / {a.Rule} · {a.Evidence}"));
            }
            foreach (var r in records) Records.Add(r); SelectedRecord = Records.FirstOrDefault(); SelectionLoadTask = RefreshSelection(); await SelectionLoadTask;
        }
        finally { if (generation == _generation) IsBusy = false; }
    }
    private async Task RefreshSelection()
    {
        var generation = ++_selectionGeneration; _preview = null; InvalidatePlan(); Raise(nameof(ContextText)); Raise(nameof(CanDecide)); AssignedTags.Clear();
        var security = _security; var account = _workspace.SelectedAccount?.Id;
        if (security is null) return;
        try
        {
            var tags = await _store.SecurityTagsAsync(security.Id); var preview = account.HasValue ? await Task.Run(() => _store.PreviewAsync(account.Value, security.Id)) : null;
            if (generation != _selectionGeneration || account != _workspace.SelectedAccount?.Id) return;
            foreach (var tag in tags) AssignedTags.Add(tag); _preview = preview; Raise(nameof(ContextText)); Raise(nameof(CanDecide));
        }
        catch (Exception e) { _errors.Report(e, "无法读取证券风险，请刷新重试。"); }
    }
    private Security RequireSecurity() => SelectedSecurity ?? throw new BusinessException("请选择证券。");
    private DecisionPreview RequirePreview() => _preview ?? throw new BusinessException("请先选择账户和证券，等待读取完成。");
    private void InvalidatePlan() { _plan = null; SizingText = "参数或证券已改变，请重新计算。\n" + PortfolioRiskRules.SizingNotice; }
    private static decimal Number(string text) => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : throw new BusinessException("请输入小数，例如 5% 填 0.05，不使用千分符。");
}




