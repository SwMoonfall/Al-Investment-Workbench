using AIInvestmentWorkbench.Domain.Rules;

namespace AIInvestmentWorkbench.Application.AI;

public static class ProviderTypes
{
    public static readonly string[] All = ["OpenAI", "GoogleGenAI", "Anthropic", "OpenAICompatible"];
    public static string DefaultUrl(string type) => type switch
    {
        "GoogleGenAI" => "https://generativelanguage.googleapis.com/v1beta/",
        "Anthropic" => "https://api.anthropic.com/v1/",
        _ => "https://api.openai.com/v1/"
    };
    public static string NormalizeModel(string type, string id) => type == "GoogleGenAI" && id.Trim().StartsWith("models/", StringComparison.Ordinal) ? id.Trim()[7..] : id.Trim();
}

public sealed record ProviderSource(Guid Id, string Name, string Type, string BaseUrl = "", int TimeoutSeconds = 90,
    int MaxOutputTokens = 4096, string ApiVersion = "2023-06-01", string Organization = "", string Project = "", string CompatibilityApi = "ChatCompletions")
{
    public AISettings Settings(string model = "") => new(Type, model, TimeoutSeconds, MaxOutputTokens, BaseUrl, Id, ApiVersion, Organization, Project, CompatibilityApi);
    public void Validate()
    {
        if (!ProviderTypes.All.Contains(Type)) throw new BusinessException("请选择支持的提供商类型。");
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl)) throw new BusinessException("请填写有效的提供商名称（最多 100 字）。");
        Settings().Validate();
    }
}
public sealed record ProviderModel(Guid Id, Guid SourceId, string ModelId, bool Enabled = true);
public sealed record RemoteModel(string Id, string Name);
public sealed record ProviderCatalog(ProviderSource[] Sources, ProviderModel[] Models, Guid? ActiveModelId = null)
{
    public static ProviderCatalog Empty => new([], []);
    public AISettings ActiveSettings => Models.FirstOrDefault(x => x.Id == ActiveModelId && x.Enabled) is { } model
        && Sources.FirstOrDefault(x => x.Id == model.SourceId) is { } source ? source.Settings(model.ModelId) : new();
    public void Validate()
    {
        foreach (var source in Sources) source.Validate();
        if (Sources.Select(x => x.Id).Distinct().Count() != Sources.Length || Models.Select(x => x.Id).Distinct().Count() != Models.Length) throw new BusinessException("配置 ID 重复。");
        foreach (var model in Models)
        {
            var source = Sources.SingleOrDefault(x => x.Id == model.SourceId) ?? throw new BusinessException("模型关联的提供商不存在。");
            source.Settings(model.ModelId).Validate(true);
            if (model.Id == Guid.Empty || ProviderTypes.NormalizeModel(source.Type, model.ModelId) != model.ModelId) throw new BusinessException("模型 ID 格式无效。");
        }
        if (Models.GroupBy(x => (x.SourceId, x.ModelId)).Any(x => x.Count() > 1)) throw new BusinessException("该提供商已配置相同模型。");
        if (ActiveModelId is not null && !Models.Any(x => x.Id == ActiveModelId && x.Enabled)) throw new BusinessException("当前模型不存在或已禁用。");
    }
}
public interface IProviderCatalogStore
{
    Task<ProviderCatalog?> ReadCatalogAsync(CancellationToken ct = default);
    Task SaveCatalogAsync(ProviderCatalog catalog, CancellationToken ct = default);
}
public interface IModelDiscovery
{
    Task<IReadOnlyList<RemoteModel>> DiscoverAsync(ProviderSource source, CancellationToken ct = default);
}

// Coordinates migration and catalog mutations; callers never attach models to a draft ID.
public sealed class ProviderCatalogService(IProviderCatalogStore store, IAIStore legacy, ISecretStore secrets)
{
    public async Task<ProviderCatalog> LoadAsync(CancellationToken ct = default)
    {
        if (await store.ReadCatalogAsync(ct) is { } catalog) return catalog;
        var old = await legacy.ReadSettingsAsync(ct);
        if (old.Provider == "Disabled") return ProviderCatalog.Empty;
        var source = new ProviderSource(Guid.NewGuid(), "已迁移的 " + old.Provider, old.Provider, old.BaseUrl, old.TimeoutSeconds, old.MaxOutputTokens);
        var model = new ProviderModel(Guid.NewGuid(), source.Id, old.Model.Trim());
        string? key;
        try { key = await secrets.ReadAsync(old.SecretScope, ct); }
        catch (BusinessException) { key = null; } // A DPAPI key from another Windows user must be replaceable.
        if (!string.IsNullOrWhiteSpace(key)) await secrets.WriteAsync(source.Settings().SecretScope, key, ct);
        catalog = new([source], string.IsNullOrWhiteSpace(model.ModelId) ? [] : [model], string.IsNullOrWhiteSpace(model.ModelId) ? null : model.Id);
        await store.SaveCatalogAsync(catalog, ct);
        return catalog;
    }
    public async Task<ProviderCatalog> SaveSourceAsync(ProviderCatalog catalog, ProviderSource source, string? key, CancellationToken ct = default)
    {
        source.Validate();
        var old = catalog.Sources.SingleOrDefault(x => x.Id == source.Id);
        var models = old is not null && old.Type != source.Type ? catalog.Models.Where(x => x.SourceId != source.Id).ToArray() : catalog.Models;
        var next = catalog with { Sources = catalog.Sources.Where(x => x.Id != source.Id).Append(source).ToArray(), Models = models,
            ActiveModelId = models.Any(x => x.Id == catalog.ActiveModelId) ? catalog.ActiveModelId : null };
        next.Validate();
        var scope = source.Settings().SecretScope;
        string? previousKey = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            try { previousKey = await secrets.ReadAsync(scope, ct); } catch (BusinessException) { /* Allow replacing an unreadable key. */ }
            await secrets.WriteAsync(scope, key.Trim(), ct);
        }
        try { await store.SaveCatalogAsync(next, ct); }
        catch
        {
            // Restore credentials if the database transaction fails or is cancelled.
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (previousKey is null) await secrets.DeleteAsync(scope, CancellationToken.None);
                else await secrets.WriteAsync(scope, previousKey, CancellationToken.None);
            }
            throw;
        }
        return next;
    }
    public Task SaveAsync(ProviderCatalog catalog, CancellationToken ct = default) { catalog.Validate(); return store.SaveCatalogAsync(catalog, ct); }
    public static ProviderCatalog AddModel(ProviderCatalog catalog, Guid sourceId, string id)
    {
        var source = catalog.Sources.SingleOrDefault(x => x.Id == sourceId) ?? throw new BusinessException("请先保存提供商。");
        id = ProviderTypes.NormalizeModel(source.Type, id);
        source.Settings(id).Validate(true);
        var existing = catalog.Models.SingleOrDefault(x => x.SourceId == sourceId && x.ModelId == id);
        return catalog with { Models = existing is null ? [.. catalog.Models, new(Guid.NewGuid(), sourceId, id)]
            : catalog.Models.Select(x => x.Id == existing.Id ? x with { Enabled = true } : x).ToArray() };
    }
}
