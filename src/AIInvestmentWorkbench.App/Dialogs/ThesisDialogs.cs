using System.Globalization;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class ThesisDialogs(IThesisUserCommands commands, IErrorHandler errors)
{
    private const ThesisActor User = ThesisActor.HumanUser;
    private static FormField F(string key, string label, string text = "", bool multiline = false) => new(key, label, text, multiline: multiline);
    private static string N(decimal? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static decimal? Optional(EditorViewModel vm, string key) => vm.Text(key).Length == 0 ? null : vm.Number(key);
    private static DateOnly? Date(EditorViewModel vm, string key) => vm.Text(key).Length == 0 ? null : DateOnly.TryParseExact(vm.Text(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new BusinessException("日期格式必须为 yyyy-MM-dd。");
    private static string Reason(EditorViewModel vm) => Guard.Text(vm.Text("Reason"), "修改/复盘依据", 1500);
    private static FormField Direction(ThresholdDirection value) => new("Direction", "触发方向（包含等号）", value.ToString(), [new("上穿：当前值 ≥ 阈值", "AtOrAbove"), new("下穿：当前值 ≤ 阈值", "AtOrBelow")]);
    public EditorViewModel ThesisEditor(Guid securityId, ThesisDetail? thesis, Action<Guid>? created = null)
    {
        var c = thesis?.Content ?? new ThesisContent("", "");
        return new(thesis is null ? "建立投资逻辑" : "编辑投资逻辑", "明确可验证的事实与反证。保存后生成完整历史快照；取消不改变已保存版本。", [
            F("Title", "标题", c.Title), F("Summary", "Investment Summary · 投资逻辑", c.InvestmentSummary, true),
            F("Market", "Why Market May Be Wrong · 市场可能错在哪里", c.WhyMarketMayBeWrong, true), F("Holding", "预期持有期（例如 3 年 / 催化剂兑现前）", c.ExpectedHoldingPeriod),
            F("Base", "Base Case · 基准情景", c.BaseCaseNarrative, true), F("Bull", "Bull Case · 乐观情景", c.BullCaseNarrative, true), F("Bear", "Bear Case · 悲观情景", c.BearCaseNarrative, true),
            F("Catalysts", "预期催化剂", c.ExpectedCatalysts, true), F("Risks", "关键风险", c.KeyRisks, true), F("Review", "复盘日期 yyyy-MM-dd（可留空）", thesis is null ? DateOnly.FromDateTime(DateTime.Today).AddDays(30).ToString("yyyy-MM-dd") : thesis.ReviewDate?.ToString("yyyy-MM-dd") ?? ""),
            F("Reason", "本次建立 / 修改依据（必填）", thesis is null ? "初次建立逻辑" : "", true)
        ], async vm =>
        {
            var reason = Reason(vm); var content = new ThesisContent(vm.Text("Title"), vm.Text("Summary"), vm.Text("Market"), vm.Text("Holding"), vm.Text("Base"), vm.Text("Bull"), vm.Text("Bear"), vm.Text("Catalysts"), vm.Text("Risks"));
            if (thesis is null) { var id = await commands.CreateAsync(securityId, content, Date(vm, "Review"), User, reason); created?.Invoke(id); }
            else await commands.EditAsync(thesis.Id, thesis.Version, content, Date(vm, "Review"), reason, User);
        }, errors);
    }
    public EditorViewModel AssumptionEditor(ThesisDetail t, AssumptionDto? item = null)
    {
        var c = item?.Content ?? new("", "", null, null, 0, 0, null, "", ThresholdDirection.AtOrBelow, "");
        return new("研究假设 · 手工核实", "比例单位由你明确填写，例如 % 时 20 表示 20%。状态由阈值计算；未知当前值显示 Unknown。假设触发不替代 Kill 条件或人工决策。", [
            F("Description", "假设描述", c.Description, true), F("Metric", "验证指标", c.Metric), F("Baseline", "基线（可空）", N(c.Baseline)), F("Expected", "预期值（可空）", N(c.ExpectedValue)),
            Direction(c.Direction), F("Warning", "Warning 阈值", N(c.WarningThreshold)), F("Kill", "Kill 阈值", N(c.KillThreshold)), F("Current", "当前值（空白 = 未知）", N(c.CurrentValue)),
            F("Unit", "单位（例如 %、CNY、倍、0/1）", c.Unit), F("Evidence", "证据 / 来源 / 数据日期", c.Evidence, true), F("Reason", "本次修改依据（必填）", "", true)
        ], vm => commands.SaveAssumptionAsync(t.Id, t.Version, item?.Id, new(vm.Text("Description"), vm.Text("Metric"), Optional(vm, "Baseline"), Optional(vm, "Expected"), vm.Number("Warning"), vm.Number("Kill"), Optional(vm, "Current"), vm.Text("Unit"), vm.EnumValue<ThresholdDirection>("Direction"), vm.Text("Evidence")), Reason(vm), User), errors);
    }
    public EditorViewModel KillEditor(ThesisDetail t, KillDto? item = null)
    {
        var c = item?.Content ?? new("", "", "", 0, 0, null, "", ThresholdDirection.AtOrBelow, "");
        return new(item is null ? "用户新增 Kill Condition" : "用户编辑 Kill Condition / 当前值", "每条都是关键条件。只有用户能新增、编辑、删除或改变阈值。Triggered 将提示 Warning，是否失效仍需你确认；任何修改均留存历史。", [
            F("Title", "条件标题", c.Title), F("Description", "什么事实将否定逻辑", c.Description, true), F("Metric", "可观测指标（定性事件可使用 0/1）", c.Metric), Direction(c.Direction),
            F("Warning", "Warning 阈值", N(c.WarningThreshold)), F("Trigger", "Trigger 阈值", N(c.TriggerThreshold)), F("Current", "当前值（空白 = 未评估，不代表正常）", N(c.CurrentValue)),
            F("Unit", "单位", c.Unit), F("Evidence", "证据 / 来源 / 数据日期", c.Evidence, true), F("Reason", "本次新增 / 修改依据（必填）", "", true)
        ], vm => commands.SaveKillAsync(t.Id, t.Version, item?.Id, new(vm.Text("Title"), vm.Text("Description"), vm.Text("Metric"), vm.Number("Warning"), vm.Number("Trigger"), Optional(vm, "Current"), vm.Text("Unit"), vm.EnumValue<ThresholdDirection>("Direction"), vm.Text("Evidence")), Reason(vm), User), errors);
    }
    public EditorViewModel StatusEditor(ThesisDetail t)
        => new("用户确认 Thesis 状态", "Active 将占用该证券唯一的当前逻辑位置。Warning 不会自动恢复 Active。Invalidated / Closed 后正文与条件只读；确认失效不会触发任何交易。", [
            new("Status", "目标状态", t.Status.ToString(), Enum.GetValues<ThesisStatus>().Select(x => new FormOption(x.ToString(), x.ToString())).ToArray()), F("Reason", "决策依据（必填）", "", true)
        ], vm => commands.ChangeStatusAsync(t.Id, t.Version, vm.EnumValue<ThesisStatus>("Status"), Reason(vm), User), errors);
    public EditorViewModel ReviewEditor(ThesisDetail t)
        => new("完成复盘", "核对假设、触发条件与来源后记录结论。此操作安排下一次复盘，不自动改变 Thesis 状态或抹去触发条件。", [F("Review", "下次复盘 yyyy-MM-dd", DateOnly.FromDateTime(DateTime.Today).AddDays(30).ToString("yyyy-MM-dd")), F("Reason", "复盘结论与证据（必填）", "", true)],
            vm => commands.CompleteReviewAsync(t.Id, t.Version, Date(vm, "Review") ?? throw new BusinessException("请填写下次复盘日期。"), DateOnly.FromDateTime(DateTime.Today), Reason(vm), User), errors);
    public EditorViewModel DeleteEditor(ThesisDetail t, Guid childId, bool kill)
        => new(kill ? "用户删除 Kill 条件" : "删除假设", "只移出当前版本，旧条件与证据仍保留在历史快照。删除触发条件不会自动把 Warning 恢复为 Active。", [F("Reason", "删除依据（必填）", "", true)],
            vm => kill ? commands.DeleteKillAsync(t.Id, t.Version, childId, Reason(vm), User) : commands.DeleteAssumptionAsync(t.Id, t.Version, childId, Reason(vm), User), errors);
    public EditorViewModel ArchiveEditor(ThesisDetail t)
        => new("归档历史逻辑", "仅允许归档 Invalidated / Closed。记录和版本不会删除，仍可在 Thesis 选择器查看。", [F("Reason", "归档依据（必填）", "", true)], vm => commands.ArchiveAsync(t.Id, t.Version, Reason(vm), User), errors);
}
