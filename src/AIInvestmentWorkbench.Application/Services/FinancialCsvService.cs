using System.Globalization;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
namespace AIInvestmentWorkbench.Application.Services;

public sealed class FinancialCsvService(IResearchStore store)
{
    public static IReadOnlyList<string> Fields { get; } = ["Period", "PeriodType", "MetricType", "Value", "Currency", "SourceId", "IsEstimated", "Notes"];
    public static IReadOnlyList<string> Required { get; } = ["Period", "PeriodType", "MetricType", "Value", "Currency"];
    public async Task<ImportOutcome> ExecuteAsync(Guid securityId, CsvDocument document, IReadOnlyDictionary<string, string> mapping, bool validateOnly, CancellationToken ct = default)
    {
        var errors = new List<string>();
        foreach (var field in Required)
            if (!mapping.TryGetValue(field, out var header) || !document.Headers.Contains(header)) errors.Add($"请映射必需字段 {field}。");
        if (errors.Count > 0) return new(false, 0, errors);
        var drafts = new List<MetricDraft>();
        for (var i = 0; i < document.Rows.Count; i++)
        {
            try
            {
                var row = document.Rows[i];
                string Read(string field) => mapping.TryGetValue(field, out var header) && document.Headers.Contains(header) ? row[document.Headers.ToList().IndexOf(header)].Trim() : "";
                T ParseEnum<T>(string field) where T : struct, Enum => Enum.TryParse<T>(Read(field), true, out var value) && Enum.IsDefined(value) ? value : throw new BusinessException($"{field} 选项无效。");
                if (!decimal.TryParse(Read("Value"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) throw new BusinessException("Value 必须为数字，不含千分位或百分号。");
                var sourceText = Read("SourceId");
                Guid? source = sourceText.Length == 0 ? null : Guid.TryParse(sourceText, out var id) ? id : throw new BusinessException("SourceId 必须是 GUID。");
                var estimatedText = Read("IsEstimated");
                var estimated = estimatedText.Length == 0 ? false : bool.TryParse(estimatedText, out var b) ? b : throw new BusinessException("IsEstimated 必须为 true 或 false。");
                drafts.Add(new(null, securityId, Read("Period"), ParseEnum<PeriodType>("PeriodType"), ParseEnum<MetricType>("MetricType"), number, Read("Currency"), source, estimated, Read("Notes")));
            }
            catch (BusinessException e) { errors.Add($"第 {i + 2} 行：{e.Message}"); }
        }
        if (errors.Count > 0) return new(false, 0, errors);
        return await store.ImportMetricsAsync(drafts, validateOnly, ct);
    }
}
