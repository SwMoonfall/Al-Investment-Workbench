using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;

namespace AIInvestmentWorkbench.App.ViewModels;
public sealed class WorkspaceContext(PortfolioApplicationService service) : ObservableObject
{
    private IReadOnlyList<AccountDto> _accounts = [];
    private AccountDto? _selected;
    public IReadOnlyList<AccountDto> Accounts { get => _accounts; private set => Set(ref _accounts, value); }
    public AccountDto? SelectedAccount { get => _selected; set { if (Set(ref _selected, value)) AccountChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? AccountChanged;
    public async Task ReloadAsync(Guid? select = null)
    {
        var id = select ?? SelectedAccount?.Id;
        Accounts = await service.AccountsAsync();
        SelectedAccount = Accounts.FirstOrDefault(x => x.Id == id) ?? Accounts.FirstOrDefault();
    }
}
