using System.Collections.ObjectModel;
using System.Windows;
using AIInvestmentWorkbench.App.Commands;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.App.ViewModels;

public sealed record ModelRow(Guid SourceId, string ModelId, string Name, Guid? LocalId, bool Enabled, bool Remote, bool Active)
{
    public string State => Active ? "当前模型" : LocalId is null ? "未添加" : Enabled ? "已启用" : "已禁用";
    public string Origin => Remote ? "远端发现" : "本地配置";
}

public sealed class AISettingsViewModel : PageViewModel
{
    private readonly ProviderCatalogService _service;
    private readonly ISecretStore _secrets;
    private readonly IModelDiscovery _discovery;
    private readonly IAIProvider _provider;
    private readonly Func<string, bool> _confirmDelete;
    private ProviderCatalog _catalog = ProviderCatalog.Empty;
    private readonly Dictionary<Guid, Draft> _drafts = [];
    private readonly Dictionary<Guid, IReadOnlyList<RemoteModel>> _remote = [];
    private readonly HashSet<string> _keyScopes = [];
    private Guid _editingId = Guid.NewGuid();
    private Guid? _newDraftId;
    private bool _loaded, _hydrating, _busy, _dirty;
    private ProviderSource? _selectedSource;
    private ModelRow? _selectedModel;
    private CancellationTokenSource? _request;
    private string _name = "", _type = "OpenAI", _url = "", _apiKey = "", _version = "2023-06-01", _organization = "", _project = "", _compatibility = "ChatCompletions", _timeout = "90", _tokens = "4096", _custom = "";
    private string _status = "添加提供商，保存配置后获取模型。";
    private sealed record Draft(string Name, string Type, string Url, string Key, string Version, string Organization, string Project, string Compatibility, string Timeout, string Tokens, bool Dirty);

    public AISettingsViewModel(ProviderCatalogService service, ISecretStore secrets, IModelDiscovery discovery, IAIProvider provider, IErrorHandler errors, Func<string, bool>? confirmDelete = null)
        : base("AIServices", "AI服务", "管理提供商连接与模型，选择研究助理使用的当前模型。")
    {
        _service = service; _secrets = secrets; _discovery = discovery; _provider = provider;
        _confirmDelete = confirmDelete ?? (message => MessageBox.Show(message, "删除提供商", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        SaveCommand = new(_ => Run(async ct => { await Save(ct); Status = "提供商配置已保存。"; }), errors);
        SaveAndDiscoverCommand = new(_ => Run(async ct => { await Save(ct); await Discover(ct); }), errors);
        DiscoverCommand = new(_ => Run(Discover), errors);
        NewCommand = new RelayCommand(_ => { if (IsBusy) return; CacheDraft(); _selectedSource = null; Raise(nameof(SelectedSource)); _newDraftId ??= Guid.NewGuid(); OpenDraft(_newDraftId.Value, null); });
        DiscardCommand = new RelayCommand(_ => { if (IsBusy) return; _drafts.Remove(_editingId); OpenDraft(_editingId, _catalog.Sources.FirstOrDefault(x => x.Id == _editingId)); Status = "已放弃当前草稿。"; });
        DeleteSourceCommand = new(_ => Run(DeleteSource), errors);
        AddModelCommand = new(_ => Run(async ct => { var source = SavedSource(); var row = RequireRow(); await Persist(ProviderCatalogService.AddModel(_catalog, source.Id, row.ModelId), ct); Status = "模型已添加 / 启用。重复添加不会创建副本。"; }), errors);
        AddCustomCommand = new(_ => Run(async ct => { var source = SavedSource(); await Persist(ProviderCatalogService.AddModel(_catalog, source.Id, CustomModelId), ct); CustomModelId = ""; Status = "自定义模型已添加 / 启用。"; }), errors);
        ToggleModelCommand = new(_ => Run(async ct => { SavedSource(); var row = RequireLocalRow(); var enabled = !row.Enabled; await Persist(_catalog with { Models = _catalog.Models.Select(x => x.Id == row.LocalId ? x with { Enabled = enabled } : x).ToArray(), ActiveModelId = !enabled && row.Active ? null : _catalog.ActiveModelId }, ct); Status = enabled ? "模型已启用。" : "模型已禁用；若为当前模型，已停止选用。"; }), errors);
        RemoveModelCommand = new(_ => Run(async ct => { SavedSource(); var row = RequireLocalRow(); await Persist(_catalog with { Models = _catalog.Models.Where(x => x.Id != row.LocalId).ToArray(), ActiveModelId = row.Active ? null : _catalog.ActiveModelId }, ct); Status = "已移除本地模型；远端发现记录仍可重新添加。"; }), errors);
        UseModelCommand = new(_ => Run(async ct => { SavedSource(); var row = RequireLocalRow(); if (!row.Enabled) throw new BusinessException("请先启用模型。"); await Persist(_catalog with { ActiveModelId = row.LocalId }, ct); Status = "当前模型已更新，后续分析将使用此模型。"; }), errors);
        TestModelCommand = new(_ => Run(async ct => { var source = SavedSource(); var row = RequireLocalRow(); Status = "正在调用测试（最小请求，可能产生费用）…"; var result = await _provider.CompleteAsync(new(source.Settings(row.ModelId) with { MaxOutputTokens = 128 }, "Reply briefly with OK.", "Connection test."), ct); Status = result.Completed && !string.IsNullOrWhiteSpace(result.RawOutput) ? $"模型 {row.ModelId} 调用成功。" : "已连接，但模型未返回完整结果；请检查模型能力或输出限制。"; }), errors);
        DeleteKeyCommand = new(_ => Run(async ct => { var source = SavedSource(); await _secrets.DeleteAsync(source.Settings().SecretScope, ct); _keyScopes.Remove(source.Settings().SecretScope); Raise(nameof(KeyStatus)); Status = "该提供商当前地址的密钥已删除。"; }), errors);
        CancelCommand = new RelayCommand(_ => _request?.Cancel());
    }
    public ObservableCollection<ProviderSource> Sources { get; } = [];
    public ObservableCollection<ModelRow> Models { get; } = [];
    public string[] ProviderOptions => ProviderTypes.All;
    public string[] CompatibilityOptions { get; } = ["ChatCompletions", "Responses"];
    public ProviderSource? SelectedSource { get => _selectedSource; set { if (_hydrating || IsBusy || value == _selectedSource) return; CacheDraft(); _selectedSource = value; Raise(); if (value is not null) OpenDraft(value.Id, value); } }
    public ModelRow? SelectedModel { get => _selectedModel; set => Set(ref _selectedModel, value); }
    public string SourceName { get => _name; set { if (Set(ref _name, value)) Dirty(); } }
    public string ProviderType { get => _type; set { if (Set(ref _type, value)) { if (!_hydrating) { BaseUrl = ""; ApiKey = ""; Organization = ""; Project = ""; ApiVersion = "2023-06-01"; CompatibilityApi = "ChatCompletions"; } Dirty(); RaiseType(); } } }
    public string BaseUrl { get => _url; set { if (Set(ref _url, value)) { Dirty(); Raise(nameof(KeyStatus)); } } }
    public string ApiKey { get => _apiKey; set { if (Set(ref _apiKey, value)) { Dirty(); Raise(nameof(KeyStatus)); } } }
    public string ApiVersion { get => _version; set { if (Set(ref _version, value)) Dirty(); } }
    public string Organization { get => _organization; set { if (Set(ref _organization, value)) Dirty(); } }
    public string Project { get => _project; set { if (Set(ref _project, value)) Dirty(); } }
    public string CompatibilityApi { get => _compatibility; set { if (Set(ref _compatibility, value)) Dirty(); } }
    public string TimeoutSeconds { get => _timeout; set { if (Set(ref _timeout, value)) Dirty(); } }
    public string MaxOutputTokens { get => _tokens; set { if (Set(ref _tokens, value)) Dirty(); } }
    public string CustomModelId { get => _custom; set => Set(ref _custom, value); }
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(CanEdit)); } }
    public bool CanEdit => _loaded && !IsBusy;
    public bool HasUnsavedDrafts => IsDirty && (!string.IsNullOrWhiteSpace(SourceName) || ApiKey.Length > 0 || BaseUrl.Length > 0)
        || _drafts.Any(x => x.Key != _editingId && x.Value.Dirty && (x.Value.Name.Length > 0 || x.Value.Key.Length > 0 || x.Value.Url.Length > 0));
    public bool IsDirty { get => _dirty; private set { Set(ref _dirty, value); Raise(nameof(DraftStatus)); } }
    public bool IsOpenAI => ProviderType == "OpenAI";
    public bool IsGoogle => ProviderType == "GoogleGenAI";
    public bool IsAnthropic => ProviderType == "Anthropic";
    public bool IsCompatible => ProviderType == "OpenAICompatible";
    public string DefaultUrl => IsCompatible ? "填写支持所选兼容协议的 HTTPS API 根地址（通常以 /v1 结尾）" : "留空使用：" + ProviderTypes.DefaultUrl(ProviderType);
    public string DraftStatus => IsDirty ? "有未保存更改 · 切换提供商或页面会保留草稿；关闭应用前请保存。" : "配置已同步 · 模型操作使用已保存配置。";
    public string KeyStatus { get { if (ApiKey.Length > 0) return "新密钥待保存"; try { return _keyScopes.Contains(Current().Settings().SecretScope) ? "已加密保存；留空保留现有密钥" : "当前提供商 / 地址未配置密钥"; } catch { return "请先填写有效配置"; } } }
    public string SourceEmpty => Sources.Count == 0 ? "尚无提供商。填写右侧表单创建第一个连接。" : $"{Sources.Count} 个提供商 · 点击查看或编辑";
    public string ModelSummary => Models.Count == 0 ? "暂无模型。保存并获取模型，或填写自定义模型 ID。" : $"{Models.Count} 个模型 · 远端发现不代表已启用或可调用";
    public string ActiveModel => _catalog.ActiveSettings.Provider == "Disabled" ? "当前模型：未选择（AI 分析未启用）" : $"当前模型：{_catalog.Sources.Single(x => x.Id == _catalog.ActiveSettings.SourceId).Name} / {_catalog.ActiveSettings.Model}";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand SaveAndDiscoverCommand { get; }
    public AsyncCommand DiscoverCommand { get; }
    public AsyncCommand DeleteSourceCommand { get; }
    public AsyncCommand AddModelCommand { get; }
    public AsyncCommand AddCustomCommand { get; }
    public AsyncCommand ToggleModelCommand { get; }
    public AsyncCommand RemoveModelCommand { get; }
    public AsyncCommand UseModelCommand { get; }
    public AsyncCommand TestModelCommand { get; }
    public AsyncCommand DeleteKeyCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public RelayCommand CancelCommand { get; }
    private void Dirty() { if (!_hydrating) IsDirty = true; }
    private void RaiseType() { foreach (var name in new[] { nameof(IsOpenAI), nameof(IsGoogle), nameof(IsAnthropic), nameof(IsCompatible), nameof(DefaultUrl), nameof(KeyStatus) }) Raise(name); }
    private void CacheDraft() => _drafts[_editingId] = new(SourceName, ProviderType, BaseUrl, ApiKey, ApiVersion, Organization, Project, CompatibilityApi, TimeoutSeconds, MaxOutputTokens, IsDirty);
    private void OpenDraft(Guid id, ProviderSource? source)
    {
        if (source is null) _newDraftId = id;
        _editingId = id; _hydrating = true;
        var d = _drafts.GetValueOrDefault(id) ?? new Draft(source?.Name ?? "", source?.Type ?? "OpenAI", source?.BaseUrl ?? "", "", source?.ApiVersion ?? "2023-06-01", source?.Organization ?? "", source?.Project ?? "", source?.CompatibilityApi ?? "ChatCompletions", (source?.TimeoutSeconds ?? 90).ToString(), (source?.MaxOutputTokens ?? 4096).ToString(), source is null);
        SourceName = d.Name; ProviderType = d.Type; BaseUrl = d.Url; ApiKey = d.Key; ApiVersion = d.Version; Organization = d.Organization; Project = d.Project; CompatibilityApi = d.Compatibility; TimeoutSeconds = d.Timeout; MaxOutputTokens = d.Tokens;
        IsDirty = d.Dirty; CustomModelId = ""; _hydrating = false; RaiseType(); RefreshRows();
    }
    private ProviderSource Current()
    {
        if (!int.TryParse(TimeoutSeconds, out var timeout) || !int.TryParse(MaxOutputTokens, out var tokens)) throw new BusinessException("超时与输出上限必须是整数。");
        return new(_editingId, SourceName.Trim(), ProviderType, BaseUrl.Trim(), timeout, tokens, ApiVersion.Trim(), Organization.Trim(), Project.Trim(), CompatibilityApi);
    }
    private ProviderSource SavedSource()
    {
        if (IsDirty) throw new BusinessException("请先保存配置，或放弃草稿后再操作模型。");
        return _catalog.Sources.SingleOrDefault(x => x.Id == _editingId) ?? throw new BusinessException("请先保存提供商。");
    }
    private ModelRow RequireRow() => SelectedModel is { } row && row.SourceId == _editingId ? row : throw new BusinessException("请选择当前提供商的模型。");
    private ModelRow RequireLocalRow() => RequireRow() is { LocalId: not null } row ? row : throw new BusinessException("请先将模型添加到本地。");
    private async Task Save(CancellationToken ct)
    {
        var source = Current();
        var changed = _catalog.Sources.FirstOrDefault(x => x.Id == source.Id) is { } old && old != source;
        _catalog = await _service.SaveSourceAsync(_catalog, source, ApiKey, ct);
        if (changed) _remote.Remove(source.Id);
        if (!string.IsNullOrWhiteSpace(ApiKey)) _keyScopes.Add(source.Settings().SecretScope);
        _hydrating = true; ApiKey = ""; _hydrating = false; IsDirty = false; _drafts.Remove(source.Id); if (_newDraftId == source.Id) _newDraftId = null;
        RefreshSources(); RefreshRows();
    }
    private async Task Discover(CancellationToken ct)
    {
        var source = SavedSource(); _remote.Remove(source.Id); RefreshRows();
        Status = "配置已保存，正在获取远端模型…";
        var rows = await _discovery.DiscoverAsync(source, ct);
        _remote[source.Id] = rows; RefreshRows();
        Status = rows.Count == 0 ? "配置已保存；远端未返回可用模型，可填写自定义模型 ID。" : $"已发现 {rows.Count} 个模型。选择后添加到本地。";
    }
    private async Task DeleteSource(CancellationToken ct)
    {
        var source = _catalog.Sources.SingleOrDefault(x => x.Id == _editingId) ?? throw new BusinessException("尚未保存的草稿可直接放弃。");
        if (!_confirmDelete($"删除提供商“{source.Name}”及其本地模型、草稿和当前地址密钥？")) return;
        var models = _catalog.Models.Where(x => x.SourceId != source.Id).ToArray();
        await Persist(_catalog with { Sources = _catalog.Sources.Where(x => x.Id != source.Id).ToArray(), Models = models, ActiveModelId = models.Any(x => x.Id == _catalog.ActiveModelId) ? _catalog.ActiveModelId : null }, ct);
        _drafts.Remove(source.Id); _remote.Remove(source.Id);
        _selectedSource = Sources.FirstOrDefault(); Raise(nameof(SelectedSource)); OpenDraft(_selectedSource?.Id ?? Guid.NewGuid(), _selectedSource);
        Status = "提供商及关联模型已删除。";
        try { await _secrets.DeleteAsync(source.Settings().SecretScope, ct); _keyScopes.Remove(source.Settings().SecretScope); }
        catch { Status = "提供商及模型已删除，但旧密钥文件清理失败；该密钥已不再被使用。"; }
    }
    private async Task Persist(ProviderCatalog next, CancellationToken ct) { await _service.SaveAsync(next, ct); _catalog = next; RefreshSources(); RefreshRows(); }
    private void RefreshSources()
    {
        _hydrating = true;
        foreach (var removed in Sources.Where(x => !_catalog.Sources.Any(s => s.Id == x.Id)).ToArray()) Sources.Remove(removed);
        foreach (var source in _catalog.Sources)
        {
            var existing = Sources.FirstOrDefault(x => x.Id == source.Id);
            if (existing is null) Sources.Add(source);
            else if (existing != source) Sources[Sources.IndexOf(existing)] = source;
        }
        _selectedSource = Sources.FirstOrDefault(x => x.Id == _editingId); Raise(nameof(SelectedSource)); _hydrating = false; Raise(nameof(SourceEmpty)); Raise(nameof(ActiveModel));
    }
    private void RefreshRows()
    {
        var selected = SelectedModel?.ModelId;
        var local = _catalog.Models.Where(x => x.SourceId == _editingId).ToDictionary(x => x.ModelId, StringComparer.Ordinal);
        var remote = (_remote.GetValueOrDefault(_editingId) ?? []).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        Models.Clear();
        foreach (var id in local.Keys.Union(remote.Keys).OrderBy(x => x, StringComparer.Ordinal))
        { var model = local.GetValueOrDefault(id); Models.Add(new(_editingId, id, remote.GetValueOrDefault(id)?.Name ?? id, model?.Id, model?.Enabled ?? false, remote.ContainsKey(id), model is not null && model.Id == _catalog.ActiveModelId)); }
        SelectedModel = Models.FirstOrDefault(x => x.ModelId == selected); Raise(nameof(ModelSummary)); Raise(nameof(ActiveModel));
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true; using var request = new CancellationTokenSource(); _request = request;
        try { await action(request.Token); }
        catch (OperationCanceledException) { Status = "操作已取消；已保存的配置和本地模型保留。"; }
        catch (AIProviderException e) { Status = "已保存的配置保留。" + AIErrorMessages.For(e.Kind); }
        catch (BusinessException e) { Status = e.Message; }
        catch { Status = IsDirty ? "保存未完成；草稿保留，请检查配置并重试。" : "操作失败；已保存的配置保留，请重试。"; }
        finally { _request = null; IsBusy = false; }
    }
    public override async Task LoadAsync()
    {
        if (_loaded || IsBusy) return;
        await Run(async ct =>
        {
            _catalog = await _service.LoadAsync(ct);
            foreach (var source in _catalog.Sources)
            { try { if (!string.IsNullOrWhiteSpace(await _secrets.ReadAsync(source.Settings().SecretScope, ct))) _keyScopes.Add(source.Settings().SecretScope); } catch (OperationCanceledException) { throw; } catch { /* Unreadable key can be replaced in the editor. */ } }
            RefreshSources(); _selectedSource = Sources.FirstOrDefault(); Raise(nameof(SelectedSource));
            OpenDraft(_selectedSource?.Id ?? _editingId, _selectedSource); _loaded = true;
        });
    }
}
