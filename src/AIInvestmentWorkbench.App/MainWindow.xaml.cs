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
        Closing += (_, e) =>
        {
            var ai = viewModel.Navigation.Pages.OfType<AISettingsViewModel>().Single();
            if (ai.HasUnsavedDrafts && MessageBox.Show(this, "AI服务还有未保存的草稿。关闭将丢弃这些更改，是否仍然关闭？", "未保存的 AI 配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
        };
    }
    private void ResizeWorkspace() { WorkspaceLayout.Width = Math.Max(1040, ActualWidth - 18); WorkspaceLayout.Height = Math.Max(640, ActualHeight - 42); }
}
