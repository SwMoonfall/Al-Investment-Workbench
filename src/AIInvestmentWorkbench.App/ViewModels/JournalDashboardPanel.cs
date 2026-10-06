using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.Application.Interfaces;
namespace AIInvestmentWorkbench.App.ViewModels;
public sealed class JournalDashboardPanel(IJournalReviewReader reader, ResearchSelection selection) : ObservableObject
{
    private JournalDashboard _data = new([], [], [], []); private long _generation;
    public RelayCommand OpenCommand { get; } = new(p => { selection.QuarterlyReviewAnchor = (p as QuarterlyDueItem)?.PeriodEnd; selection.Open(p is QuarterlyDueItem due ? due.SecurityId : Guid.Empty, "Journal"); });
    public IReadOnlyList<QuarterlyDueItem> Due => _data.Due;
    public string Summary => $"待季度复盘 {_data.Due.Count} 项（最近已结束自然季度） · 当前 Thesis Warnings {_data.ThesisWarnings.Count} 条";
    public string Warnings => _data.ThesisWarnings.Count == 0 ? "暂无开放逻辑 Warning。" : string.Join("\n", _data.ThesisWarnings.Take(6));
    public string RecentJournal => _data.RecentJournals.Count == 0 ? "尚无 Journal。" : string.Join("\n", _data.RecentJournals.Select(x => x.Label));
    public string RecentAI => _data.RecentAI.Count == 0 ? "尚无当前账户的 AI 复盘。" : string.Join("\n", _data.RecentAI.Select(x => $"{x.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {x.AnalysisType} · {x.Status}"));
    public async Task LoadAsync(Guid account)
    { var generation = ++_generation; var data = await Task.Run(() => reader.DashboardAsync(account, DateOnly.FromDateTime(DateTime.Today))); if (generation != _generation) return; _data = data; Raise(nameof(Due)); Raise(nameof(Summary)); Raise(nameof(Warnings)); Raise(nameof(RecentJournal)); Raise(nameof(RecentAI)); }
}

