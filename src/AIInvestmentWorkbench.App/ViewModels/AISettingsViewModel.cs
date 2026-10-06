using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.AI;
namespace AIInvestmentWorkbench.App.ViewModels;

public sealed class AISettingsViewModel : ObservableObject
{
    private readonly IAIStore _store; private readonly ISecretStore _secrets;
    private string _provider = "Disabled", _model = "", _baseUrl = "", _keyStatus = "尚未检查", _status = "默认关闭；填写模型并保存配置后启用。";
    private string _timeout = "90", _tokens = "4096";
    public AISettingsViewModel(IAIStore store, ISecretStore secrets, AIAnalysisService service, IErrorHandler errors)
    {
        _store = store; _secrets = secrets;
        SaveCommand = new(async _ => { await store.SaveSettingsAsync(Current()); await RefreshKey(); Status = "AI 设置已保存。API Key 与当前服务地址绑定。"; }, errors);
        SetKeyCommand = new(async _ => { var settings = Current(); settings.Validate(); var window = new SecretWindow(secrets, settings.SecretScope) { Owner = System.Windows.Application.Current.MainWindow }; window.ShowDialog(); await RefreshKey(); }, errors);
        DeleteKeyCommand = new(async _ => { Current().Validate(); await secrets.DeleteAsync(Current().SecretScope); await RefreshKey(); Status = "已删除此地址的密钥。"; }, errors);
        TestCommand = new(async _ => { await store.SaveSettingsAsync(Current()); Status = "正在测试连接（会发送最小请求，可能产生 API 费用）…"; Status = await service.TestConnectionAsync(); }, errors);
    }
    public string[] Providers { get; } = ["Disabled", "OpenAI"];
    public string Provider { get => _provider; set => Set(ref _provider, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string BaseUrl { get => _baseUrl; set { if (Set(ref _baseUrl, value)) KeyStatus = "地址已修改；保存后检查该地址的密钥"; } }
    public string TimeoutSeconds { get => _timeout; set => Set(ref _timeout, value); }
    public string MaxOutputTokens { get => _tokens; set => Set(ref _tokens, value); }
    public string KeyStatus { get => _keyStatus; private set => Set(ref _keyStatus, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand SetKeyCommand { get; }
    public AsyncCommand DeleteKeyCommand { get; }
    public AsyncCommand TestCommand { get; }
    private AISettings Current()
    {
        if (!int.TryParse(TimeoutSeconds, out var timeout) || !int.TryParse(MaxOutputTokens, out var tokens)) throw new AIInvestmentWorkbench.Domain.Rules.BusinessException("超时与最大输出 tokens 必须是整数。");
        return new(Provider, Model.Trim(), timeout, tokens, BaseUrl.Trim());
    }
    private async Task RefreshKey()
    {
        try { KeyStatus = string.IsNullOrEmpty(await _secrets.ReadAsync(Current().SecretScope)) ? "未配置" : "已加密保存（不会显示密钥）"; }
        catch { KeyStatus = "密钥不可读取，请重新设置"; }
    }
    public async Task LoadAsync()
    { var s = await _store.ReadSettingsAsync(); Provider = s.Provider; Model = s.Model; TimeoutSeconds = s.TimeoutSeconds.ToString(); MaxOutputTokens = s.MaxOutputTokens.ToString(); BaseUrl = s.BaseUrl; await RefreshKey(); }
}

