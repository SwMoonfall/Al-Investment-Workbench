using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Domain.Entities;

/// <summary>Only non-secret preferences belong here. Credentials require an OS credential store.</summary>
public sealed class AppSetting : Entity
{
    private AppSetting() { }
    public AppSetting(string key, string value) { Key = Guard.Text(key, nameof(key), 100); SetValue(value); }
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public void SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 4000) throw new ArgumentException("设置值过长。", nameof(value));
        Value = value; MarkUpdated();
    }
}
