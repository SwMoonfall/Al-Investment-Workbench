using AIInvestmentWorkbench.Domain.Valuation;
namespace AIInvestmentWorkbench.Application.Interfaces;

public sealed record ValuationScenarioDto(Guid Id, ScenarioKind Kind, DateOnly AssumptionDate, ValuationInputs Inputs);
public sealed record ValuationModelDto(Guid Id, Guid SecurityId, string Name, ValuationModelType ModelType, int Revision,
    ValuationActuals Actuals, IReadOnlyList<ValuationScenarioDto> Scenarios);
public interface IValuationStore
{
    Task<IReadOnlyList<ValuationModelDto>> ListAsync(Guid securityId, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid securityId, string name, ValuationModelType type, ValuationActuals actuals, ValuationInputs inputs, DateOnly date, CancellationToken ct = default);
    Task UpdateActualsAsync(Guid id, int revision, string name, ValuationActuals actuals, CancellationToken ct = default);
    Task SaveScenarioAsync(Guid id, int revision, ScenarioKind kind, ValuationInputs inputs, DateOnly date, CancellationToken ct = default);
    Task CopyBaseAsync(Guid id, int revision, ScenarioKind destination, DateOnly date, CancellationToken ct = default);
    Task DeleteAsync(Guid id, int revision, CancellationToken ct = default);
}
