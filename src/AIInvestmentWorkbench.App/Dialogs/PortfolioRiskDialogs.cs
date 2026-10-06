using System.Globalization;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Risk;
namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class PortfolioRiskDialogs(IPortfolioRiskStore store, IErrorHandler errors)
{
    public EditorViewModel TagEditor() => new("创建自定义风险标签", "标签用于暴露归组，不是相关系数。Sector / Market / Currency 显式标签会替代该证券对应资料字段；同类多个标签按完整仓位重复计入各组。", [
        new("Name", "标签名称", ""), new("Category", "分类", "Custom", Enum.GetValues<RiskTagCategory>().Select(x => new FormOption(x.ToString(), x.ToString())).ToArray())
    ], async vm => { await store.CreateTagAsync(vm.Text("Name"), vm.EnumValue<RiskTagCategory>("Category")); }, errors);
    public EditorViewModel ThresholdEditor(decimal value) => new("接近仓位上限的预警阈值", "例如 0.90：达到 MaxWeight 的 90% 时提示接近上限；超过 MaxWeight 则提示超限。此设置不阻止用户记录决策。", [new("Ratio", "接近上限比例（0–1，不含 0）", value.ToString(CultureInfo.InvariantCulture))], vm => store.SetNearMaxRatioAsync(vm.Number("Ratio")), errors);
    public EditorViewModel DecisionEditor(DecisionPreview preview, DecisionKind kind, SizingInputs? sizing = null)
    {
        var c = preview.Context;
        List<FormField> fields = [new("Context", "系统核对 · 保存时会再次校验", ContextText(c), readOnly: true, multiline: true)];
        foreach (var q in DecisionRules.Questions(kind)) fields.Add(new(q.Key, q.Text, "", [new("是", "Yes"), new("否", "No"), new("未知 / 尚未核实", "Unknown")]));
        if (sizing is not null) fields.Add(new("Sizing", "本次仓位规划（目标总仓位，非加仓量）", SizingText(sizing, c), readOnly: true, multiline: true));
        var choices = kind == DecisionKind.AddPosition ? new[] { DecisionChoice.Add, DecisionChoice.NoAction } : [DecisionChoice.Hold, DecisionChoice.Trim, DecisionChoice.Exit, DecisionChoice.NoAction];
        fields.Add(new("Choice", "最终选择（不会自动交易）", "", choices.Select(x => new FormOption(x.ToString(), x.ToString())).ToArray()));
        fields.Add(new("Reason", "决定理由 / 证据；若与系统提示不同，请说明判断依据", "", multiline: true));
        return new(kind == DecisionKind.AddPosition ? "加仓前检查与决策" : "减仓 / 退出检查与决策", "逐项作答，未知可以明确保留。触发条件和仓位警示不会代替你决策。保存会永久保留清单、理由及当时风险快照，不创建交易。", fields,
            async vm => { await store.SaveDecisionAsync(new(c.Portfolio.AccountId, c.SecurityId, kind, vm.EnumValue<DecisionChoice>("Choice"), DecisionRules.Questions(kind).Select(q => new DecisionAnswer(q.Key, vm.EnumValue<ChecklistAnswer>(q.Key))).ToArray(), vm.Text("Reason"), preview.Fingerprint, sizing)); }, errors);
    }
    public static string ContextText(DecisionContext c)
    {
        var p = c.Portfolio;
        return $"{p.AccountName} / {c.Security}\n风险标签：{string.Join(" / ", c.SecurityTags.Select(x => x.Name))}\n当前 Thesis：{(c.CurrentThesis is null ? "无当前逻辑" : c.CurrentThesis.Status + " · " + c.CurrentThesis.Title + " v" + c.CurrentThesis.Version)}；最近逻辑状态：{c.LatestThesisStatus?.ToString() ?? "无记录"}\n" +
            $"开放逻辑触发条件 {c.TriggeredCount} 项；未评估条件 {c.OpenTheses.Sum(x => x.UnknownConditions)} 项（无条件不等于安全）\n" +
            $"当前仓位 {c.CurrentWeight:P2} / MaxWeight {c.MaxWeight:P2} · {WeightText(c.MaxWeightWarning)}；接近阈值 {p.NearMaxRatio:P0}\n" +
            $"组合最大持仓 {p.Exposure.Concentration.LargestPosition:P2} / Top 3 {p.Exposure.Concentration.Top3:P2} / Top 5 {p.Exposure.Concentration.Top5:P2}\n" +
            $"会计 High Attention {c.HighAttention.Count} 项；{c.UnassessedAccountingChecks} 项数据不足；组合成本暂估 {p.EstimatedPriceCount} 项\n" +
            string.Join("\n", c.OpenTheses.SelectMany(t => t.Conditions.Where(k => k.Status == Domain.Enums.KillConditionStatus.Triggered).Select(k => $"Triggered · {t.Title} / {k.Title}：{k.Evidence}"))) +
            "\n" + string.Join("\n", c.HighAttention.Select(x => $"High Attention · {x.PeriodType} {x.Currency} / {x.Rule}：{x.Evidence}"));
    }
    public static string WeightText(WeightWarning value) => value switch { WeightWarning.AboveMaximum => "超出上限", WeightWarning.NearMaximum => "接近上限", _ => "未触及仓位阈值" };
    public static string SizingText(SizingInputs inputs, DecisionContext c)
    {
        var result = PortfolioRiskRules.Size(inputs, c.MaxWeight, c.Portfolio.NearMaxRatio);
        return $"{inputs.BasePosition:P2} × {inputs.ConfidenceFactor} × {inputs.ValuationFactor} × {inputs.RiskFactor} = {result.SuggestedPosition:P2}\n{WeightText(result.Warning)}" +
            (result.AboveFullPortfolio ? "；结果超过总资产 100%，请重新审视参数。" : "") + "\n" + PortfolioRiskRules.SizingNotice;
    }
}

