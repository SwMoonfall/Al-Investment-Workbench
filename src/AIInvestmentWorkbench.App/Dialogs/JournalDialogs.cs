using System.Globalization;
using System.Text.Json;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class JournalDialogs(IJournalReviewCommands commands, ISecurityRepository securities, IErrorHandler errors)
{
    public async Task<EditorViewModel> JournalEditorAsync(Guid accountId, JournalDraft? draft = null)
    {
        var list = await securities.SearchAsync(""); var d = draft ?? new(accountId, null, new(DateOnly.FromDateTime(DateTime.Today), JournalAction.ResearchDecision, null, null, null, "")); var c = d.Content;
        var fields = new List<FormField>
        {
            new("Date", "日期 yyyy-MM-dd", c.Date.ToString("yyyy-MM-dd")), new("Security", "证券（可不关联）", d.SecurityId?.ToString() ?? "", [new("账户日志 / 不关联证券", ""), .. list.Select(x => new FormOption(x.Symbol + " " + x.Name, x.Id.ToString()))], readOnly: d.PreviousId.HasValue || d.DecisionId.HasValue),
            Choice("Action", "动作（日志是记录，不会下单）", c.Action), new("Price", "记录价格（可空，不等于实际成交）", N(c.Price)), new("Quantity", "记录数量（可空）", N(c.Quantity)), new("Weight", "当时组合权重（0–1，可空）", N(c.PortfolioWeight)),
            new("Reason", "理由（必填）", c.Reason, multiline: true), new("Concern", "市场担忧", c.MarketConcern, multiline: true), new("Wrong", "为什么市场可能错了", c.WhyMarketMayBeWrong, multiline: true),
            new("Expected", "预期发展", c.ExpectedDevelopment, multiline: true), new("Kill", "当时 Kill 条件摘要", c.KillConditionSummary, multiline: true), new("Horizon", "预计持有周期（文字）", c.ExpectedHoldingPeriod),
            new("Days", "预计持有天数（可空；供周期成熟度统计）", c.ExpectedHoldingDays?.ToString(CultureInfo.InvariantCulture) ?? ""), new("Emotion", "当时情绪（仅用户自述，不做诊断）", c.Emotion), new("Confidence", "信心 0–100（可空）", N(c.Confidence)), new("Notes", "备注", c.Notes, multiline: true)
        };
        if (d.PreviousId.HasValue) fields.Add(new("Correction", "更正原因（新版本保留原始记录）", ""));
        return new(d.PreviousId.HasValue ? "追加 Journal 更正版本" : d.DecisionId.HasValue ? "确认决策生成的 Journal 草稿" : "新增 Investment Journal", "只有点击保存才写入。原始记录不可覆盖；日志不会产生交易或修改 Thesis。", fields, async e =>
        {
            var days = Optional(e, "Days"); if (days.HasValue && days.Value != decimal.Truncate(days.Value)) throw new BusinessException("预计天数必须是整数。");
            var content = new JournalContent(DateOnly.FromDateTime(e.Date("Date").Date), e.EnumValue<JournalAction>("Action"), Optional(e, "Price"), Optional(e, "Quantity"), Optional(e, "Weight"), e.Text("Reason"), e.Text("Concern"), e.Text("Wrong"), e.Text("Expected"), e.Text("Kill"), e.Text("Horizon"), e.Text("Emotion"), Optional(e, "Confidence"), e.Text("Notes"), days.HasValue ? checked((int)days.Value) : null);
            await commands.SaveJournalAsync(d with { SecurityId = e.Text("Security") == "" ? null : Guid.Parse(e.Text("Security")), Content = content, CorrectionReason = d.PreviousId.HasValue ? e.Text("Correction") : "用户确认保存" }, ThesisActor.HumanUser);
        }, errors);
    }
    public EditorViewModel ReviewEditor(ReviewEvidence evidence, InvestmentReview? previous = null)
    {
        var old = previous is null ? [] : JsonSerializer.Deserialize<AssumptionReview[]>(previous.AssumptionsJson)!;
        var fields = new List<FormField> { new("Scope", "对象 / 期间 / 资料日期", $"{evidence.Kind} · {evidence.Start:yyyy-MM-dd}—{evidence.End:yyyy-MM-dd} · 资料 {evidence.CapturedAt.ToLocalTime():yyyy-MM-dd HH:mm}", readOnly: true), new("Actual", "本期实际结果 / 资料缺口", previous?.ActualResults ?? "", multiline: true) };
        foreach (var a in evidence.Assumptions)
        { var saved = old.SingleOrDefault(x => x.Id == a.Id); fields.Add(Choice(a.Id.ToString(), a.Description, saved?.Outcome ?? AssumptionOutcome.Unassessed)); fields.Add(new(a.Id + "Evidence", "此假设的验证证据 / 不确定性", saved?.Evidence ?? "", multiline: true)); }
        if (evidence.Kind == ReviewKind.Quarterly) fields.Add(new("Decision", "用户决策（AI 不选择，不创建交易）", previous?.Decision?.ToString() ?? "", [new("尚未决定", ""), .. Enum.GetValues<ReviewDecision>().Select(x => new FormOption(x.ToString(), x.ToString()))]));
        fields.Add(new("Conclusion", "用户结论与理由（完成时必填）", previous?.Conclusion ?? "", multiline: true)); fields.Add(Choice("Status", "保存状态（本期未结束只能 Draft）", ReviewStatus.Draft));
        return new(previous is null ? "确认保存复盘" : "追加复盘版本", "系统只提供资料与变化，假设评估和最终决策由你选择。旧版本永久保留。", fields, async e =>
        {
            var assessments = evidence.Assumptions.Select(x => x with { Outcome = e.EnumValue<AssumptionOutcome>(x.Id.ToString()), Evidence = e.Text(x.Id + "Evidence") }).ToArray();
            var decision = evidence.Kind == ReviewKind.Quarterly && e.Text("Decision") != "" ? e.EnumValue<ReviewDecision>("Decision") : (ReviewDecision?)null;
            await commands.SaveReviewAsync(new(evidence, e.Text("Actual"), assessments, decision, e.Text("Conclusion"), e.EnumValue<ReviewStatus>("Status"), previous?.Id), ThesisActor.HumanUser);
        }, errors);
    }
    private static FormField Choice<T>(string key, string label, T value) where T : struct, Enum => new(key, label, value.ToString(), Enum.GetValues<T>().Select(x => new FormOption(x.ToString(), x.ToString())).ToArray());
    private static string N(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static decimal? Optional(EditorViewModel e, string key) => e.Text(key) == "" ? null : e.Number(key);
}
