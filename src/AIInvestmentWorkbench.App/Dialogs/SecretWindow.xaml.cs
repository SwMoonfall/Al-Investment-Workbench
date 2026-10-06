using System.Windows;
using AIInvestmentWorkbench.Application.AI;
namespace AIInvestmentWorkbench.App.Dialogs;
public partial class SecretWindow : Window
{
    private readonly ISecretStore _store; private readonly string _scope;
    public SecretWindow(ISecretStore store, string scope) { InitializeComponent(); _store = store; _scope = scope; Endpoint.Text = "仅用于：" + scope; Closed += (_, _) => Secret.Clear(); }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        Save.IsEnabled = false;
        try { await _store.WriteAsync(_scope, Secret.Password); DialogResult = true; }
        catch { Status.Text = "无法保存密钥。请确认输入非空、无换行，且本地目录可写。"; }
        finally { Secret.Clear(); Save.IsEnabled = true; }
    }
}
