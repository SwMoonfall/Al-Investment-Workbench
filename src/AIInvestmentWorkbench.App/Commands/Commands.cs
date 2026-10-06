using System.Windows.Input;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.Commands;

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public sealed class AsyncCommand(Func<object?, Task> execute, IErrorHandler errors) : ICommand
{
    private bool _executing;
    public bool IsExecuting => _executing;
    public bool CanExecute(object? parameter) => !_executing;
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (_executing) return;
        _executing = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(parameter); }
        catch (Exception exception) { errors.Report(exception, exception is BusinessException or ArgumentException ? exception.Message : "操作未完成，请重试。若问题持续，请查看本地日志。"); }
        finally { _executing = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
