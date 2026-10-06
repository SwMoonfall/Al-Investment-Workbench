using System.Collections.ObjectModel;
using System.Globalization;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.ViewModels;

public sealed record FormOption(string Label, string Value)
{
    public override string ToString() => Label;
}
public sealed class FormField(string key, string label, string value, IReadOnlyList<FormOption>? options = null, bool readOnly = false, bool multiline = false) : ObservableObject
{
    private string _value = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Value { get => _value; set => Set(ref _value, value ?? ""); }
    public IReadOnlyList<FormOption> Options { get; } = options ?? [];
    public bool IsChoice => Options.Count > 0;
    public bool IsEditable => !readOnly;
    public bool IsReadOnly => readOnly;
    public bool IsMultiline => multiline;
    public double FieldHeight => multiline ? 180 : 44;
}
public sealed class EditorViewModel : ObservableObject
{
    private string _error = "";
    public EditorViewModel(string title, string hint, IEnumerable<FormField> fields, Func<EditorViewModel, Task> save, IErrorHandler errors)
    {
        Title = title; Hint = hint; Fields = new(fields);
        SaveCommand = new AsyncCommand(async _ =>
        {
            Error = "";
            try { await save(this); CloseRequested?.Invoke(true); }
            catch (Exception ex) when (ex is BusinessException or ArgumentException or FormatException or OverflowException)
            { Error = ex is OverflowException ? "输入金额过大，请检查。" : ex.Message; }
        }, errors);
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(false));
    }
    public string Title { get; }
    public string Hint { get; }
    public ObservableCollection<FormField> Fields { get; }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public AsyncCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public event Action<bool>? CloseRequested;
    public string Text(string key) => Fields.Single(x => x.Key == key).Value.Trim();
    public decimal Number(string key)
        => decimal.TryParse(Text(key), (NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint), CultureInfo.InvariantCulture, out var value) ? value
            : throw new BusinessException($"{Fields.Single(x => x.Key == key).Label} 必须是有效数字。");
    public DateTimeOffset Date(string key)
    {
        if (!DateTime.TryParseExact(Text(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) throw new BusinessException("日期格式应为 yyyy-MM-dd。");
        return new(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }
    public T EnumValue<T>(string key) where T : struct, Enum => Enum.TryParse<T>(Text(key), out var value) && Enum.IsDefined(value)
        ? value : throw new BusinessException("请选择有效选项。");
}


