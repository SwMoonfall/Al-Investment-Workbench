using AIInvestmentWorkbench.Domain.Enums;
namespace AIInvestmentWorkbench.Domain.Rules;

public static class ThesisRules
{
    public static void RequireUser(ThesisActor actor)
    {
        if (actor != ThesisActor.HumanUser) throw new BusinessException("投资逻辑和触发条件只能由用户修改；AI 或系统只能提供建议。");
    }
    public static void ValidateThresholds(ThresholdDirection direction, decimal warning, decimal trigger)
    {
        if (!Enum.IsDefined(direction) || (direction == ThresholdDirection.AtOrAbove ? warning > trigger : warning < trigger))
            throw new BusinessException("阈值顺序无效：上穿时 Warning ≤ Trigger；下穿时 Warning ≥ Trigger。边界按包含等号判断。");
    }
    public static AssumptionStatus Evaluate(decimal? value, ThresholdDirection direction, decimal warning, decimal trigger)
    {
        ValidateThresholds(direction, warning, trigger);
        if (value is null) return AssumptionStatus.Unknown;
        bool Reached(decimal threshold) => direction == ThresholdDirection.AtOrAbove ? value >= threshold : value <= threshold;
        return Reached(trigger) ? AssumptionStatus.Triggered : Reached(warning) ? AssumptionStatus.Warning : AssumptionStatus.Normal;
    }
    public static bool ReviewDue(DateOnly? reviewDate, DateOnly today, ThesisStatus status, bool archived)
        => !archived && status is not (ThesisStatus.Invalidated or ThesisStatus.Closed) && reviewDate.HasValue && reviewDate <= today;
}
