using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using Microsoft.Win32;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.ViewModels;
public sealed class FieldMapping(string field, bool required, IReadOnlyList<string> headers, Action changed) : ObservableObject
{
    private string _header = headers.FirstOrDefault(h => h.Equals(field, StringComparison.OrdinalIgnoreCase)) ?? "";
    public string Field { get; } = field;
    public string Label => Field + (required ? " *" : "");
    public IReadOnlyList<string> Headers { get; } = new[] { "" }.Concat(headers).ToArray();
    public string Header { get => _header; set { if (Set(ref _header, value ?? "")) changed(); } }
}
public sealed class ImportViewModel : PageViewModel
{
    private readonly CsvImportService _service;
    private readonly WorkspaceContext _workspace;
    private CsvDocument? _document;
    private ImportKind _kind;
    private string _file = "未选择文件", _report = "先选择 UTF-8 CSV 文件。", _status = "1 / 选择文件";
    private DataView? _preview;
    private bool _canImport;
    private bool _busy;
    private int _revision;
    private ObservableCollection<FieldMapping> _mappings = [];
    public ImportViewModel(CsvImportService service, WorkspaceContext workspace, IErrorHandler errors)
        : base("Import", "CSV 导入向导", "预览、映射、整批验证后导入。任何错误都会回滚整批数据。")
    {
        _service = service; _workspace = workspace;
        SelectFileCommand = new AsyncCommand(async _ => { var dialog = new OpenFileDialog { Filter = "CSV 文件 (*.csv)|*.csv" }; if (dialog.ShowDialog() == true) await LoadFileAsync(dialog.FileName); }, errors);
        ValidateCommand = new AsyncCommand(async _ => await ValidateAsync(), errors);
        ImportCommand = new AsyncCommand(async _ => await CommitAsync(), errors);
        SaveReportCommand = new AsyncCommand(async _ => { var dialog = new SaveFileDialog { FileName = "csv-import-report.txt", Filter = "文本 (*.txt)|*.txt" }; if (dialog.ShowDialog() == true) await File.WriteAllTextAsync(dialog.FileName, Report, Encoding.UTF8); }, errors);
        workspace.AccountChanged += (_, _) => Invalidate();
    }
    public IReadOnlyList<ImportKind> Kinds { get; } = Enum.GetValues<ImportKind>();
    public ImportKind Kind { get => _kind; set { if (Set(ref _kind, value)) { BuildMapping(); Raise(nameof(Hint)); } } }
    public string Hint => Kind == ImportKind.Positions ? "Positions：每行生成一笔期初买入；AverageCost 应含历史费用，扣减所选账户现金。不会覆盖现有仓位。证券需事先建立。"
        : Kind == ImportKind.Transactions ? "Transactions：买卖填写 Ticker/Exchange/Quantity/Price；现金流填写 Amount。日期 yyyy-MM-dd。币种必须匹配所选账户。"
        : "Security：Ticker、CompanyName、Market、Exchange、Currency、SecurityType 必填。类型 Stock/Etf/Other；市场 ChinaA/HongKong/US/Other。已有证券不会被覆盖。";
    public string FileName { get => _file; private set => Set(ref _file, value); }
    public string Report { get => _report; private set => Set(ref _report, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public DataView? Preview { get => _preview; private set => Set(ref _preview, value); }
    public bool CanImport { get => _canImport; private set => Set(ref _canImport, value); }
    public bool IsIdle => !_busy;
    private void SetBusy(bool value) { _busy = value; Raise(nameof(IsIdle)); }
    public ObservableCollection<FieldMapping> Mappings { get => _mappings; private set => Set(ref _mappings, value); }
    public AsyncCommand SelectFileCommand { get; }
    public AsyncCommand ValidateCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand SaveReportCommand { get; }
    public async Task LoadFileAsync(string path)
    {
        Invalidate(); _document = null; Preview = null; Mappings = []; FileName = path;
        if (new FileInfo(path).Length > 5_000_000) throw new BusinessException("文件超过 5 MB，请拆分。");
        string text;
        try { text = await File.ReadAllTextAsync(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException) { throw new BusinessException("文件不是 UTF-8 编码，请先另存为 UTF-8 CSV。"); }
        _document = CsvImportService.Parse(text);
        var table = new DataTable();
        foreach (var header in _document.Headers) table.Columns.Add(header);
        foreach (var row in _document.Rows.Take(100)) table.Rows.Add(row.Cast<object>().ToArray());
        Preview = table.DefaultView; BuildMapping();
        Report = $"共 {_document.Rows.Count} 条记录，预览前 100 条。请核对字段映射并验证。";
    }
    private void BuildMapping()
    {
        Invalidate();
        Mappings = _document is null ? [] : new(CsvImportService.Fields(Kind).Select(f => new FieldMapping(f, CsvImportService.Required(Kind, f), _document.Headers, Invalidate)));
    }
    private void Invalidate() { _revision++; CanImport = false; Status = _document is null ? "1 / 选择文件" : "2 / 预览与字段映射"; }
    private Task<ImportOutcome> ExecuteAsync(bool validateOnly)
    {
        if (_document is null) throw new BusinessException("请先选择 CSV。");
        var document = _document; var kind = Kind; var accountId = _workspace.SelectedAccount?.Id ?? Guid.Empty;
        var mapping = Mappings.ToDictionary(x => x.Field, x => x.Header);
        // Microsoft.Data.Sqlite performs synchronous I/O internally. Keep long imports off the dispatcher.
        return Task.Run(() => _service.ExecuteAsync(document, kind, accountId, mapping, validateOnly));
    }
    public async Task ValidateAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var revision = _revision;
            CanImport = false; Status = "正在验证全部记录…";
            var result = await ExecuteAsync(true);
            if (revision != _revision) { Report = "账户或映射已改变，请重新验证。"; return; }
            CanImport = result.Success; Status = result.Success ? "3 / 验证通过，可以导入" : "3 / 验证失败";
            Report = result.Success ? $"{result.RowCount} 条记录全部验证通过；尚未保存。" : "整批未导入：\n" + string.Join("\n", result.Errors);
        }
        finally { SetBusy(false); }
    }
    public async Task CommitAsync()
    {
        if (_busy) return;
        if (!CanImport) throw new BusinessException("请先验证当前文件和映射。");
        SetBusy(true);
        try
        {
            var destination = Kind == ImportKind.Security ? "证券库" : _workspace.SelectedAccount?.Name ?? "未选择账户";
            CanImport = false; Status = "正在导入，请稍候…";
            var result = await ExecuteAsync(false);
            Report = result.Success ? $"成功导入 {result.RowCount} 条记录到 {destination}。所有记录在同一数据库事务中提交。" : "整批未导入：\n" + string.Join("\n", result.Errors);
            if (result.Success) await _workspace.ReloadAsync();
            Status = result.Success ? "4 / 导入完成" : "4 / 导入失败，已回滚";
        }
        finally { SetBusy(false); }
    }
}
