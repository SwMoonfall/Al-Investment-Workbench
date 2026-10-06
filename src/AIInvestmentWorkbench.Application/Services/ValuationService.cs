using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Valuation;
namespace AIInvestmentWorkbench.Application.Services;

public sealed record ScenarioCalculation(ValuationScenarioDto Scenario, ValuationResult Result);
public sealed class ValuationService
{
    public IReadOnlyList<ScenarioCalculation> Compare(ValuationModelDto model)
        => model.Scenarios.OrderBy(x => x.Kind).Select(x => new ScenarioCalculation(x, ValuationEngine.Calculate(model.ModelType, model.Actuals, x.Inputs))).ToArray();
}
