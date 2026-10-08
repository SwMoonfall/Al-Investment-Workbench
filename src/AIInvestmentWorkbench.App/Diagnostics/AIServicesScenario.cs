using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.App.Views;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AIInvestmentWorkbench.App.Diagnostics;

internal sealed class AIServicesDiscovery : IModelDiscovery
{
    public Task<IReadOnlyList<RemoteModel>> DiscoverAsync(ProviderSource source, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RemoteModel>>([new("sample-fast", "Sample fast"), new("sample-reasoning", "Sample reasoning"), new("sample-fast", "Duplicate")]);
}

internal sealed class AIServicesTestProvider : IAIProvider
{
    public string Name => "Offline connection test";
    public Task<AIProviderResult> CompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new AIProviderResult("OK"));
    public Task<AIProviderResult> StructuredCompleteAsync(AIRequest request, CancellationToken cancellationToken = default) => CompleteAsync(request, cancellationToken);
}

internal static class AIServicesScenario
{
    public static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var bindings = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var main = services.GetRequiredService<MainViewModel>(); var vm = services.GetRequiredService<AISettingsViewModel>();
        await main.NavigateCommand.ExecuteAsync("AIServices");
        await Idle(); Check(Descendants(window).OfType<AISettingsView>().Any(), "Independent page not rendered");
        SmokeTest.SaveRender(window, Path.Combine(paths.Root, "AIServices-Empty.png"));
        foreach (var type in ProviderTypes.All)
        {
            vm.NewCommand.Execute(null); vm.ProviderType = type; vm.SourceName = type + " 示例连接";
            if (type == "OpenAICompatible") vm.BaseUrl = "https://compatible.example/v1";
            var password = Descendants(window).OfType<PasswordBox>().Single(); password.Password = "fake-offline-test-key";
            await vm.SaveAndDiscoverCommand.ExecuteAsync(); Check(!vm.IsDirty && vm.Models.Count == 2, "Save/discovery failed");
            Check(password.Password.Length == 0, "Password not cleared");
            vm.SelectedModel = vm.Models[0]; await vm.AddModelCommand.ExecuteAsync(); await vm.UseModelCommand.ExecuteAsync();
            await vm.TestModelCommand.ExecuteAsync(); Check(vm.Status.Contains("成功"), "Test failed");
            await Idle();
            Check(Descendants(window).OfType<ListBox>().Single().SelectedItem is ProviderSource selected && selected.Id == vm.SelectedSource?.Id, "Provider list selection lost");
            SmokeTest.SaveRender(window, Path.Combine(paths.Root, $"AIServices-{type}.png"));
        }
        var settings = services.GetRequiredService<SettingsViewModel>();
        foreach (var theme in new[] { "Dark", "Light" })
        {
            await settings.ChangeThemeCommand.ExecuteAsync(theme); await Idle();
            var editor = Descendants(window).OfType<ScrollViewer>().First(x => x.Content is StackPanel panel && panel.Children.OfType<Border>().Count() == 2);
            editor.ScrollToBottom(); await Idle(); SmokeTest.SaveRender(window, Path.Combine(paths.Root, $"AIServices-Models-{theme}.png"));
            editor.ScrollToTop(); await Idle(); SmokeTest.SaveRender(window, Path.Combine(paths.Root, $"AIServices-Editor-{theme}.png"));
        }
        vm.SourceName = "未保存的草稿"; await main.NavigateCommand.ExecuteAsync("Settings"); await main.NavigateCommand.ExecuteAsync("AIServices");
        Check(vm.SourceName == "未保存的草稿" && vm.IsDirty, "Navigation lost draft"); vm.DiscardCommand.Execute(null);
        Check(!vm.HasUnsavedDrafts, "Unintended draft remains");
        Check(bindings.Errors.Count == 0, string.Join("\n", bindings.Errors));
        await File.WriteAllTextAsync(Path.Combine(paths.Root, "ai-services-result.json"), JsonSerializer.Serialize(new { Passed = true, Providers = vm.Sources.Count, BindingErrors = bindings.Errors, Offline = true }, new JsonSerializerOptions { WriteIndented = true }));
        PresentationTraceSources.DataBindingSource.Listeners.Remove(bindings);
        async Task Idle() => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private sealed class BindingErrors : TraceListener
    { public List<string> Errors { get; } = []; public override void Write(string? message) { } public override void WriteLine(string? message) { if (!string.IsNullOrEmpty(message)) Errors.Add(message); } }
}
