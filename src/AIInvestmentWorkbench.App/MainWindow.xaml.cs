using System.Windows;
using AIInvestmentWorkbench.App.ViewModels;
namespace AIInvestmentWorkbench.App;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DataContext = viewModel;
        Loaded += (_, _) => { Width = Math.Min(Width, SystemParameters.WorkArea.Width); Height = Math.Min(Height, SystemParameters.WorkArea.Height); ResizeWorkspace(); };
        SizeChanged += (_, _) => ResizeWorkspace();
    }
    private void ResizeWorkspace() { WorkspaceLayout.Width = Math.Max(1040, ActualWidth - 18); WorkspaceLayout.Height = Math.Max(640, ActualHeight - 42); }
}
