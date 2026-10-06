using System.Collections.ObjectModel;
using System.Text;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class ThesisViewModel : PageViewModel
{
    private readonly ISecurityRepository _securities;
    private readonly IThesisReader _reader;
    private readonly ResearchSelection _selection;
    private readonly IErrorHandler _errors;
    private Security? _security;
    private ThesisDetail? _selected;
    private ThesisHistoryEntry? _history;
    private AssumptionDto? _assumption;
    private KillDto? _kill;
    private long _generation, _historyGeneration;
    private bool _busy;
    public ThesisViewModel(ISecurityRepository securities, IThesisReader reader, ResearchSelection selection, ThesisDialogs dialogs, ResearchDialogs windows, IErrorHandler errors)
        : base("Thesis", "投资逻辑 · Thesis", "提出判断，定义反证，持续复盘。条件触发不等于系统替你决定失效。")
    {
        _securities = securities; _reader = reader; _selection = selection; _errors = errors;
        ReloadCommand = new(async _ => await LoadAsync(), errors);
        CreateCommand = new(async _ => { Guid? created = null; if (_security is null) throw new BusinessException("请先选择证券。"); if (windows.Show(dialogs.ThesisEditor(_security.Id, null, id => created = id))) await ReloadTheses(created); }, errors);
        EditCommand = Command(t => dialogs.ThesisEditor(t.SecurityId, t));
        StatusCommand = Command(dialogs.StatusEditor); ReviewCommand = Command(dialogs.ReviewEditor); ArchiveCommand = Command(dialogs.ArchiveEditor);
        AddAssumptionCommand = Command(t => dialogs.AssumptionEditor(t));
        EditAssumptionCommand = Command(t => dialogs.AssumptionEditor(t, SelectedAssumption ?? throw new BusinessException("请选择假设。")));
        DeleteAssumptionCommand = Command(t => dialogs.DeleteEditor(t, SelectedAssumption?.Id ?? throw new BusinessException("请选择假设。"), false));
        AddKillCommand = Command(t => dialogs.KillEditor(t));
        EditKillCommand = Command(t => dialogs.KillEditor(t, SelectedKill ?? throw new BusinessException("请选择条件。")));
        DeleteKillCommand = Command(t => dialogs.DeleteEditor(t, SelectedKill?.Id ?? throw new BusinessException("请选择条件。"), true));
        AsyncCommand Command(Func<ThesisDetail, EditorViewModel> editor) => new(async _ => { var t = RequireSelected(); if (windows.Show(editor(t))) await ReloadTheses(t.Id); }, errors);
    }
    public ObservableCollection<Security> Securities { get; } = [];
    public ObservableCollection<ThesisDetail> Theses { get; } = [];
    public ObservableCollection<ThesisHistoryEntry> History { get; } = [];
    public Task HistoryLoadTask { get; private set; } = Task.CompletedTask;
    public Security? SelectedSecurity { get => _security; set { if (Set(ref _security, value)) { _selection.SecurityId = value?.Id; RefreshSelection(); } } }
    public ThesisDetail? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            SelectedAssumption = value?.Assumptions.FirstOrDefault(); SelectedKill = value?.KillConditions.FirstOrDefault();
            Raise(nameof(SelectedAssumption)); Raise(nameof(SelectedKill)); Raise(nameof(CurrentText)); Raise(nameof(ReviewNotice)); Raise(nameof(HasThesis));
            Raise(nameof(CanEdit)); Raise(nameof(CanChangeStatus)); Raise(nameof(CanArchive));
            HistoryLoadTask = LoadHistoryAsync();
        }
    }
    public AssumptionDto? SelectedAssumption { get => _assumption; set => Set(ref _assumption, value); }
    public KillDto? SelectedKill { get => _kill; set => Set(ref _kill, value); }
    public ThesisHistoryEntry? SelectedHistory { get => _history; set { if (Set(ref _history, value)) Raise(nameof(HistoryText)); } }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(HasThesis)); Raise(nameof(CanEdit)); Raise(nameof(CanChangeStatus)); Raise(nameof(CanArchive)); } }
    public bool HasThesis => Selected is not null && !IsBusy;
    public bool CanEdit => HasThesis && !Selected!.Archived && Selected.Status is not (Domain.Enums.ThesisStatus.Invalidated or Domain.Enums.ThesisStatus.Closed);
    public bool CanChangeStatus => HasThesis && !Selected!.Archived && Selected.Status != Domain.Enums.ThesisStatus.Closed;
    public bool CanArchive => HasThesis && !Selected!.Archived && Selected.Status is Domain.Enums.ThesisStatus.Invalidated or Domain.Enums.ThesisStatus.Closed;
    public string CurrentText => Format(Selected);
    public string HistoryText => Format(SelectedHistory?.Snapshot);
    public string ReviewNotice => Selected is null ? "尚无逻辑。点击新建，填写观点和可证伪条件。" : Selected.Status is Domain.Enums.ThesisStatus.Invalidated or Domain.Enums.ThesisStatus.Closed ? "已结束 · 仅保留历史" : ThesisRules.ReviewDue(Selected.ReviewDate, DateOnly.FromDateTime(DateTime.Today), Selected.Status, Selected.Archived) ? $"需要复盘 · 计划 {Selected.ReviewDate:yyyy-MM-dd}" : Selected.ReviewDate.HasValue ? $"计划复盘 {Selected.ReviewDate:yyyy-MM-dd}" : "尚未安排复盘日期";
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand CreateCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand StatusCommand { get; }
    public AsyncCommand ReviewCommand { get; }
    public AsyncCommand ArchiveCommand { get; }
    public AsyncCommand AddAssumptionCommand { get; }
    public AsyncCommand EditAssumptionCommand { get; }
    public AsyncCommand DeleteAssumptionCommand { get; }
    public AsyncCommand AddKillCommand { get; }
    public AsyncCommand EditKillCommand { get; }
    public AsyncCommand DeleteKillCommand { get; }
    public override async Task LoadAsync()
    {
        var rows = await _securities.SearchAsync(""); var id = _selection.SecurityId;
        _security = null; Securities.Clear(); foreach (var row in rows) Securities.Add(row);
        _security = Securities.FirstOrDefault(x => x.Id == id) ?? Securities.FirstOrDefault(); _selection.SecurityId = _security?.Id; Raise(nameof(SelectedSecurity));
        await ReloadTheses(_selection.ThesisId); _selection.ThesisId = null;
    }
    private async void RefreshSelection() { try { await ReloadTheses(); } catch (Exception e) { _errors.Report(e, "无法载入投资逻辑，请刷新重试。"); } }
    public async Task ReloadTheses(Guid? prefer = null)
    {
        var generation = ++_generation; IsBusy = true;
        try
        {
            var rows = _security is null ? [] : await _reader.ListAsync(_security.Id);
            if (generation != _generation) return;
            Theses.Clear(); foreach (var row in rows) Theses.Add(row);
            Selected = Theses.FirstOrDefault(x => x.Id == prefer) ?? Theses.FirstOrDefault();
        }
        finally { if (generation == _generation) IsBusy = false; }
    }
    private async Task LoadHistoryAsync()
    {
        var generation = ++_historyGeneration; var id = Selected?.Id; History.Clear(); SelectedHistory = null;
        try
        {
            if (id is null) return;
            var rows = await _reader.HistoryAsync(id.Value);
            if (generation != _historyGeneration) return;
            foreach (var row in rows) History.Add(row);
            SelectedHistory = History.Skip(1).FirstOrDefault() ?? History.FirstOrDefault();
        }
        catch (Exception e) { _errors.Report(e, "无法读取历史版本，请刷新重试。"); }
    }
    private ThesisDetail RequireSelected() => HasThesis ? Selected! : throw new BusinessException("请选择已载入的 Thesis。");
    public static string Format(ThesisDetail? t)
    {
        if (t is null) return "";
        var b = new StringBuilder($"{t.SecurityName}\n{t.Label}\n建立：{t.CreatedDate:yyyy-MM-dd HH:mm} UTC\n修改：{t.UpdatedAt:yyyy-MM-dd HH:mm} UTC\n复盘：{t.ReviewDate:yyyy-MM-dd}\n\n");
        foreach (var p in typeof(ThesisContent).GetProperties()) b.AppendLine(p.Name).AppendLine((string?)p.GetValue(t.Content)).AppendLine();
        b.AppendLine("ASSUMPTIONS");
        foreach (var a in t.Assumptions) b.AppendLine($"• {a.Content.Description}\n{a.Content.Metric} | {a.Content.Direction} | 基线 {a.Content.Baseline} → 预期 {a.Content.ExpectedValue} | 当前 {a.Content.CurrentValue} {a.Content.Unit}\nWarning {a.Content.WarningThreshold} / Kill {a.Content.KillThreshold} | {a.Status}\n证据：{a.Content.Evidence}\n复核：{a.LastReviewedAt:O}\n");
        b.AppendLine("KILL CONDITIONS · 全部为关键条件");
        foreach (var k in t.KillConditions) b.AppendLine($"• {k.Content.Title}\n{k.Content.Description}\n{k.Content.Metric} | {k.Content.Direction} | 当前 {k.Content.CurrentValue} {k.Content.Unit}\nWarning {k.Content.WarningThreshold} / Trigger {k.Content.TriggerThreshold} | {k.Assessment}\n证据：{k.Content.Evidence}\n创建：{k.CreatedAt:O} | 复核：{k.LastReviewedAt:O}\n");
        return b.ToString();
    }
}
public sealed class ThesisReviewPanel(IThesisReader reader, ResearchSelection selection) : ObservableObject
{
    private long _generation;
    private IReadOnlyList<ThesisIndicator> _items = [];
    public IReadOnlyList<ThesisIndicator> Items { get => _items; private set { Set(ref _items, value); Raise(nameof(Empty)); } }
    public bool Empty => Items.Count == 0;
    public RelayCommand OpenCommand { get; } = new(p => { if (p is ThesisIndicator item) { selection.ThesisId = item.Id; selection.Open(item.SecurityId, "Thesis"); } });
    public async Task LoadAsync(IEnumerable<Guid>? securities = null, bool onlyDue = false)
    {
        var generation = ++_generation;
        var ids = securities?.ToHashSet();
        var rows = await reader.IndicatorsAsync(DateOnly.FromDateTime(DateTime.Today));
        if (generation != _generation) return;
        Items = rows.Where(x => (ids is null || ids.Contains(x.SecurityId)) && (!onlyDue || x.NeedsReview)).ToList();
    }
}
