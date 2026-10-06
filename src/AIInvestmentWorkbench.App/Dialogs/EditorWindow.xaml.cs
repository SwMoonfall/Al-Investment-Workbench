using System.Windows;
using AIInvestmentWorkbench.App.ViewModels;
namespace AIInvestmentWorkbench.App.Dialogs;
public partial class EditorWindow : Window
{
    private readonly EditorViewModel _model;
    private bool _allowClose;
    public EditorWindow(EditorViewModel model)
    {
        InitializeComponent(); DataContext = model; _model = model;
        model.CloseRequested += OnClose;
        Closed += (_, _) => model.CloseRequested -= OnClose;
        Closing += (_, e) => { if (!_allowClose && model.SaveCommand.IsExecuting) e.Cancel = true; };
    }
    private void OnClose(bool result)
    {
        if (!result && _model.SaveCommand.IsExecuting) return;
        _allowClose = true; DialogResult = result;
    }
}
