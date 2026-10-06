using System.Collections.ObjectModel;
using System.IO;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.Win32;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class AIResearchViewModel : PageViewModel
{
    private readonly IAIStore _store; private readonly IAIContextBuilder _contexts; private readonly AIAnalysisService _service;
    private readonly ISecurityRepository _securities; private readonly WorkspaceContext _workspace; private readonly ResearchSelection _selection;
    private Security? _security; private PromptTemplate? _template; private AIAnalysis? _analysis; private AIResearchReport? _report;
    private CancellationTokenSource? _run; private string _status = "AI 只提供研究建议，不修改投资逻辑、估值或交易。", _notes = "", _preview = "点击预览可检查即将发送的上下文。";
    private bool _busy; private int _progress;
    public AIResearchViewModel(IAIStore store, IAIContextBuilder contexts, AIAnalysisService service, ISecurityRepository securities,
        WorkspaceContext workspace, ResearchSelection selection, ResearchDialogs dialogs, IErrorHandler errors) : base("AIResearch", "AI 研究助理", "资料 → 假设 → 反证。AI 输出需核实，最终投资决策由用户完成。")
    {
        _store = store; _contexts = contexts; _service = service; _securities = securities; _workspace = workspace; _selection = selection;
        RefreshCommand = new(async _ => await LoadAsync(), errors);
        PreviewCommand = new(async _ => { EnsureIdle(); Preview = (await contexts.BuildAsync(Request())).Json; }, errors);
        RunCommand = new(async _ => await Run(false), errors);
        FullCommand = new(async _ => await Run(true), errors);
        CancelCommand = new(_ => { _run?.Cancel(); Status = "正在取消，已完成的章节和分析记录会保留。"; });
        CopyCommand = new(async _ => { EnsureIdle(); var id = await store.CopyTemplateAsync(RequireTemplate().Id); await LoadTemplates(id); }, errors);
        RestoreCommand = new(async _ => { EnsureIdle(); var t = RequireTemplate(); if (dialogs.Confirm("恢复此模板的默认提示词？已有分析快照不变。")) { await store.RestoreTemplateAsync(t.Id, t.Revision); await LoadTemplates(t.Id); } }, errors);
        EditCommand = new(async _ =>
        {
            EnsureIdle(); var t = RequireTemplate();
            dialogs.Show(new("编辑提示词", "系统研究边界始终保留；旧分析保存原始提示词快照。", [new("Name", "名称", t.Name), new("Body", "任务提示词", t.Body, multiline: true)],
                e => store.EditTemplateAsync(t.Id, t.Revision, e.Text("Name"), e.Text("Body")), errors)); await LoadTemplates(t.Id);
        }, errors);
        ExportCommand = new(async _ =>
        {
            var report = SelectedReport ?? throw new BusinessException("请在报告页选中报告。");
            var file = new SaveFileDialog { Filter = "Markdown|*.md", FileName = "Research-" + report.CreatedAt.ToString("yyyyMMdd-HHmmss") + ".md" };
            if (file.ShowDialog() == true) { await File.WriteAllTextAsync(file.FileName, report.ReportMarkdown); Status = "报告已导出。全部章节与审计记录已自动保存在本地数据库。"; }
        }, errors);
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public ObservableCollection<PromptTemplate> Templates { get; } = [];
    public ObservableCollection<AIAnalysis> Analyses { get; } = [];
    public ObservableCollection<AIResearchReport> Reports { get; } = [];
    public Security? SelectedSecurity { get => _security; set { if (Set(ref _security, value)) { _selection.SecurityId = value?.Id; ClearHistory(); } } }
    public PromptTemplate? SelectedTemplate { get => _template; set { if (Set(ref _template, value)) { Preview = "任务已更改，请重新预览。"; ClearHistory(); } } }
    public AIAnalysis? SelectedAnalysis { get => _analysis; set { if (Set(ref _analysis, value)) { Raise(nameof(ResultText)); Raise(nameof(AuditText)); } } }
    public AIResearchReport? SelectedReport { get => _report; set => Set(ref _report, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public string Preview { get => _preview; private set => Set(ref _preview, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanEdit)); } }
    public bool CanEdit => !Busy;
    public int ProgressValue { get => _progress; private set => Set(ref _progress, value); }
    public string ResultText => SelectedAnalysis is not { } a ? "选择历史分析，或开始一次新分析。" : a.Status == AIAnalysisStatus.Completed ? StructuredAnalysis.Render(a.StructuredOutput) : a.ErrorMessage;
    public string AuditText => SelectedAnalysis is not { } a ? "" : $"请求模型：{a.Model}\n返回模型：{a.ResponseModel}\nProvider：{a.Provider}\n响应 ID：{a.ResponseId}\n状态：{a.Status}\n创建：{a.CreatedAt:O}\n完成：{a.FinishedAt:O}\n\n设置（无密钥）\n{a.SettingsSnapshot}\n\n提示词快照\n{a.PromptSnapshot}\n\n上下文快照\n{a.ContextSnapshot}\n\n来源引用\n{a.SourceReferences}\n\n完整响应封套\n{a.ResponseEnvelope}";
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand RunCommand { get; }
    public AsyncCommand FullCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand CopyCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public AsyncCommand ExportCommand { get; }
    private void EnsureIdle() { if (Busy) throw new BusinessException("请等待当前分析结束，或取消后重试。"); }
    private PromptTemplate RequireTemplate() => SelectedTemplate ?? throw new BusinessException("请选择提示词模板。");
    private AIContextRequest Request() { var type = RequireTemplate().AnalysisType; return new(type is AnalysisType.PortfolioRiskReview or AnalysisType.JournalReview ? null : SelectedSecurity?.Id, _workspace.SelectedAccount?.Id, type, Notes); }
    private void ClearHistory() { Analyses.Clear(); Reports.Clear(); SelectedAnalysis = null; SelectedReport = null; Preview = "对象或任务已更改，请重新预览；点击刷新载入历史。"; }
    private async Task LoadTemplates(Guid? id) { var rows = await _store.TemplatesAsync(); Templates.Clear(); foreach (var x in rows) Templates.Add(x); SelectedTemplate = rows.FirstOrDefault(x => x.Id == id) ?? rows.FirstOrDefault(); }
    public override async Task LoadAsync()
    {
        if (Busy) return;
        var rows = await _securities.SearchAsync(""); var id = _selection.SecurityId ?? SelectedSecurity?.Id;
        Securities.Clear(); foreach (var x in rows) Securities.Add(x); SelectedSecurity = rows.FirstOrDefault(x => x.Id == id) ?? rows.FirstOrDefault();
        await LoadTemplates(SelectedTemplate?.Id); await History();
    }
    private async Task History()
    {
        var id = Request().SecurityId; var analyses = await _store.AnalysesAsync(id);
        Analyses.Clear(); foreach (var x in analyses) Analyses.Add(x); SelectedAnalysis = analyses.FirstOrDefault();
        Reports.Clear(); if (SelectedSecurity is { } s) foreach (var x in await _store.ReportsAsync(s.Id)) Reports.Add(x); SelectedReport = Reports.FirstOrDefault();
    }
    private async Task Run(bool full)
    {
        EnsureIdle(); var template = RequireTemplate(); var request = Request();
        if (full && SelectedSecurity is null) throw new BusinessException("完整公司研究需要选择证券。");
        Busy = true; _run = new(); ProgressValue = 0;
        try
        {
            Status = full ? "正在执行 10 步研究流程，每步完成即保存。" : "正在分析；结果自动保存…";
            if (full)
            {
                await _service.FullCompanyAsync(SelectedSecurity!.Id, request.AccountId, Notes, new Progress<AIProgress>(p => { ProgressValue = p.Step; Status = $"{p.Step}/{p.Total} · {p.Message}"; }), _run.Token);
                await History(); Status = $"报告已保存：{SelectedReport?.Status}，完成 {SelectedReport?.CompletedSections}/10 章节。";
            }
            else { var result = await _service.RunAsync(template.Id, request, _run.Token); await History(); Status = result.Status == AIAnalysisStatus.Completed ? "分析已保存。请核实来源和推断；AI 建议不会修改投资记录。" : result.Error; }
        }
        catch (OperationCanceledException) { Status = "已取消。"; }
        finally { _run.Dispose(); _run = null; Busy = false; }
    }
}
