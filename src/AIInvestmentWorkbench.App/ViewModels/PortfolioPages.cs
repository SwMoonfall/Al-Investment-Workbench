using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.ViewModels;

public abstract class PortfolioPageViewModel(string key, string title, string subtitle, PortfolioApplicationService service, WorkspaceContext workspace)
    : PageViewModel(key, title, subtitle)
{
    private PortfolioSnapshot _snapshot = PortfolioSnapshot.Empty;
    public PortfolioSnapshot Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool HasAccount => workspace.SelectedAccount is not null;
    public override async Task LoadAsync()
    {
        var id = workspace.SelectedAccount?.Id ?? Guid.Empty;
        var snapshot = await Task.Run(() => service.ReadAsync(id));
        if (id != (workspace.SelectedAccount?.Id ?? Guid.Empty)) return;
        Snapshot = snapshot; Raise(nameof(HasAccount)); OnSnapshotChanged();
    }
    protected virtual void OnSnapshotChanged() { }
}
public sealed class DashboardViewModel(PortfolioApplicationService service, WorkspaceContext workspace, IThesisReader thesisReader, ResearchSelection selection, IJournalReviewReader journalReader)
    : PortfolioPageViewModel("Dashboard", "研究总览", "资产、现金与研究进度，来自你的本地记录。", service, workspace)
{
    public ThesisReviewPanel ThesisPanel { get; } = new(thesisReader, selection);
    public JournalDashboardPanel JournalPanel { get; } = new(journalReader, selection);
    public override async Task LoadAsync() { await base.LoadAsync(); await ThesisPanel.LoadAsync(onlyDue: true); await JournalPanel.LoadAsync(Snapshot.Account?.Id ?? Guid.Empty); }
    public IReadOnlyList<HoldingDto> TopPositions => Snapshot.Holdings.Take(5).ToArray();
    protected override void OnSnapshotChanged() => Raise(nameof(TopPositions));
}
public sealed class PortfolioViewModel : PortfolioPageViewModel
{
    private string _search = "", _filter = "全部";
    private ICollectionView _holdings = CollectionViewSource.GetDefaultView(new ObservableCollection<HoldingDto>());
    private HoldingDto? _selected;
    public PortfolioViewModel(PortfolioApplicationService service, SecurityApplicationService securities, WorkspaceContext workspace, WorkbenchDialogs dialogs, IErrorHandler errors, IThesisReader thesisReader, ResearchSelection selection)
        : base("Portfolio", "投资组合", "交易形成持仓；手工报价始终带有日期。点击表头排序。", service, workspace)
    {
        ThesisPanel = new(thesisReader, selection);
        NewAccountCommand = new AsyncCommand(async _ => { if (await dialogs.EditAccountAsync() is { } id) { await workspace.ReloadAsync(id); await LoadAsync(); } }, errors);
        EditAccountCommand = new AsyncCommand(async _ => { if (await dialogs.EditAccountAsync(RequireAccount()) is { } id) { await workspace.ReloadAsync(id); await LoadAsync(); } }, errors);
        AddTransactionCommand = new AsyncCommand(async _ => { if (await dialogs.AddTransactionAsync(RequireAccount(), SelectedHolding?.SecurityId)) { await workspace.ReloadAsync(); await LoadAsync(); } }, errors);
        EditPriceCommand = new AsyncCommand(async _ => { if (await dialogs.EditPriceAsync(RequireAccount(), RequireHolding())) await LoadAsync(); }, errors);
        ViewSecurityCommand = new AsyncCommand(async _ => { var security = await securities.GetAsync(RequireHolding().SecurityId); if (await dialogs.EditSecurityAsync(security)) await LoadAsync(); }, errors);
        RiskCommand = new RelayCommand(_ => selection.Open(SelectedHolding?.SecurityId ?? Guid.Empty, "Risk"));
        AccountDto RequireAccount() => workspace.SelectedAccount ?? throw new BusinessException("请先创建账户。");
    }
    private HoldingDto RequireHolding() => SelectedHolding ?? throw new BusinessException("请先选中一条持仓。");
    public ThesisReviewPanel ThesisPanel { get; }
    public override async Task LoadAsync() { await base.LoadAsync(); await ThesisPanel.LoadAsync(Snapshot.Holdings.Select(x => x.SecurityId)); }
    public IReadOnlyList<string> Filters { get; } = ["全部", "Stock", "Etf", "Other", "超出上限"];
    public string Search { get => _search; set { if (Set(ref _search, value)) Holdings.Refresh(); } }
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) Holdings.Refresh(); } }
    public ICollectionView Holdings { get => _holdings; private set => Set(ref _holdings, value); }
    public HoldingDto? SelectedHolding { get => _selected; set => Set(ref _selected, value); }
    public AsyncCommand NewAccountCommand { get; }
    public AsyncCommand EditAccountCommand { get; }
    public AsyncCommand AddTransactionCommand { get; }
    public AsyncCommand EditPriceCommand { get; }
    public AsyncCommand ViewSecurityCommand { get; }
    public RelayCommand RiskCommand { get; }
    protected override void OnSnapshotChanged()
    {
        var selectedId = SelectedHolding?.SecurityId;
        Holdings = CollectionViewSource.GetDefaultView(new ObservableCollection<HoldingDto>(Snapshot.Holdings));
        Holdings.Filter = item => item is HoldingDto p && (p.Ticker + " " + p.CompanyName).Contains(Search, StringComparison.OrdinalIgnoreCase)
            && (Filter == "全部" || p.SecurityType.ToString() == Filter || (Filter == "超出上限" && p.Weight > p.MaxWeight));
        SelectedHolding = Snapshot.Holdings.FirstOrDefault(x => x.SecurityId == selectedId);
    }
}
public sealed class SecuritiesViewModel : PageViewModel
{
    private readonly SecurityApplicationService _service;
    private string _search = "", _filter = "全部";
    private ICollectionView _rows = CollectionViewSource.GetDefaultView(new ObservableCollection<Security>());
    private Security? _selected;
    public SecuritiesViewModel(SecurityApplicationService service, WorkbenchDialogs dialogs, IErrorHandler errors)
        : base("Securities", "证券库", "维护证券资料与研究背景。选中记录可查看详情、编辑或删除。")
    {
        _service = service;
        AddCommand = new AsyncCommand(async _ => { if (await dialogs.EditSecurityAsync()) await LoadAsync(); }, errors);
        EditCommand = new AsyncCommand(async _ => { if (await dialogs.EditSecurityAsync(RequireSelected())) await LoadAsync(); }, errors);
        DeleteCommand = new AsyncCommand(async _ =>
        {
            var item = RequireSelected();
            if (dialogs.Confirm($"删除 {item.Symbol} {item.Name}？有关联投资记录时将阻止删除。")) { await service.DeleteAsync(item.Id); await LoadAsync(); }
        }, errors);
    }
    private Security RequireSelected() => Selected ?? throw new BusinessException("请先选中证券。");
    public IReadOnlyList<string> Filters { get; } = ["全部", "ChinaA", "HongKong", "US", "Other"];
    public string Search { get => _search; set { if (Set(ref _search, value)) Rows.Refresh(); } }
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) Rows.Refresh(); } }
    public ICollectionView Rows { get => _rows; private set => Set(ref _rows, value); }
    public Security? Selected { get => _selected; set => Set(ref _selected, value); }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public override async Task LoadAsync()
    {
        Rows = CollectionViewSource.GetDefaultView(new ObservableCollection<Security>(await _service.SearchAsync()));
        Rows.Filter = item => item is Security s && string.Join(" ", s.Symbol, s.Name, s.Exchange, s.Sector, s.Industry, s.ISIN).Contains(Search, StringComparison.OrdinalIgnoreCase)
            && (Filter == "全部" || s.Market.ToString() == Filter);
        Selected = null;
    }
}
public sealed class WatchlistViewModel : PageViewModel
{
    private readonly PortfolioApplicationService _service;
    private string _search = "", _filter = "全部";
    private ICollectionView _rows = CollectionViewSource.GetDefaultView(new ObservableCollection<WatchlistDto>());
    private WatchlistDto? _selected;
    public WatchlistViewModel(PortfolioApplicationService service, WorkbenchDialogs dialogs, IErrorHandler errors, IThesisReader thesisReader, ResearchSelection selection)
        : base("Watchlist", "观察清单", "记录每项研究所处阶段，以及下一步准备做什么。")
    {
        _service = service;
        ThesisPanel = new(thesisReader, selection);
        AddCommand = new AsyncCommand(async _ => { if (await dialogs.EditWatchlistAsync()) await LoadAsync(); }, errors);
        EditCommand = new AsyncCommand(async _ => { if (await dialogs.EditWatchlistAsync(RequireSelected())) await LoadAsync(); }, errors);
        RemoveCommand = new AsyncCommand(async _ => { var item = RequireSelected(); if (dialogs.Confirm($"从观察清单移除 {item.Ticker}？证券和投资历史将保留。")) { await service.RemoveWatchlistAsync(item.SecurityId); await LoadAsync(); } }, errors);
    }
    private WatchlistDto RequireSelected() => Selected ?? throw new BusinessException("请先选中观察记录。");
    public ThesisReviewPanel ThesisPanel { get; }
    public IReadOnlyList<string> Filters { get; } = new[] { "全部" }.Concat(Enum.GetNames<Domain.Enums.WatchlistStage>()).ToArray();
    public string Search { get => _search; set { if (Set(ref _search, value)) Rows.Refresh(); } }
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) Rows.Refresh(); } }
    public ICollectionView Rows { get => _rows; private set => Set(ref _rows, value); }
    public WatchlistDto? Selected { get => _selected; set => Set(ref _selected, value); }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public override async Task LoadAsync()
    {
        Rows = CollectionViewSource.GetDefaultView(new ObservableCollection<WatchlistDto>(await _service.WatchlistAsync()));
        Rows.Filter = item => item is WatchlistDto w && string.Join(" ", w.Ticker, w.CompanyName, w.Reason, w.Notes).Contains(Search, StringComparison.OrdinalIgnoreCase) && (Filter == "全部" || w.Stage.ToString() == Filter);
        Rows.SortDescriptions.Add(new(nameof(WatchlistDto.Priority), ListSortDirection.Descending));
        Selected = null;
        await ThesisPanel.LoadAsync(Rows.SourceCollection.Cast<WatchlistDto>().Select(x => x.SecurityId));
    }
}



