using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Theme;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIInvestmentWorkbench.App.Diagnostics;

/// <summary>Opt-in real WPF / SQLite smoke check against a caller-supplied isolated directory.</summary>
internal static class SmokeTest
{
    public static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        await Phase1Scenario.RunAsync(services, window, paths);
        await Phase2Scenario.RunAsync(services, window, paths);
        await Phase3Scenario.RunAsync(services, window, paths);
        await Phase4Scenario.RunAsync(services, window, paths);
        await Phase5Scenario.RunAsync(services, window, paths);
        await Phase6Scenario.RunAsync(services, window, paths);
        await Phase7Scenario.RunAsync(services, window, paths);
        await Phase8Scenario.RunAsync(services, window, paths);
        var main = services.GetRequiredService<MainViewModel>();
        var visited = new List<string>();
        foreach (var key in new[] { "Dashboard", "Portfolio", "Securities", "Watchlist", "Import", "Research", "Financial", "Thesis", "Valuation", "Risk", "AIResearch", "Journal", "Search", "Settings" })
        {
            await main.NavigateCommand.ExecuteAsync(key);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            if (main.Navigation.Current?.Key != key) throw new InvalidOperationException("Navigation failed.");
            var expected = $"{key}View";
            if (!Descendants(window).OfType<UserControl>().Any(x => x.GetType().Name == expected))
                throw new InvalidOperationException("View template did not render.");
            visited.Add(key);
        }
        var settings = services.GetRequiredService<SettingsViewModel>();
        var store = services.GetRequiredService<ISettingsService>();
        var original = settings.SelectedTheme;
        var themes = new List<string>();
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            await settings.ChangeThemeCommand.ExecuteAsync(theme.ToString());
            window.UpdateLayout();
            if (services.GetRequiredService<ThemeService>().Current != theme || await store.GetThemeAsync() != theme)
                throw new InvalidOperationException("Theme persistence failed.");
            var background = ((SolidColorBrush)window.Background).Color;
            var expected = theme == AppTheme.Dark ? "#FF101B22" : "#FFF3F5F7";
            if (background.ToString() != expected) throw new InvalidOperationException("Theme resource did not update.");
            foreach (var key in new[] { "Dashboard", "Portfolio", "Securities", "Watchlist", "Import", "Research", "Financial", "Thesis", "Valuation", "Risk", "AIResearch", "Journal", "Search", "Settings" })
            {
                await main.NavigateCommand.ExecuteAsync(key);
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
                if (key == "AIResearch" && main.Navigation.Current is AIResearchViewModel ai) ai.SelectedAnalysis = ai.Analyses.FirstOrDefault(x => x.Status == AIInvestmentWorkbench.Domain.Entities.AIAnalysisStatus.Completed);
                SaveRender(window, Path.Combine(paths.Root, $"{key}-{theme}.png"));
                if (key is "Research" or "Financial" or "Thesis" or "Risk" or "AIResearch" or "Journal") await Phase2Scenario.CaptureTabsAsync(window, paths.Root, key, theme.ToString());
            }
            themes.Add(theme.ToString());
        }
        await settings.ChangeThemeCommand.ExecuteAsync(original.ToString());
        var factory = services.GetRequiredService<IDbContextFactory<InvestmentDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        if (migrations.Length == 0 || !File.Exists(paths.DatabasePath)) throw new InvalidOperationException("Database initialization failed.");
        services.GetRequiredService<ILoggerFactory>().CreateLogger("SmokeTest").LogInformation("Smoke validation passed.");
        if (!Directory.EnumerateFiles(paths.LogsDirectory, "*.log").Any()) throw new InvalidOperationException("File logging failed.");
        await File.WriteAllTextAsync(Path.Combine(paths.Root, "smoke-result.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Visited = visited, Themes = themes, Migrations = migrations, Database = paths.DatabasePath,
            Logs = paths.LogsDirectory, CheckedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static void SaveRender(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}






