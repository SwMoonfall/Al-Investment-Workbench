using System.Globalization;
using System.Windows;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class WorkbenchDialogs(PortfolioApplicationService portfolio, SecurityApplicationService securities, IErrorHandler errors)
{
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static FormField F(string key, string label, string value, bool readOnly = false) => new(key, label, value, readOnly: readOnly);
    private static FormField Choice<T>(string key, string label, T value) where T : struct, Enum
        => new(key, label, value.ToString(), Enum.GetValues<T>().Select(v => new FormOption(v.ToString(), v.ToString())).ToArray());
    private static bool Show(EditorViewModel vm)
    {
        var window = new EditorWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
        return window.ShowDialog() == true;
    }
    public EditorViewModel AccountEditor(AccountDto? account, Action<Guid> saved, bool initial = false) => new(
        initial ? "欢迎 · 创建第一个投资账户" : account is null ? "新增账户" : "编辑账户",
        "下列为可修改的示例。权重以百分数填写，四类合计 100%。当前现金由交易计算；修改初始资金会重新核算历史。",
        [F("Name", "账户名称", account?.Name ?? "我的投资账户"), F("Currency", "本位币（RMB 会转为 CNY）", account?.BaseCurrency ?? "CNY", account is not null),
         F("Capital", "初始资金", N(account?.InitialCapital ?? 500000)), F("Etf", "ETF 目标 %", N((account?.TargetEtfWeight ?? .4m)*100)),
         F("Active", "主动投资目标 %", N((account?.TargetActiveWeight ?? .4m)*100)), F("Experimental", "观察 / 试验目标 %", N((account?.TargetExperimentalWeight ?? .1m)*100)),
         F("Cash", "现金目标 %", N((account?.TargetCashWeight ?? .1m)*100)), F("Max", "默认单个仓位上限 %", N((account?.DefaultMaxPositionWeight ?? .1m)*100))],
        async vm => saved(await portfolio.SaveAccountAsync(new(account?.Id, vm.Text("Name"), vm.Text("Currency"), vm.Number("Capital"), vm.Number("Cash")/100,
            vm.Number("Etf")/100, vm.Number("Active")/100, vm.Number("Experimental")/100, vm.Number("Max")/100))), errors);
    public Task<Guid?> EditAccountAsync(AccountDto? account = null, bool initial = false)
    {
        Guid? id = null; Show(AccountEditor(account, value => id = value, initial)); return Task.FromResult(id);
    }
    public EditorViewModel SecurityEditor(Security? security, Action<Guid> saved) => new(security is null ? "新增证券" : "证券详情 / 编辑",
        "交易所与代码共同标识证券。已有投资记录或报价时不能更改币种。ISIN 可留空。",
        [F("Ticker", "证券代码 Ticker", security?.Symbol ?? ""), F("Name", "公司 / 证券名称", security?.Name ?? ""),
         Choice("Market", "市场", security?.Market ?? Market.ChinaA), F("Exchange", "交易所（如 XSHG / XSHE / XHKG / XNAS）", security?.Exchange ?? "XSHG"),
         F("Currency", "币种", security?.Currency ?? "CNY"), Choice("Type", "证券类型（Etf = ETF）", security?.Type ?? SecurityType.Stock),
         F("Sector", "板块 Sector", security?.Sector ?? ""), F("Industry", "行业 Industry", security?.Industry ?? ""), F("Country", "国家 / 地区", security?.Country ?? ""),
         F("ISIN", "ISIN（可选）", security?.ISIN ?? ""), F("Notes", "备注", security?.Notes ?? "")],
        async vm => saved(await securities.SaveAsync(new(security?.Id, vm.Text("Ticker"), vm.Text("Name"), vm.EnumValue<Market>("Market"), vm.Text("Exchange"), vm.Text("Currency"),
            vm.EnumValue<SecurityType>("Type"), vm.Text("Sector"), vm.Text("Industry"), vm.Text("Country"), vm.Text("ISIN"), vm.Text("Notes")))), errors);
    public Task<bool> EditSecurityAsync(Security? security = null) => Task.FromResult(Show(SecurityEditor(security, _ => { })));
    public async Task<EditorViewModel> TransactionEditorAsync(AccountDto account, Guid? selected = null)
    {
        var options = (await securities.SearchAsync()).Where(x => x.Currency == account.BaseCurrency).Select(x => new FormOption($"{x.Symbol} · {x.Name} / {x.Exchange}", x.Id.ToString())).Prepend(new("无证券（现金交易）", "")).ToArray();
        return new("新增交易", "买卖：填写数量、单价，现金金额为 0。出入金/分红/费用：数量和单价为 0，填写现金金额。Fee 附加手续费为 0。按日期、录入顺序重算历史，不支持超卖和透支。",
            [Choice("Type", "交易类型", TransactionType.Buy), new("Security", "证券（仅同币种）", selected?.ToString() ?? "", options),
             F("Date", "交易日期 yyyy-MM-dd", DateTime.Today.ToString("yyyy-MM-dd")), F("Quantity", "数量", "0"), F("Price", "单价", "0"),
             F("Amount", "现金金额（买卖填 0）", "0"), F("Fees", "附加手续费", "0"), F("Currency", "币种", account.BaseCurrency, true), F("Notes", "备注", "")],
            async vm => await portfolio.PostAsync(new(account.Id, Guid.TryParse(vm.Text("Security"), out var id) ? id : null, vm.Date("Date"), vm.EnumValue<TransactionType>("Type"),
                vm.Number("Quantity"), vm.Number("Price"), vm.Number("Amount"), vm.Number("Fees"), vm.Text("Currency"), vm.Text("Notes"))), errors);
    }
    public async Task<bool> AddTransactionAsync(AccountDto account, Guid? selected = null) => Show(await TransactionEditorAsync(account, selected));
    public EditorViewModel PriceEditor(AccountDto account, HoldingDto holding)
    {
        var vm = new EditorViewModel($"价格与仓位规则 · {holding.Ticker}", "报价为人工录入，不代表实时行情。报价影响所有持有该证券的账户。目标与上限只作用于当前账户，不触发交易。",
            [F("Price", "最新价格", N(holding.LatestPrice)), F("Date", "价格日期 yyyy-MM-dd", (holding.PriceDate?.LocalDateTime ?? DateTime.Today).ToString("yyyy-MM-dd")),
             F("Target", "目标权重 %", N(holding.TargetWeight*100)), F("Max", "最大权重 %", N(holding.MaxWeight*100)), Choice("Bucket", "资产分类", holding.Bucket)],
            async f => await portfolio.UpdateHoldingAsync(account.Id, holding.SecurityId, f.Number("Price"), f.Date("Date"), f.Number("Target")/100, f.Number("Max")/100, f.EnumValue<InvestmentBucket>("Bucket")), errors);
        return vm;
    }
    public Task<bool> EditPriceAsync(AccountDto account, HoldingDto holding) => Task.FromResult(Show(PriceEditor(account, holding)));
    public async Task<EditorViewModel> WatchlistEditorAsync(WatchlistDto? item = null)
    {
        var options = (await securities.SearchAsync()).Select(x => new FormOption($"{x.Symbol} · {x.Name} / {x.Exchange}", x.Id.ToString())).ToArray();
        if (options.Length == 0) throw new BusinessException("请先在证券库新增证券。");
        var vm = new EditorViewModel(item is null ? "加入观察清单" : "编辑观察记录", "记录研究阶段、优先级与下一步行动。研究日期可以留空。",
            [new("Security", "证券", item?.SecurityId.ToString() ?? options[0].Value, options, item is not null), Choice("Stage", "研究阶段", item?.Stage ?? WatchlistStage.Candidate),
             Choice("Priority", "优先级", item?.Priority ?? WatchlistPriority.Normal), F("Reason", "观察理由", item?.Reason ?? ""), F("Next", "下一步行动", item?.NextAction ?? ""),
             F("Date", "最近研究日期 yyyy-MM-dd（可选）", item?.LastResearchDate?.LocalDateTime.ToString("yyyy-MM-dd") ?? ""), Choice("Risk", "风险状态", item?.RiskStatus ?? RiskStatus.Unknown), F("Notes", "备注", item?.Notes ?? "")],
            async f => await portfolio.SaveWatchlistAsync(new(Guid.Parse(f.Text("Security")), f.EnumValue<WatchlistStage>("Stage"), f.EnumValue<WatchlistPriority>("Priority"), f.Text("Reason"), f.Text("Next"),
                f.Text("Date") == "" ? null : f.Date("Date"), f.EnumValue<RiskStatus>("Risk"), f.Text("Notes"))), errors);
        return vm;
    }
    public async Task<bool> EditWatchlistAsync(WatchlistDto? item = null) => Show(await WatchlistEditorAsync(item));
    public bool Confirm(string message) => MessageBox.Show(System.Windows.Application.Current.MainWindow, message, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
