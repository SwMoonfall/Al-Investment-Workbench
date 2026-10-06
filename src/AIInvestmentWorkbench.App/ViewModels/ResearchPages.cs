using System.Collections.ObjectModel;
using System.IO;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.Win32;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class ResearchSelection
{
    public Guid? ThesisId { get; set; }
    public Guid? SecurityId { get; set; }
    public DateOnly? QuarterlyReviewAnchor { get; set; }
    public event Action<string>? NavigationRequested;
    public void Open(Guid securityId, string page) { SecurityId = securityId; NavigationRequested?.Invoke(page); }
}
public abstract class SecurityPageViewModel : PageViewModel
{
    private readonly ISecurityRepository _securities;
    private readonly ResearchSelection _selection;
    private readonly IErrorHandler _errors;
    private Security? _selected;
    private bool _loading;
    private long _version;
    protected readonly IResearchStore Store;
    protected readonly ResearchDialogs Dialogs;
    protected ResearchSnapshot Snapshot = new(null, [], [], []);
    protected SecurityPageViewModel(string key, string title, string subtitle, ISecurityRepository securities, IResearchStore store,
        ResearchSelection selection, ResearchDialogs dialogs, IErrorHandler errors) : base(key, title, subtitle)
    {
        _securities = securities; _selection = selection; _errors = errors; Store = store; Dialogs = dialogs;
        ReloadCommand = new(async _ => await LoadAsync(), errors);
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public Security? SelectedSecurity
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { _selection.SecurityId = value?.Id; OnSelectionChanged(); } }
    }
    public bool IsLoading { get => _loading; private set { Set(ref _loading, value); Raise(nameof(HasSecurity)); } }
    public bool HasSecurity => SelectedSecurity is not null && !IsLoading;
    public AsyncCommand ReloadCommand { get; }
    protected Guid RequireSecurity() => HasSecurity ? SelectedSecurity!.Id : throw new BusinessException("请先选择证券并等待资料载入；没有证券时请先在证券页面新增。");
    private async void OnSelectionChanged()
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { _errors.Report(ex, ex is BusinessException ? ex.Message : "无法载入研究资料，请刷新重试。"); }
    }
    public override async Task LoadAsync()
    {
        var items = await _securities.SearchAsync("");
        var selected = items.FirstOrDefault(x => x.Id == _selection.SecurityId) ?? items.FirstOrDefault();
        _selected = null; Securities.Clear(); foreach (var s in items) Securities.Add(s);
        _selected = selected; _selection.SecurityId = selected?.Id; Raise(nameof(SelectedSecurity));
        await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        var version = ++_version; var id = SelectedSecurity?.Id; IsLoading = true;
        try
        {
            var snapshot = id is null ? new ResearchSnapshot(null, [], [], []) : await Store.ReadAsync(id.Value);
            if (version != _version) return;
            Snapshot = snapshot; ApplySnapshot();
        }
        finally { if (version == _version) IsLoading = false; }
    }
    protected abstract void ApplySnapshot();
}
public sealed record ResearchSection(string Key, string Title, string Body);
public sealed class ResearchViewModel : SecurityPageViewModel
{
    private ResearchSection? _section;
    private ResearchSource? _source;
    private ResearchScore? _score;
    public ResearchViewModel(ISecurityRepository securities, IResearchStore store, ResearchSelection selection, ResearchDialogs dialogs, IErrorHandler errors, IResearchFileReader files)
        : base("Research", "公司研究", "整理事实、来源与判断。所有编辑均需明确保存，AI 评分尚未启用。", securities, store, selection, dialogs, errors)
    {
        EditSectionCommand = new(async _ => { var id = RequireSecurity(); if (SelectedSection is not null && Dialogs.Show(Dialogs.SectionEditor(id, Snapshot.Research, SelectedSection.Key))) await RefreshAsync(); }, errors);
        AddSourceCommand = new(async _ => { if (Dialogs.Show(Dialogs.SourceEditor(RequireSecurity()))) await RefreshAsync(); }, errors);
        EditSourceCommand = new(async _ => { var id = RequireSecurity(); if (SelectedSource is not null && Dialogs.Show(Dialogs.SourceEditor(id, SelectedSource))) await RefreshAsync(); }, errors);
        ImportSourceCommand = new(async _ =>
        {
            var id = RequireSecurity(); var dialog = new OpenFileDialog { Filter = "研究文件|*.txt;*.md;*.markdown;*.csv;*.pdf", CheckFileExists = true };
            if (dialog.ShowDialog() != true) return;
            Status = "正在提取文本，请稍候…";
            try { var text = await files.ReadAsync(dialog.FileName); if (Dialogs.Show(Dialogs.SourceEditor(id, path: Path.GetFullPath(dialog.FileName), text: text))) await RefreshAsync(); }
            finally { Status = "文件保留原位，提取文本保存到数据库。"; }
        }, errors);
        DeleteSourceCommand = new(async _ => { RequireSecurity(); if (SelectedSource is { } source && Dialogs.Confirm("删除此来源及保存的提取文本？原文件不会删除。")) { await Store.DeleteSourceAsync(source.Id); await RefreshAsync(); } }, errors);
        EditScoreCommand = new(async _ => { var id = RequireSecurity(); if (SelectedScore is not null && Dialogs.Show(Dialogs.ScoreEditor(id, Snapshot.Scores, SelectedScore))) await RefreshAsync(); }, errors);
        EditWeightsCommand = new(async _ => { if (Dialogs.Show(Dialogs.WeightsEditor(RequireSecurity(), Snapshot.Scores))) await RefreshAsync(); }, errors);
    }
    public ObservableCollection<ResearchSection> Sections { get; } = [];
    public ObservableCollection<ResearchSource> Sources { get; } = [];
    public ObservableCollection<ResearchScore> Scores { get; } = [];
    public ResearchSection? SelectedSection { get => _section; set => Set(ref _section, value); }
    public ResearchSource? SelectedSource { get => _source; set => Set(ref _source, value); }
    public ResearchScore? SelectedScore { get => _score; set => Set(ref _score, value); }
    public string ScoreSummary
    {
        get
        {
            var covered = Scores.Where(x => x.EffectiveScore.HasValue).Sum(x => x.Weight);
            var total = Scores.Where(x => x.EffectiveScore.HasValue).Sum(x => x.EffectiveScore!.Value * x.Weight) / 100;
            return covered == 100 ? $"加权总分 {total:N1} / 100。用户评分优先；任何 AI 参考项在表中单独标明。" : $"已评分权重 {covered:N0}% · 已评分项贡献 {total:N1} 分；资料不完整，暂不形成总分。";
        }
    }
    private string _status = "选择章节后点击编辑；来源等级 1 最高、6 最低。";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand EditSectionCommand { get; }
    public AsyncCommand AddSourceCommand { get; }
    public AsyncCommand EditSourceCommand { get; }
    public AsyncCommand ImportSourceCommand { get; }
    public AsyncCommand DeleteSourceCommand { get; }
    public AsyncCommand EditScoreCommand { get; }
    public AsyncCommand EditWeightsCommand { get; }
    protected override void ApplySnapshot()
    {
        var section = SelectedSection?.Key; var source = SelectedSource?.Id; var score = SelectedScore?.Dimension;
        Sections.Clear();
        var labels = new[] { "概览", "商业模式", "行业", "竞争", "管理层", "增长驱动", "催化剂", "风险", "会计备注", "个人笔记" };
        var i = 0; var content = Snapshot.Research?.Content ?? new();
        foreach (var p in typeof(ResearchContent).GetProperties()) Sections.Add(new(p.Name, labels[i++] + " · " + p.Name, (string)p.GetValue(content)!));
        SelectedSection = Sections.FirstOrDefault(x => x.Key == section) ?? Sections.First();
        Sources.Clear(); foreach (var item in Snapshot.Sources) Sources.Add(item); SelectedSource = Sources.FirstOrDefault(x => x.Id == source) ?? Sources.FirstOrDefault();
        Scores.Clear(); foreach (var item in Snapshot.Scores) Scores.Add(item); SelectedScore = Scores.FirstOrDefault(x => x.Dimension == score) ?? Scores.FirstOrDefault();
        Raise(nameof(ScoreSummary));
    }
}
public sealed class SearchViewModel : PageViewModel
{
    private string _query = "";
    private SearchHit? _selected;
    private string _status = "输入至少两个字符，搜索证券、研究正文、来源全文、笔记和观察清单。";
    public SearchViewModel(IResearchStore store, ResearchSelection selection, IErrorHandler errors) : base("Search", "全局搜索", "所有本地研究资料集中检索，结果可直接回到相关证券。")
    {
        SearchCommand = new(async _ => { var query = Query; var rows = await Task.Run(() => store.SearchAsync(query)); Results.Clear(); foreach (var r in rows) Results.Add(r); Selected = Results.FirstOrDefault(); Status = $"找到 {Results.Count} 项 · 查询：{query}"; }, errors);
        OpenResearchCommand = new(_ => { if (Selected is { } item) selection.Open(item.SecurityId, "Research"); });
        OpenFinancialCommand = new(_ => { if (Selected is { } item) selection.Open(item.SecurityId, "Financial"); });
    }
    public string Query { get => _query; set => Set(ref _query, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public SearchHit? Selected { get => _selected; set => Set(ref _selected, value); }
    public ObservableCollection<SearchHit> Results { get; } = [];
    public AsyncCommand SearchCommand { get; }
    public RelayCommand OpenResearchCommand { get; }
    public RelayCommand OpenFinancialCommand { get; }
}
