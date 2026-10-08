using System.IO;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.AI;
using Xunit;
namespace AIInvestmentWorkbench.App.Tests;

public sealed class AIServicesViewModelTests
{
    [Fact] public async Task SaveThenDiscoverUsesPersistedIdAndFailurePreservesSource()
    {
        var h = new Harness(); await h.Vm.LoadAsync(); h.Vm.SourceName = "source"; h.Vm.ApiKey = "key";
        h.Discovery.Respond = (source, _) => { Assert.Contains(h.Store.Catalog.Sources, x => x.Id == source.Id); throw new AIProviderException(AIErrorKind.Network); };
        await h.Vm.SaveAndDiscoverCommand.ExecuteAsync();
        Assert.Single(h.Store.Catalog.Sources); Assert.False(h.Vm.IsDirty); Assert.False(h.Vm.IsBusy); Assert.Empty(h.Vm.ApiKey); Assert.Contains("配置保留", h.Vm.Status);
        h.Discovery.Respond = (_, _) => Task.FromResult<IReadOnlyList<RemoteModel>>([new("one", "One"), new("one", "Duplicate")]);
        await h.Vm.DiscoverCommand.ExecuteAsync(); Assert.Single(h.Vm.Models);
    }
    [Fact] public async Task DraftsSurviveSourceSwitchNavigationAndRefresh()
    {
        var h = await Harness.WithSource(); var original = h.Vm.SelectedSource!;
        h.Vm.SourceName = "unsaved edit"; h.Vm.NewCommand.Execute(null); h.Vm.SourceName = "new draft";
        h.Vm.SelectedSource = original; Assert.Equal("unsaved edit", h.Vm.SourceName);
        await h.Vm.LoadAsync(); Assert.Equal("unsaved edit", h.Vm.SourceName);
        h.Vm.NewCommand.Execute(null); Assert.Equal("new draft", h.Vm.SourceName);
        Assert.True(h.Vm.HasUnsavedDrafts); h.Vm.DiscardCommand.Execute(null); Assert.Empty(h.Vm.SourceName);
        h.Vm.SelectedSource = original; h.Vm.DiscardCommand.Execute(null); Assert.Equal("Source", h.Vm.SourceName); Assert.False(h.Vm.HasUnsavedDrafts);
    }
    [Fact] public async Task InitialNewDraftCanBeRecoveredWithoutChangingIdentity()
    {
        var h = new Harness(); await h.Vm.LoadAsync(); h.Vm.SourceName = "first draft"; h.Vm.NewCommand.Execute(null);
        Assert.Equal("first draft", h.Vm.SourceName); await h.Vm.SaveCommand.ExecuteAsync(); var id = h.Vm.SelectedSource!.Id;
        h.Vm.SourceName = "rename"; await h.Vm.SaveCommand.ExecuteAsync(); Assert.Equal(id, h.Vm.SelectedSource!.Id); Assert.Single(h.Store.Catalog.Sources);
    }
    [Fact] public async Task DirtyDraftBlocksModelActionsAndTesting()
    {
        var h = await Harness.WithSource(); h.Vm.BaseUrl = "https://new.example/v1"; h.Vm.CustomModelId = "model";
        await h.Vm.AddCustomCommand.ExecuteAsync(); await h.Vm.DiscoverCommand.ExecuteAsync(); await h.Vm.TestModelCommand.ExecuteAsync();
        Assert.Empty(h.Store.Catalog.Models); Assert.Equal(0, h.Discovery.Calls); Assert.Empty(h.Provider.Requests); Assert.Contains("先保存", h.Vm.Status);
    }
    [Fact] public async Task DuplicateAddDisableReenableRemoveAndCurrentModelRemainConsistent()
    {
        var h = await Harness.WithSource(); h.Vm.CustomModelId = " model "; await h.Vm.AddCustomCommand.ExecuteAsync();
        h.Vm.SelectedModel = Assert.Single(h.Vm.Models); var id = h.Vm.SelectedModel.LocalId;
        await h.Vm.AddModelCommand.ExecuteAsync(); Assert.Equal(id, Assert.Single(h.Vm.Models).LocalId);
        await h.Vm.UseModelCommand.ExecuteAsync(); Assert.Equal(id, h.Store.Catalog.ActiveModelId);
        await h.Vm.ToggleModelCommand.ExecuteAsync(); Assert.Null(h.Store.Catalog.ActiveModelId); Assert.False(h.Store.Catalog.Models[0].Enabled);
        await h.Vm.AddModelCommand.ExecuteAsync(); Assert.Equal(id, Assert.Single(h.Store.Catalog.Models).Id); Assert.True(h.Store.Catalog.Models[0].Enabled);
        await h.Vm.TestModelCommand.ExecuteAsync(); Assert.Equal(h.Vm.SelectedSource!.Id, Assert.Single(h.Provider.Requests).Settings.SourceId);
        await h.Vm.RemoveModelCommand.ExecuteAsync(); Assert.Empty(h.Store.Catalog.Models);
    }
    [Fact] public async Task TypeChangeClearsLocalModelsRemoteRowsAndActiveSelection()
    {
        var h = await Harness.WithSource(); await h.Vm.DiscoverCommand.ExecuteAsync(); h.Vm.SelectedModel = h.Vm.Models[0];
        await h.Vm.AddModelCommand.ExecuteAsync(); await h.Vm.UseModelCommand.ExecuteAsync();
        h.Vm.ProviderType = "Anthropic"; Assert.True(h.Vm.IsAnthropic); Assert.False(h.Vm.IsOpenAI);
        await h.Vm.SaveCommand.ExecuteAsync(); Assert.Empty(h.Vm.Models); Assert.Null(h.Store.Catalog.ActiveModelId);
    }
    [Fact] public async Task BusyStateBlocksCompetingActionsAndCancellationRestoresControls()
    {
        var h = await Harness.WithSource(); var started = new TaskCompletionSource();
        h.Discovery.Respond = async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return []; };
        var task = h.Vm.DiscoverCommand.ExecuteAsync(); await started.Task; Assert.True(h.Vm.IsBusy); Assert.False(h.Vm.CanEdit);
        var id = h.Vm.SelectedSource!.Id; h.Vm.NewCommand.Execute(null); await h.Vm.DeleteSourceCommand.ExecuteAsync(); Assert.Equal(id, h.Vm.SelectedSource!.Id);
        h.Vm.CancelCommand.Execute(null); await task; Assert.False(h.Vm.IsBusy); Assert.True(h.Vm.CanEdit); Assert.Contains("取消", h.Vm.Status);
    }
    [Fact] public async Task DeleteCascadesAndDoesNotAffectOtherSourceOrSecrets()
    {
        var h = await Harness.WithSource(); var first = h.Vm.SelectedSource!; h.Vm.CustomModelId = "one"; await h.Vm.AddCustomCommand.ExecuteAsync(); h.Vm.SelectedModel = h.Vm.Models[0]; await h.Vm.UseModelCommand.ExecuteAsync();
        h.Vm.NewCommand.Execute(null); h.Vm.SourceName = "Other"; h.Vm.ApiKey = "other-key"; await h.Vm.SaveCommand.ExecuteAsync(); var second = h.Vm.SelectedSource!;
        h.Vm.SelectedSource = first; await h.Vm.DeleteSourceCommand.ExecuteAsync();
        Assert.Equal(second.Id, Assert.Single(h.Store.Catalog.Sources).Id); Assert.Empty(h.Store.Catalog.Models); Assert.Null(h.Store.Catalog.ActiveModelId);
        Assert.Null(await h.Secrets.ReadAsync(first.Settings().SecretScope)); Assert.Equal("other-key", await h.Secrets.ReadAsync(second.Settings().SecretScope));
    }
    [Fact] public async Task PersistenceFailureRetainsDraftAndCanRetry()
    {
        var h = new Harness(); await h.Vm.LoadAsync(); h.Vm.SourceName = "Draft"; h.Store.Fail = true;
        await h.Vm.SaveCommand.ExecuteAsync(); Assert.True(h.Vm.IsDirty); Assert.Empty(h.Vm.Sources); Assert.False(h.Vm.IsBusy);
        h.Store.Fail = false; await h.Vm.SaveCommand.ExecuteAsync(); Assert.False(h.Vm.IsDirty); Assert.Single(h.Vm.Sources);
    }
    [Fact] public async Task FailedSaveRestoresPreviousKey()
    {
        var h = await Harness.WithSource(); var scope = h.Vm.SelectedSource!.Settings().SecretScope;
        h.Vm.ApiKey = "replacement"; h.Store.Fail = true; await h.Vm.SaveCommand.ExecuteAsync();
        Assert.Equal("key", await h.Secrets.ReadAsync(scope)); Assert.Equal("replacement", h.Vm.ApiKey); Assert.True(h.Vm.IsDirty);
        h.Store.Fail = false; await h.Vm.SaveCommand.ExecuteAsync(); Assert.Equal("replacement", await h.Secrets.ReadAsync(scope));
    }
    private sealed class Harness
    {
        public Store Store { get; } = new(); public Secrets Secrets { get; } = new(); public Discovery Discovery { get; } = new(); public Provider Provider { get; } = new(); public AISettingsViewModel Vm { get; }
        public Harness() => Vm = new(new(Store, null!, Secrets), Secrets, Discovery, Provider, new Errors(), _ => true);
        public static async Task<Harness> WithSource() { var h = new Harness(); await h.Vm.LoadAsync(); h.Vm.SourceName = "Source"; h.Vm.ApiKey = "key"; await h.Vm.SaveCommand.ExecuteAsync(); return h; }
    }
    private sealed class Store : IProviderCatalogStore
    {
        public ProviderCatalog Catalog = ProviderCatalog.Empty; public bool Fail;
        public Task<ProviderCatalog?> ReadCatalogAsync(CancellationToken ct = default) => Task.FromResult<ProviderCatalog?>(Catalog);
        public Task SaveCatalogAsync(ProviderCatalog catalog, CancellationToken ct = default) { if (Fail) throw new IOException(); catalog.Validate(); Catalog = catalog; return Task.CompletedTask; }
    }
    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> ReadAsync(string scope, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(scope));
        public Task WriteAsync(string scope, string secret, CancellationToken ct = default) { _values[scope] = secret; return Task.CompletedTask; }
        public Task DeleteAsync(string scope, CancellationToken ct = default) { _values.Remove(scope); return Task.CompletedTask; }
    }
    private sealed class Discovery : IModelDiscovery
    {
        public int Calls;
        public Func<ProviderSource, CancellationToken, Task<IReadOnlyList<RemoteModel>>> Respond = (_, _) => Task.FromResult<IReadOnlyList<RemoteModel>>([new("remote", "Remote")]);
        public Task<IReadOnlyList<RemoteModel>> DiscoverAsync(ProviderSource source, CancellationToken ct = default) { Calls++; return Respond(source, ct); }
    }
    private sealed class Provider : IAIProvider
    {
        public string Name => "Fake"; public List<AIRequest> Requests { get; } = [];
        public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) { Requests.Add(request); return Task.FromResult(new AIProviderResult("OK")); }
        public Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => CompleteAsync(request, cancellationToken);
    }
    private sealed class Errors : IErrorHandler { public void Report(Exception e, string message) => throw new Xunit.Sdk.XunitException(message); }
}
