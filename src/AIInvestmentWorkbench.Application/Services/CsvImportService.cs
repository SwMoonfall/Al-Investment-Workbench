using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Application.Services;

public sealed class CsvImportService(IPortfolioStore store)
{
    public static IReadOnlyList<string> Fields(ImportKind kind) => kind switch
    {
        ImportKind.Security => ["Ticker", "CompanyName", "Market", "Exchange", "Currency", "SecurityType", "Sector", "Industry", "Country", "ISIN", "Notes"],
        ImportKind.Positions => ["Ticker", "Exchange", "Quantity", "AverageCost", "Date", "Currency"],
        _ => ["Ticker", "Exchange", "Type", "Date", "Quantity", "Price", "Amount", "Fees", "Currency", "Notes"]
    };
    public static bool Required(ImportKind kind, string field) => kind switch
    {
        ImportKind.Security => new[] { "Ticker", "CompanyName", "Market", "Exchange", "Currency", "SecurityType" }.Contains(field),
        ImportKind.Positions => true,
        _ => new[] { "Type", "Date", "Currency" }.Contains(field)
    };
    public static CsvDocument Parse(string text)
    {
        if (text.Length > 5_000_000) throw new BusinessException("CSV 超过 5MB 字符上限，请拆分文件。");
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false;
        void EndField() { row.Add(field.ToString()); field.Clear(); closed = false; }
        void EndRow() { EndField(); if (row.Any(x => x.Length > 0)) rows.Add(row.ToArray()); row.Clear(); }
        text = text.TrimStart('\uFEFF');
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') { quoted = false; closed = true; }
                else field.Append(c);
            }
            else if (c == ',' ) EndField();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else { if (closed || c == '"') throw new BusinessException("CSV 引号格式错误。"); field.Append(c); }
        }
        if (quoted) throw new BusinessException("CSV 引号未闭合。");
        if (field.Length > 0 || row.Count > 0 || closed) EndRow();
        if (rows.Count < 2) throw new BusinessException("CSV 需要表头和至少一行数据。");
        var headers = rows[0].Select(x => x.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new BusinessException("CSV 表头不能空白或重复。");
        if (rows.Count > 10001) throw new BusinessException("一次最多导入 10000 条记录。");
        for (int i = 1; i < rows.Count; i++) if (rows[i].Length != headers.Length) throw new BusinessException($"第 {i + 1} 条记录的字段数与表头不一致。");
        return new(headers, rows.Skip(1).ToArray());
    }

    public async Task<ImportOutcome> ExecuteAsync(CsvDocument document, ImportKind kind, Guid accountId,
        IReadOnlyDictionary<string, string> mapping, bool validateOnly, CancellationToken ct = default)
    {
        var errors = new List<string>(); var securities = new List<SecurityDraft>(); var positions = new List<ImportPosition>(); var transactions = new List<ImportTransaction>();
        foreach (var field in Fields(kind).Where(f => Required(kind, f)))
            if (!mapping.TryGetValue(field, out var header) || !document.Headers.Contains(header)) errors.Add($"请映射必填字段 {field}。");
        if (errors.Count > 0) return new(false, 0, errors);
        for (int i = 0; i < document.Rows.Count; i++)
        {
            var row = document.Rows[i];
            string Get(string field) => mapping.TryGetValue(field, out var header) && document.Headers.Contains(header)
                ? row[document.Headers.ToList().IndexOf(header)].Trim() : "";
            decimal Number(string field, bool optional = false)
            {
                var value = Get(field);
                if (optional && value == "") return 0;
                return decimal.TryParse(value, (NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint), CultureInfo.InvariantCulture, out var number) ? number : throw new BusinessException($"{field} 不是有效数字。");
            }
            DateTimeOffset Date()
            {
                var value = Get("Date");
                if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw new BusinessException("Date 必须为 yyyy-MM-dd。");
                if (date.Date > DateTime.Today) throw new BusinessException("不能导入未来日期。");
                return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
            }
            try
            {
                if (kind == ImportKind.Security)
                {
                    var draft = new SecurityDraft(null, Get("Ticker"), Get("CompanyName"), ParseEnum<Market>(Get("Market")), Get("Exchange"), Get("Currency"),
                        ParseEnum<SecurityType>(Get("SecurityType")), Get("Sector"), Get("Industry"), Get("Country"), Get("ISIN"), Get("Notes"));
                    var entity = new Domain.Entities.Security(draft.Ticker, draft.CompanyName, draft.Exchange, draft.Currency, draft.SecurityType);
                    SecurityApplicationService.Apply(entity, draft); securities.Add(draft);
                }
                else if (kind == ImportKind.Positions)
                    positions.Add(new(Get("Ticker"), Get("Exchange"), Number("Quantity"), Number("AverageCost"), Date(), Get("Currency")));
                else transactions.Add(new(Get("Ticker"), Get("Exchange"), ParseEnum<TransactionType>(Get("Type")), Date(), Number("Quantity", true), Number("Price", true), Number("Amount", true), Number("Fees", true), Get("Currency"), Get("Notes")));
            }
            catch (Exception ex) when (ex is ArgumentException or BusinessException) { errors.Add($"记录 {i + 2}: {ex.Message}"); }
        }
        if (errors.Count > 0) return new(false, 0, errors);
        var normalized = System.Text.Json.JsonSerializer.Serialize(new { kind, accountId, securities, positions, transactions });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return await store.ImportAsync(new(kind, accountId, hash, securities, positions, transactions), validateOnly, ct);
    }
    private static T ParseEnum<T>(string text) where T : struct, Enum
        => Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value) ? value : throw new BusinessException($"无效的 {typeof(T).Name}: {text}");
}

