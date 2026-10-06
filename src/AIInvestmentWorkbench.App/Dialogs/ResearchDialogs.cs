using System.Globalization;
using System.Windows;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.App.Dialogs;

public sealed class ResearchDialogs(IResearchStore store, IErrorHandler errors)
{
    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static FormField F(string key, string label, string value = "", bool multiline = false) => new(key, label, value, multiline: multiline);
    private static FormField Choice<T>(string key, string label, T value) where T : struct, Enum
        => new(key, label, value.ToString(), Enum.GetValues<T>().Select(x => new FormOption(x.ToString(), x.ToString())).ToArray());
    public bool Show(EditorViewModel vm) => new EditorWindow(vm) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog() == true;
    public bool Confirm(string text) => MessageBox.Show(text, "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public EditorViewModel SectionEditor(Guid securityId, CompanyResearch? research, string section)
    {
        var content = research?.Content ?? new ResearchContent();
        var properties = typeof(ResearchContent).GetProperties();
        return new("编辑研究 · " + section, "保存后才写入。取消或关闭会放弃本次修改；编辑期间不能切换证券。", [F("Body", section, (string)properties.Single(x => x.Name == section).GetValue(content)!, true)], async vm =>
        {
            var values = properties.Select(p => p.Name == section ? vm.Text("Body") : (string)p.GetValue(content)!).ToArray();
            await store.SaveResearchAsync(securityId, new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9]), research?.Revision ?? 0);
        }, errors);
    }
    public EditorViewModel MetricEditor(Guid securityId, string currency, IReadOnlyList<ResearchSource> sources, FinancialMetric? metric = null)
    {
        var sourceOptions = new List<FormOption> { new("未关联来源", "") };
        sourceOptions.AddRange(sources.Select(x => new FormOption($"L{x.ReliabilityLevel} · {x.Title}", x.Id.ToString())));
        return new(metric is null ? "新增财务指标" : "编辑财务指标", "金额使用原币单位，比例用小数（0.25 = 25%），股数用股，EPS 用每股金额。Capex 填正数支出。", [
            F("Period", "期间：年度 YYYY；季度/TTM YYYY-Q1", metric?.Period ?? "2025"),
            Choice("PeriodType", "期间类型", metric?.PeriodType ?? PeriodType.Annual), Choice("MetricType", "指标", metric?.MetricType ?? MetricType.Revenue),
            F("Value", "数值", N(metric?.Value ?? 0)), F("Currency", "财务报告币种（不进行汇率换算）", metric?.Currency ?? currency),
            new("Source", "资料来源", metric?.SourceId?.ToString() ?? "", sourceOptions),
            new("Estimated", "是否估算", (metric?.IsEstimated ?? false).ToString(), [new("已披露 / 非估算", "False"), new("估算值", "True")]), F("Notes", "备注", metric?.Notes ?? "", true)
        ], vm => store.SaveMetricAsync(new(metric?.Id, securityId, vm.Text("Period"), vm.EnumValue<PeriodType>("PeriodType"), vm.EnumValue<MetricType>("MetricType"), vm.Number("Value"), vm.Text("Currency"),
            vm.Text("Source").Length == 0 ? null : Guid.Parse(vm.Text("Source")), bool.Parse(vm.Text("Estimated")), vm.Text("Notes"))), errors);
    }
    public EditorViewModel SourceEditor(Guid securityId, ResearchSource? source = null, string path = "", string text = "")
    {
        var filePath = source?.LocalFilePath ?? path;
        var extracted = source?.ExtractedText ?? text;
        var type = source?.SourceType ?? (System.IO.Path.GetExtension(path).ToLowerInvariant() switch { ".pdf" => SourceType.PDF, ".csv" => SourceType.CSV, _ => SourceType.UserNotes });
        return new(source is null ? "新增研究来源" : "编辑研究来源", "等级 1 最高、6 最低，由用户核实。原文件保持原位，提取文本存入数据库；不会自动访问链接。", [
            F("Title", "标题", source?.Title ?? System.IO.Path.GetFileNameWithoutExtension(path)), F("Publisher", "发布者", source?.Publisher ?? ""),
            F("Date", "发布日期 yyyy-MM-dd（可留空）", source?.PublishedDate?.ToString("yyyy-MM-dd") ?? ""), F("Url", "网址（可留空）", source?.Url ?? ""),
            Choice("Type", "来源类型", type), new("Level", "来源等级", (source?.ReliabilityLevel ?? 3).ToString(CultureInfo.InvariantCulture), Enumerable.Range(1, 6).Select(x => new FormOption($"{x} 级" + (x == 1 ? " · 最高" : x == 6 ? " · 最低" : ""), x.ToString(CultureInfo.InvariantCulture))).ToArray()),
            new("Path", "原始文件路径", filePath, readOnly: true), F("Notes", "备注", source?.Notes ?? "", true), new("Extracted", "提取文本（只读预览）", extracted, readOnly: true, multiline: true)
        ], async vm => { await store.SaveSourceAsync(new(source?.Id, securityId, vm.Text("Title"), vm.Text("Publisher"), vm.Text("Date").Length == 0 ? null : vm.Date("Date"), vm.Text("Url"), filePath,
            vm.EnumValue<SourceType>("Type"), int.Parse(vm.Text("Level"), CultureInfo.InvariantCulture), vm.Text("Notes"), extracted)); }, errors);
    }
    private static ScoreDraft Draft(ResearchScore score) => new(score.Dimension, score.Weight, score.UserScore, score.Reason, score.Evidence);
    public EditorViewModel ScoreEditor(Guid securityId, IReadOnlyList<ResearchScore> scores, ResearchScore selected)
        => new("评分 · " + selected.Dimension, "用户评分优先。空白表示未评分；当前版本不会生成 AI 评分。满分 100。", [
            F("Score", "用户评分（可留空）", selected.UserScore.HasValue ? N(selected.UserScore.Value) : ""), new("AI", "AI 评分（仅当存在时作为未确认参考）", selected.AIScore?.ToString(CultureInfo.InvariantCulture) ?? "尚无 AI 评分", readOnly: true),
            F("Reason", "评分理由", selected.Reason, true), F("Evidence", "证据 / 来源标题或链接", selected.Evidence, true)
        ], vm => store.SaveScoresAsync(securityId, scores.Select(s => s.Dimension == selected.Dimension ? new ScoreDraft(s.Dimension, s.Weight, vm.Text("Score").Length == 0 ? null : vm.Number("Score"), vm.Text("Reason"), vm.Text("Evidence")) : Draft(s)).ToList()), errors);
    public EditorViewModel WeightsEditor(Guid securityId, IReadOnlyList<ResearchScore> scores)
        => new("评分权重", "此证券的权重合计必须为 100%。默认值可按研究方法调整。", scores.Select(s => F(s.Dimension.ToString(), s.Dimension + " 权重 %", N(s.Weight))),
            vm => store.SaveScoresAsync(securityId, scores.Select(s => new ScoreDraft(s.Dimension, vm.Number(s.Dimension.ToString()), s.UserScore, s.Reason, s.Evidence)).ToList()), errors);
    public EditorViewModel ThresholdsEditor(RiskThresholds t)
    {
        var fields = new[] {
            F("GrowthGapWarning", "应收/库存增速差 · 警示（0.15 = 15 个百分点）", N(t.GrowthGapWarning)), F("GrowthGapHigh", "应收/库存增速差 · 高关注", N(t.GrowthGapHigh)),
            F("CashConversionWarning", "OCF / 净利润 · 警示（低于等于）", N(t.CashConversionWarning)), F("CashConversionHigh", "OCF / 净利润 · 高关注（低于等于）", N(t.CashConversionHigh)),
            F("DebtGrowthWarning", "债务同比 · 警示", N(t.DebtGrowthWarning)), F("DebtGrowthHigh", "债务同比 · 高关注", N(t.DebtGrowthHigh)),
            F("DilutionWarning", "连续增股累计比例 · 警示", N(t.DilutionWarning)), F("DilutionHigh", "连续增股累计比例 · 高关注", N(t.DilutionHigh)),
            F("SbcWarning", "SBC / 收入 · 警示", N(t.SbcWarning)), F("SbcHigh", "SBC / 收入 · 高关注", N(t.SbcHigh)), F("Periods", "股数/FCF 连续期数（2 至 10 的整数）", t.ConsecutivePeriods.ToString(CultureInfo.InvariantCulture)),
            F("FcfWarning", "FCF 原币金额 · 警示上限（全部 ≤）", N(t.FcfWarning)), F("FcfHigh", "FCF 原币金额 · 高关注上限（全部 <）", N(t.FcfHigh)) };
        return new("会计关注规则阈值", "配置对所有证券生效。只检查非估算数据；提示用于进一步研究，不代表财务造假判断。FCF 门槛按所选币种原币金额比较，不换汇；高关注上限须不大于警示上限。", fields,
            vm => store.SaveThresholdsAsync(new(vm.Number("GrowthGapWarning"), vm.Number("GrowthGapHigh"), vm.Number("CashConversionWarning"), vm.Number("CashConversionHigh"), vm.Number("DebtGrowthWarning"), vm.Number("DebtGrowthHigh"), vm.Number("DilutionWarning"), vm.Number("DilutionHigh"), vm.Number("SbcWarning"), vm.Number("SbcHigh"), int.Parse(vm.Text("Periods"), CultureInfo.InvariantCulture), vm.Number("FcfWarning"), vm.Number("FcfHigh"))), errors);
    }
}
