using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.Win32;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class JournalViewModel : PageViewModel
{
    private readonly IJournalReviewReader _reader; private readonly WorkspaceContext _workspace; private readonly ISecurityRepository _securities; private readonly ResearchSelection _selection;
    private ReviewEvidence? _preview; private IReadOnlyList<JournalRow> _all = []; private JournalRow? _journal; private ReviewRow? _review; private AIAnalysis? _analysis;
    private string _search = "", _previewText = "选择类型与期间后生成资料预览。", _behavior = "", _status = "日志与复盘均保留历史。AI 只生成单独分析。";
    private bool _busy; private CancellationTokenSource? _cancel; private long _generation;
    public JournalViewModel(IJournalReviewReader reader, WorkspaceContext workspace, ISecurityRepository securities, ResearchSelection selection,
        JournalDialogs dialogs, ResearchDialogs windows, IErrorHandler errors, AIAnalysisService ai, IAIStore aiStore) : base("Journal", "日志与复盘", "记录当时为什么这样做，再用证据检验。短期盈亏不等于决策质量。")
    {
        _reader = reader; _workspace = workspace; _securities = securities; _selection = selection;
        RefreshCommand = new(async _ => await LoadAsync(), errors);
        NewCommand = new(async _ => { if (windows.Show(await dialogs.JournalEditorAsync(Account()))) await LoadAsync(); }, errors);
        CorrectCommand = new(async _ => { var row = SelectedJournal ?? throw new BusinessException("请选择日志。"); if (windows.Show(await dialogs.JournalEditorAsync(Account(), new(Account(), row.Journal.SecurityId, row.Journal.Content, row.Journal.DecisionRecordId, row.Journal.Id)))) await LoadAsync(); }, errors);
        GenerateCommand = new(async _ => { var account = Account(); var evidence = await reader.PreviewAsync(account, Kind == ReviewKind.Quarterly ? SelectedSecurity?.Id : null, Kind, Date(Anchor)); if (account != Account()) return; _preview = evidence; PreviewText = Render(evidence); Status = "资料已生成，尚未保存；请检查后确认保存。"; }, errors);
        SaveReviewCommand = new(async _ => { var preview = _preview ?? throw new BusinessException("请先生成并核对资料预览。"); if (preview.AccountId != Account() || preview.Kind != Kind || preview.Start != JournalRules.Period(Kind, Date(Anchor)).Start || preview.SecurityId != (Kind == ReviewKind.Quarterly ? SelectedSecurity?.Id : null)) throw new BusinessException("账户、期间或证券已变化，请重新生成。"); if (windows.Show(dialogs.ReviewEditor(preview))) { await LoadAsync(); Status = "复盘已保存。"; } }, errors);
        ContinueCommand = new(async _ => { var row = SelectedReview ?? throw new BusinessException("请选择已有复盘。"); var fresh = await reader.PreviewAsync(Account(), row.Review.SecurityId, row.Review.Kind, row.Review.PeriodStart); if (windows.Show(dialogs.ReviewEditor(fresh, row.Review))) await LoadAsync(); }, errors);
        ExportCommand = new(async _ => { var row = SelectedReview ?? throw new BusinessException("请选择已有复盘。"); var dialog = new SaveFileDialog { Filter = "Markdown|*.md", FileName = $"Review-{row.Review.Kind}-{row.Review.PeriodEnd:yyyyMMdd}-v{row.Review.Version}.md" }; if (dialog.ShowDialog() == true) await File.WriteAllTextAsync(dialog.FileName, ReviewText); }, errors);
        JournalAICommand = new(async _ => await RunAI(null), errors); ReviewAICommand = new(async _ => await RunAI(SelectedReview ?? throw new BusinessException("请选择已保存复盘。")), errors);
        CancelAICommand = new(_ => _cancel?.Cancel());
        async Task RunAI(ReviewRow? row)
        {
            if (Busy) throw new BusinessException("AI 复盘正在进行，请等待或取消。");
            var account = Account(); var type = row is null ? AnalysisType.JournalReview : row.Review.Kind == ReviewKind.Quarterly ? AnalysisType.QuarterlyReview : AnalysisType.PortfolioRiskReview;
            var template = (await aiStore.TemplatesAsync()).Single(x => x.AnalysisType == type && x.IsBuiltIn); _cancel = new(); Busy = true;
            try
            {
                Status = "AI 分析中；所选日志或复盘快照将发送到已配置服务，结果单独保存。";
                var result = await ai.RunAsync(template.Id, new(row?.Review.SecurityId, account, type, JournalFrom: Date(AIFrom), JournalTo: Date(AITo), ReviewId: row?.Review.Id), _cancel.Token);
                await LoadAsync(); Status = result.Status == AIAnalysisStatus.Completed ? "AI 分析已单独保存；原始日志、复盘和用户决策未修改。" : result.Error;
            }
            catch (OperationCanceledException) { Status = "AI 复盘已取消，原始记录保持不变。"; }
            finally { _cancel.Dispose(); _cancel = null; Busy = false; }
        }
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public ObservableCollection<JournalRow> Journals { get; } = [];
    public ObservableCollection<ReviewRow> Reviews { get; } = [];
    public ObservableCollection<AIAnalysis> AIReviews { get; } = [];
    public ReviewKind[] Kinds { get; } = Enum.GetValues<ReviewKind>();
    public ReviewKind Kind { get; set; } = ReviewKind.Quarterly;
    public Security? SelectedSecurity { get; set; }
    public string Anchor { get; set; } = JournalRules.LastCompletedQuarter(DateOnly.FromDateTime(DateTime.Today)).End.ToString("yyyy-MM-dd");
    public string AIFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(-90).ToString("yyyy-MM-dd");
    public string AITo { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
    public bool ShowHistory { get; set; }
    private int _selectedTab;
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    public string Search { get => _search; set { if (Set(ref _search, value)) Filter(); } }
    public JournalRow? SelectedJournal { get => _journal; set { if (Set(ref _journal, value)) Raise(nameof(JournalText)); } }
    public ReviewRow? SelectedReview { get => _review; set { if (Set(ref _review, value)) Raise(nameof(ReviewText)); } }
    public AIAnalysis? SelectedAnalysis { get => _analysis; set { if (Set(ref _analysis, value)) Raise(nameof(AIText)); } }
    public string PreviewText { get => _previewText; private set => Set(ref _previewText, value); }
    public string Behavior { get => _behavior; private set => Set(ref _behavior, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Busy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanRunAI)); } }
    public bool CanRunAI => !Busy;
    public string JournalText => SelectedJournal is not { } r ? "选择日志查看原始记录。" : $"{r.Label}\n记录创建：{r.Journal.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n更正原因：{r.Journal.CorrectionReason}\n\n记录价格：{r.Journal.Price?.ToString() ?? "未填写"}（证券本币）\n记录数量：{r.Journal.Quantity?.ToString() ?? "未填写"}\n当时组合权重：{r.Journal.PortfolioWeight?.ToString("P2") ?? "未填写"}\n\n理由\n{r.Journal.Reason}\n\n市场担忧\n{r.Journal.MarketConcern}\n\n为什么市场可能错了\n{r.Journal.WhyMarketMayBeWrong}\n\n预期发展\n{r.Journal.ExpectedDevelopment}\n\n当时 Kill 条件\n{r.Journal.KillConditionSummary}\n\n预期持有期：{r.Journal.ExpectedHoldingPeriod}\n预期天数：{r.Journal.ExpectedHoldingDays?.ToString() ?? "未知"}\n信心（0–100）：{r.Journal.Confidence?.ToString() ?? "未填写"}\n情绪自述：{r.Journal.Emotion}\n\n备注\n{r.Journal.Notes}\n\n来源决策：{r.Journal.DecisionRecordId?.ToString() ?? "无"}\n日志记录不等于实际成交。";
    public string ReviewText => SelectedReview is not { } r ? "选择复盘查看历史快照。" : $"# {r.Label}\n记录创建：{r.Review.CreatedAt:O}\n\n实际结果：{r.Review.ActualResults}\n用户决策：{r.Review.Decision?.ToString() ?? "未决定"}\n用户结论：{r.Review.Conclusion}\n\n假设评估\n" + string.Join("\n", JsonSerializer.Deserialize<AssumptionReview[]>(r.Review.AssumptionsJson)!.Select(x => $"{x.Description} · {x.Outcome} · {x.Evidence}")) + "\n\n" + Render(JsonSerializer.Deserialize<ReviewEvidence>(r.Review.SnapshotJson)!);
    public string AIText => SelectedAnalysis is not { } a ? "选择单独的 AI 分析；也可在 AI 研究助理页查看完整提示词与输入审计。" : $"{a.AnalysisType} · {a.Status} · {a.CreatedAt:O}\n{a.ErrorMessage}\n\n" + (a.Status == AIAnalysisStatus.Completed ? StructuredAnalysis.Render(a.StructuredOutput) : "原文：\n" + a.RawOutput) + "\n\n来源引用\n" + a.SourceReferences;
    public AsyncCommand RefreshCommand { get; } public AsyncCommand NewCommand { get; } public AsyncCommand CorrectCommand { get; } public AsyncCommand GenerateCommand { get; } public AsyncCommand SaveReviewCommand { get; } public AsyncCommand ContinueCommand { get; } public AsyncCommand ExportCommand { get; } public AsyncCommand JournalAICommand { get; } public AsyncCommand ReviewAICommand { get; } public RelayCommand CancelAICommand { get; }
    public override async Task LoadAsync()
    {
        var generation = ++_generation; var account = _workspace.SelectedAccount?.Id; _preview = null; PreviewText = "生成新资料预览后确认保存；历史页保留每个版本。";
        var securities = await _securities.SearchAsync(""); var journals = account.HasValue ? await _reader.JournalsAsync(account.Value, ShowHistory) : []; var reviews = account.HasValue ? await _reader.ReviewsAsync(account.Value, ShowHistory) : [];
        var dashboard = account.HasValue ? await Task.Run(() => _reader.DashboardAsync(account.Value, DateOnly.FromDateTime(DateTime.Today))) : new JournalDashboard([], [], [], []); var behavior = account.HasValue ? await Task.Run(() => _reader.BehaviorAsync(account.Value, DateOnly.FromDateTime(DateTime.Today))) : "请选择账户。";
        if (generation != _generation || account != _workspace.SelectedAccount?.Id) return;
        if (_selection.QuarterlyReviewAnchor is { } anchor) { Kind = ReviewKind.Quarterly; Anchor = anchor.ToString("yyyy-MM-dd"); Raise(nameof(Kind)); Raise(nameof(Anchor)); SelectedTab = 1; _selection.QuarterlyReviewAnchor = null; }
        var selected = _selection.SecurityId ?? SelectedSecurity?.Id; Securities.Clear(); foreach (var x in securities) Securities.Add(x); SelectedSecurity = securities.FirstOrDefault(x => x.Id == selected) ?? securities.FirstOrDefault(); Raise(nameof(SelectedSecurity));
        _all = journals; Filter(); Reviews.Clear(); foreach (var x in reviews) Reviews.Add(x); SelectedReview = Reviews.FirstOrDefault(); AIReviews.Clear(); foreach (var x in dashboard.RecentAI) AIReviews.Add(x); SelectedAnalysis = AIReviews.FirstOrDefault(); Behavior = behavior;
    }
    private void Filter() { Journals.Clear(); foreach (var row in _all.Where(x => (x.Label + " " + x.Journal.Reason + " " + x.Journal.Notes).Contains(Search, StringComparison.OrdinalIgnoreCase))) Journals.Add(row); SelectedJournal = Journals.FirstOrDefault(); }
    private Guid Account() => _workspace.SelectedAccount?.Id ?? throw new BusinessException("请先选择账户。");
    private static DateOnly Date(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new BusinessException("日期格式必须是 yyyy-MM-dd。");
    public static string Render(ReviewEvidence e) => string.Join("\n\n", e.Sections.Select(s => "## " + s.Title + "\n" + string.Join("\n", s.Lines.Select(x => "- " + x))));
}


