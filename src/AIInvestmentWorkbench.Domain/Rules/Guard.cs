namespace AIInvestmentWorkbench.Domain.Rules;

public static class Guard
{
    public static string Text(string value, string name, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        value = value.Trim();
        if (value.Length > maxLength) throw new ArgumentException($"长度不能超过 {maxLength}。", name);
        return value;
    }

    public static string Currency(string value)
    {
        value = Text(value, nameof(value), 3).ToUpperInvariant();
        if (value == "RMB") value = "CNY";
        if (value.Length != 3 || value.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("币种必须是三个英文字母代码。", nameof(value));
        return value;
    }

    public static Guid Id(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException("标识不能为空。", name) : value;

    public static string OptionalText(string? value, int max = 2000)
    {
        value = (value ?? "").Trim();
        if (value.Length > max) throw new BusinessException($"文本不能超过 {max} 字符。");
        return value;
    }
    public static decimal Weight(decimal value)
        => value is < 0 or > 1 ? throw new BusinessException("权重必须在 0% 到 100% 之间。") : value;
}
