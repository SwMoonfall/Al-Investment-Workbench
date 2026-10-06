using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Application.Services;

public sealed class SecurityApplicationService(ISecurityRepository repository)
{
    public Task<IReadOnlyList<Security>> SearchAsync(string query = "", CancellationToken ct = default) => repository.SearchAsync(query, ct);
    public Task<Security?> GetAsync(Guid id, CancellationToken ct = default) => repository.GetAsync(id, ct);
    public async Task<Guid> SaveAsync(SecurityDraft draft, CancellationToken ct = default)
    {
        var entity = draft.Id is { } id ? await repository.GetAsync(id, ct) ?? throw new BusinessException("证券已不存在，请刷新。")
            : new Security(draft.Ticker, draft.CompanyName, draft.Exchange, draft.Currency, draft.SecurityType);
        Apply(entity, draft);
        await repository.SaveAsync(entity, draft.Id is null, ct);
        return entity.Id;
    }
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => repository.DeleteAsync(id, ct);
    public static void Apply(Security security, SecurityDraft draft) => security.Edit(draft.Ticker, draft.CompanyName, draft.Exchange,
        draft.Currency, draft.SecurityType, draft.Market, draft.Sector, draft.Industry, draft.Country, draft.ISIN, draft.Notes);
}
