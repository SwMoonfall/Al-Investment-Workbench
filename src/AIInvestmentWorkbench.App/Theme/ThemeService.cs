using System.Windows;
using AIInvestmentWorkbench.Application.Interfaces;

namespace AIInvestmentWorkbench.App.Theme;

public sealed class ThemeService
{
    private ResourceDictionary? _active;
    public AppTheme Current { get; private set; } = AppTheme.Light;
    public void Apply(AppTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        var resources = System.Windows.Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = new Uri($"Theme/{theme}.xaml", UriKind.Relative) };
        if (_active is not null) resources.Remove(_active);
        resources.Add(next);
        _active = next; Current = theme;
    }
}
