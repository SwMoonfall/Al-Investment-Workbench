using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Domain.Risk;

public enum DecisionKind { AddPosition, ExitPosition }
public enum DecisionChoice { Add, Hold, Trim, Exit, NoAction }
public enum ChecklistAnswer { Unknown, Yes, No }
public sealed record ChecklistQuestion(string Key, string Text);
public sealed record DecisionAnswer(string Key, ChecklistAnswer Answer);
public static class DecisionRules
{
    public static IReadOnlyList<ChecklistQuestion> Questions(DecisionKind kind) => kind switch
    {
        DecisionKind.AddPosition => [new("ThesisActive", "Thesis 是否 Active？"), new("KillTriggered", "KillCondition 是否 Triggered？"),
            new("FundamentalsImproved", "基本面是否改善？"), new("ValuationImproved", "估值是否改善？"), new("OnlyPriceDecline", "是否只是因为股价下跌？"),
            new("NearMaxWeight", "当前仓位是否接近或超过 MaxWeight？"), new("CorrelationIncreasing", "组合相关性是否上升？（人工判断）"), new("RiskIncreasing", "风险是否明显增加？")],
        DecisionKind.ExitPosition => [new("ThesisInvalidated", "Thesis 是否已被证伪？"), new("Overvalued", "估值是否过高？"), new("ConcentrationHigh", "组合集中度是否过高？"),
            new("BetterOpportunity", "是否有更高优先级机会？"), new("OnlyPriceDecline", "是否只是因为价格短期下跌？")],
        _ => throw new BusinessException("未知决策清单类型。")
    };
    public static void Validate(DecisionKind kind, DecisionChoice choice, IReadOnlyList<DecisionAnswer> answers, string reason)
    {
        var questions = Questions(kind); Guard.Text(reason, "决策理由", 8000);
        if (!Enum.IsDefined(choice) || (kind == DecisionKind.AddPosition ? choice is not (DecisionChoice.Add or DecisionChoice.NoAction) : choice is DecisionChoice.Add))
            throw new BusinessException("清单与最终决策选项不匹配。");
        if (answers.Count != questions.Count || answers.Select(x => x.Key).Distinct().Count() != answers.Count || answers.Any(x => !Enum.IsDefined(x.Answer) || !questions.Any(q => q.Key == x.Key)))
            throw new BusinessException("请逐项回答完整清单，可以明确选择“未知”。");
    }
}
