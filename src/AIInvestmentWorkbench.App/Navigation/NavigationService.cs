using AIInvestmentWorkbench.App.ViewModels;

namespace AIInvestmentWorkbench.App.Navigation;

public sealed class NavigationService(IEnumerable<PageViewModel> pages) : ObservableObject
{
    public IReadOnlyList<PageViewModel> Pages { get; } = pages.ToArray();
    private PageViewModel? _current;
    public PageViewModel? Current { get => _current; private set => Set(ref _current, value); }
    public void Navigate(string key) => Current = Pages.Single(x => x.Key == key);
}
