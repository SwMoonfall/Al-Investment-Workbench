using System.Text.Json;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Domain.Valuation;
namespace AIInvestmentWorkbench.Domain.Entities;

public sealed class ValuationModel : Entity
{
    private ValuationModel() { }
    public ValuationModel(Guid securityId, string name, ValuationModelType type, ValuationActuals actuals)
    {
        Guard.Id(securityId, "证券"); SecurityId = securityId; ModelType = type;
        if (!Enum.IsDefined(type)) throw new BusinessException("估值模型类型无效。");
        Update(name, actuals, 1); Revision = 1;
    }
    public Guid SecurityId { get; private set; }
    public Security Security { get; private set; } = null!;
    public string Name { get; private set; } = "";
    public ValuationModelType ModelType { get; private set; }
    public int Revision { get; private set; } = 1;
    public decimal Revenue { get; private set; }
    public decimal NetDebt { get; private set; }
    public decimal ShareCount { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public string Currency { get; private set; } = "";
    public DateOnly DataSourceDate { get; private set; }
    public DateOnly PriceDate { get; private set; }
    public string Source { get; private set; } = "";
    public ICollection<ValuationScenario> Scenarios { get; private set; } = new List<ValuationScenario>();
    public ValuationActuals Actuals => new(Revenue, NetDebt, ShareCount, CurrentPrice, Currency, DataSourceDate, PriceDate, Source);
    public void CheckRevision(int expected) { if (Revision != expected) throw new BusinessException("估值已被修改，请刷新后再保存。"); }
    public void Touch(int expected) { CheckRevision(expected); Revision++; MarkUpdated(); }
    public void Update(string name, ValuationActuals a, int expected)
    {
        CheckRevision(expected); ValuationEngine.ValidateActuals(a); Name = Guard.Text(name, "模型名称", 150);
        Revenue = a.Revenue; NetDebt = a.NetDebt; ShareCount = a.ShareCount; CurrentPrice = a.CurrentPrice;
        Currency = Guard.Currency(a.Currency); Source = Guard.Text(a.Source, "数据来源", 1000); DataSourceDate = a.DataSourceDate; PriceDate = a.PriceDate; Touch(expected);
    }
}
public sealed class ValuationScenario : Entity
{
    private ValuationScenario() { }
    public ValuationScenario(Guid modelId, ScenarioKind kind, ValuationInputs inputs, DateOnly assumptionDate)
    {
        Guard.Id(modelId, "估值模型"); if (!Enum.IsDefined(kind)) throw new BusinessException("情景无效。");
        ValuationModelId = modelId; Kind = kind; Update(inputs, assumptionDate);
    }
    public Guid ValuationModelId { get; private set; }
    public ValuationModel ValuationModel { get; private set; } = null!;
    public ScenarioKind Kind { get; private set; }
    public string InputsJson { get; private set; } = "";
    public DateOnly AssumptionDate { get; private set; }
    public ValuationInputs Inputs => JsonSerializer.Deserialize<ValuationInputs>(InputsJson) ?? throw new BusinessException("估值输入无法读取。");
    public void Update(ValuationInputs inputs, DateOnly date)
    {
        if (date == default) throw new BusinessException("请填写假设日期。");
        if (inputs.SchemaVersion != 1) throw new BusinessException("未知估值输入版本。");
        InputsJson = JsonSerializer.Serialize(inputs); AssumptionDate = date; MarkUpdated();
    }
}
