using System.Text;
using System.Text.Json;
namespace AIInvestmentWorkbench.Application.AI;

public static class StructuredAnalysis
{
    public static readonly string[] Sections = ["FACTS", "MANAGEMENT_CLAIMS", "ASSUMPTIONS", "AI_INFERENCES", "RISKS", "QUESTIONS", "STRONGEST_COUNTERARGUMENTS", "WEAK_ASSUMPTIONS", "CONTRADICTORY_EVIDENCE", "UNDERESTIMATED_RISKS", "MONITORING_KPIS", "POTENTIAL_KILL_SIGNALS"];
    public static string Schema => JsonSerializer.Serialize(new
    {
        type = "object", additionalProperties = false, required = Sections,
        properties = Sections.ToDictionary(x => x, _ => new { type = "array", items = new { type = "object", additionalProperties = false, required = new[] { "text", "source_ids" }, properties = new { text = new { type = "string" }, source_ids = new { type = "array", items = new { type = "string" } } } } })
    });
    public static bool TryParse(string raw, IReadOnlyList<AISourceReference> sources, out string json, out string error)
    {
        json = ""; error = "AI JSON 不符合输出结构，原始响应已保存。";
        try
        {
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 20 }); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != Sections.Length || root.EnumerateObject().Select(x => x.Name).Distinct().Count() != Sections.Length) return false;
            var ids = sources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var name in Sections)
            {
                if (!root.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Array || section.GetArrayLength() > 100) return false;
                foreach (var item in section.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2 || !item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString())
                        || !item.TryGetProperty("source_ids", out var refs) || refs.ValueKind != JsonValueKind.Array) return false;
                    if (name == "FACTS" && refs.GetArrayLength() == 0) { error = "FACTS 缺少来源引用，原文已保存，不能作为已验证事实使用。"; return false; }
                    if (refs.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || !ids.Contains(x.GetString()!))) { error = "AI 引用了未提供的来源 ID，原文已保存。"; return false; }
                }
            }
            json = JsonSerializer.Serialize(root); error = ""; return true;
        }
        catch (JsonException) { return false; }
    }
    public static string Render(string json)
    {
        if (string.IsNullOrEmpty(json)) return "无有效结构化结果，请查看状态与原文。";
        using var doc = JsonDocument.Parse(json); var text = new StringBuilder();
        foreach (var name in Sections)
        {
            text.AppendLine("### " + name);
            foreach (var item in doc.RootElement.GetProperty(name).EnumerateArray()) text.AppendLine("- " + item.GetProperty("text").GetString() + " [" + string.Join(", ", item.GetProperty("source_ids").EnumerateArray().Select(x => x.GetString())) + "]");
            text.AppendLine();
        }
        return text.ToString();
    }
}
