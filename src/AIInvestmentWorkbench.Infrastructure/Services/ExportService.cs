using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Rules;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;
public enum ExportKind { PortfolioCsv, TransactionsCsv, FinancialMetricsCsv, JournalCsv, ResearchJson, ResearchMarkdown, ThesisMarkdown, QuarterlyReviewMarkdown }
public sealed class ExportService(IDbContextFactory<InvestmentDbContext> factory, IPortfolioStore portfolio, IResearchStore research, IThesisReader theses, IJournalReviewReader journals)
{
    public async Task ExportAsync(ExportKind kind, Guid account, Guid? security, string path, CancellationToken ct = default)
    {
        await Task.Run(async () =>
        {
            string content;
            switch (kind)
            {
                case ExportKind.PortfolioCsv: content = Csv((await portfolio.ReadAsync(account, ct)).Holdings); break;
                case ExportKind.TransactionsCsv: content = Csv((await portfolio.ReadAsync(account, ct)).Transactions); break;
                case ExportKind.FinancialMetricsCsv:
                    await using (var db = await factory.CreateDbContextAsync(ct)) content = Csv(await db.FinancialMetrics.AsNoTracking().Where(x => security == null || x.SecurityId == security).OrderBy(x => x.Period).ToArrayAsync(ct)); break;
                case ExportKind.JournalCsv: content = Csv((await journals.JournalsAsync(account, history: true, ct: ct)).Select(x => x.Journal)); break;
                case ExportKind.ResearchJson:
                case ExportKind.ResearchMarkdown:
                    if (security is null) throw new BusinessException("请先选择要导出的证券。");
                    var data = await research.ReadAsync(security.Value, ct);
                    content = kind == ExportKind.ResearchJson ? JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }) : "# Research\n\n证券 ID：" + security + "\n\n" + MarkdownProperties(data.Research?.Content) + "\n## Sources\n" + string.Join("\n", data.Sources.Select(x => $"- {x.Title} · {x.Publisher} · {x.PublishedDate:O} · 来源等级 {x.ReliabilityLevel} · {x.Url}\n  {x.Notes}")); break;
                case ExportKind.ThesisMarkdown:
                    if (security is null) throw new BusinessException("请先选择要导出的证券。");
                    var all = await theses.ListAsync(security.Value, ct);
                    content = "# Thesis（当前记录；历史保存在数据库）\n\n" + string.Join("\n\n---\n\n", all.Select(t => $"## {t.Content.Title}\n状态 {t.Status} · v{t.Version} · Review {t.ReviewDate}\n\n{MarkdownProperties(t.Content)}\n### Assumptions\n" + string.Join("\n", t.Assumptions.Select(a => $"- {a.Content.Description} · {a.Content.Metric} · 预期 {a.Content.ExpectedValue} / 当前 {a.Content.CurrentValue} {a.Content.Unit} · {a.Status} · {a.Content.Evidence}")) + "\n### Kill Conditions\n" + string.Join("\n", t.KillConditions.Select(k => $"- {k.Content.Title} · {k.Content.Description} · 警戒 {k.Content.WarningThreshold} / 触发 {k.Content.TriggerThreshold} / 当前 {k.Content.CurrentValue} · {k.Status} · {k.Content.Evidence}")))); break;
                case ExportKind.QuarterlyReviewMarkdown:
                    var reviews = (await journals.ReviewsAsync(account, ct: ct)).Where(x => x.Review.Kind == ReviewKind.Quarterly && (security == null || x.Review.SecurityId == security));
                    content = "# Quarterly Reviews\n\n" + string.Join("\n\n---\n\n", reviews.Select(x =>
                    {
                        var e = JsonSerializer.Deserialize<ReviewEvidence>(x.Review.SnapshotJson)!;
                        return $"## {x.Label}\n实际结果：{x.Review.ActualResults}\n\n用户决策：{x.Review.Decision}\n\n用户结论：{x.Review.Conclusion}\n\n" + string.Join("\n", JsonSerializer.Deserialize<AssumptionReview[]>(x.Review.AssumptionsJson)!.Select(a => $"- {a.Description} · {a.Outcome} · {a.Evidence}")) + "\n\n" + string.Join("\n\n", e.Sections.Select(s => $"### {s.Title}\n" + string.Join("\n", s.Lines.Select(l => "- " + l))));
                    })); break;
                default: throw new BusinessException("未知导出类型。");
            }
            var full = Path.GetFullPath(path); var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllTextAsync(temp, content, new UTF8Encoding(true), ct); File.Move(temp, full, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }, ct);
    }
    public static string Csv<T>(IEnumerable<T> rows)
    {
        var properties = typeof(T).GetProperties().Where(p => p.CanRead && (p.PropertyType.IsValueType || p.PropertyType == typeof(string))).ToArray();
        return string.Join(",", properties.Select(p => p.Name)) + "\r\n" + string.Join("\r\n", rows.Select(row => string.Join(",", properties.Select(p => Cell(p.GetValue(row))))));
    }
    public static string Cell(object? value)
    {
        var text = value switch { null => "", DateTimeOffset d => d.ToString("O"), DateOnly d => d.ToString("yyyy-MM-dd"), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? "" };
        // Prevent spreadsheet formula execution; numeric negatives remain numbers.
        if (value is string && text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string MarkdownProperties(object? value) => value is null ? "尚无研究记录。" : string.Join("\n\n", value.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => $"### {p.Name}\n{p.GetValue(value)}"));
}
