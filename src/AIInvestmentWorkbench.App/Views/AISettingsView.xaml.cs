using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AIInvestmentWorkbench.App.ViewModels;
namespace AIInvestmentWorkbench.App.Views;
public partial class AISettingsView : UserControl
{
    private AISettingsViewModel? _model;
    private bool _syncing;
    public AISettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        DataContextChanged += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnChanged; _model = null; _syncing = true; Secret.Clear(); _syncing = false; };
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = DataContext as AISettingsViewModel;
        if (_model is not null) _model.PropertyChanged += OnChanged;
        Sync();
    }
    private void Sync() { _syncing = true; Secret.Password = _model?.ApiKey ?? ""; _syncing = false; }
    private void OnChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(AISettingsViewModel.ApiKey)) Sync(); }
    private void OnPasswordChanged(object sender, RoutedEventArgs e) { if (!_syncing && _model is not null) _model.ApiKey = Secret.Password; }
}
