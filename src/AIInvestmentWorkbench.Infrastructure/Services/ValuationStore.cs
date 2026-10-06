using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Valuation;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed class ValuationStore(IDbContextFactory<InvestmentDbContext> factory, DatabaseWriter writer) : IValuationStore
{
    public async Task<IReadOnlyList<ValuationModelDto>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ValuationModels.AsNoTracking().Include(x => x.Scenarios).Where(x => x.SecurityId == securityId).ToListAsync(ct);
        return rows.OrderBy(x => x.Name).Select(x => new ValuationModelDto(x.Id, x.SecurityId, x.Name, x.ModelType, x.Revision, x.Actuals,
            x.Scenarios.OrderBy(s => s.Kind).Select(s => new ValuationScenarioDto(s.Id, s.Kind, s.AssumptionDate, s.Inputs)).ToArray())).ToArray();
    }
    private static void Check(ValuationModelType type, ValuationActuals a, ValuationInputs inputs)
    {
        ValuationEngine.Validate(type, a, inputs);
        var result = ValuationEngine.Calculate(type, a, inputs);
        if (result.Reverse?.Status == SolveStatus.NumericalFailure) throw new BusinessException(result.Notice);
    }
    public Task<Guid> CreateAsync(Guid securityId, string name, ValuationModelType type, ValuationActuals a, ValuationInputs inputs, DateOnly date, CancellationToken ct = default)
        => writer.ExecuteAsync(async db =>
        {
            await CheckCurrency(db, securityId, a, ct); Check(type, a, inputs);
            var model = new ValuationModel(securityId, name, type, a);
            foreach (var kind in Enum.GetValues<ScenarioKind>()) model.Scenarios.Add(new(model.Id, kind, inputs, date));
            db.ValuationModels.Add(model); return model.Id;
        }, ct);
    public Task UpdateActualsAsync(Guid id, int revision, string name, ValuationActuals a, CancellationToken ct = default)
        => writer.ExecuteAsync(async db =>
        {
            var model = await Get(db, id, revision, ct); await CheckCurrency(db, model.SecurityId, a, ct);
            foreach (var s in model.Scenarios) Check(model.ModelType, a, s.Inputs);
            model.Update(name, a, revision); return true;
        }, ct);
    public Task SaveScenarioAsync(Guid id, int revision, ScenarioKind kind, ValuationInputs inputs, DateOnly date, CancellationToken ct = default)
        => writer.ExecuteAsync(async db =>
        {
            var model = await Get(db, id, revision, ct); Check(model.ModelType, model.Actuals, inputs);
            var scenario = model.Scenarios.SingleOrDefault(x => x.Kind == kind) ?? throw new BusinessException("情景不存在。");
            scenario.Update(inputs, date); model.Touch(revision); return true;
        }, ct);
    public Task CopyBaseAsync(Guid id, int revision, ScenarioKind destination, DateOnly date, CancellationToken ct = default)
        => writer.ExecuteAsync(async db =>
        {
            if (destination is not (ScenarioKind.Bear or ScenarioKind.Bull)) throw new BusinessException("只能复制到 Bear 或 Bull。");
            var model = await Get(db, id, revision, ct);
            model.Scenarios.Single(x => x.Kind == destination).Update(model.Scenarios.Single(x => x.Kind == ScenarioKind.Base).Inputs, date);
            model.Touch(revision); return true;
        }, ct);
    public Task DeleteAsync(Guid id, int revision, CancellationToken ct = default)
        => writer.ExecuteAsync(async db => { var model = await Get(db, id, revision, ct); db.ValuationScenarios.RemoveRange(model.Scenarios); db.ValuationModels.Remove(model); return true; }, ct);
    private static async Task<ValuationModel> Get(InvestmentDbContext db, Guid id, int revision, CancellationToken ct)
    {
        var model = await db.ValuationModels.Include(x => x.Scenarios).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("估值已不存在。");
        model.CheckRevision(revision); return model;
    }
    private static async Task CheckCurrency(InvestmentDbContext db, Guid id, ValuationActuals a, CancellationToken ct)
    {
        var security = await db.Securities.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("证券不存在。");
        if (security.Currency != Guard.Currency(a.Currency)) throw new BusinessException("估值币种须与证券相同，请先把输入数据换算至该币种。");
    }
}
