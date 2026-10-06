using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Theme;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Infrastructure.Services;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;
internal static class Phase8Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var main=services.GetRequiredService<MainViewModel>(); await main.NavigateCommand.ExecuteAsync("Settings"); var settings=services.GetRequiredService<SettingsViewModel>();
        await settings.Maintenance.BackupCommand.ExecuteAsync();
        if(!settings.Maintenance.Status.Contains("备份已验证")) throw new InvalidOperationException("Manual backup command failed.");
        var backup=services.GetRequiredService<BackupService>(); var daily=await backup.CreateAsync(true); if(daily!=await backup.CreateAsync(true)) throw new InvalidOperationException("Daily duplicate.");
        var account=services.GetRequiredService<WorkspaceContext>().SelectedAccount!; var security=(await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(s=>s.Symbol=="TEST");
        var export=services.GetRequiredService<ExportService>(); var dir=Path.Combine(paths.Root,"Exports"); Directory.CreateDirectory(dir);
        foreach(var kind in Enum.GetValues<ExportKind>()) { var ext=kind.ToString().EndsWith("Csv",StringComparison.Ordinal)?"csv":kind==ExportKind.ResearchJson?"json":"md"; await export.ExportAsync(kind,account.Id,security.Id,Path.Combine(dir,kind+"."+ext)); }
        var demo=services.GetRequiredService<DemoService>(); await demo.PrepareAsync(); await demo.PrepareAsync(true); await demo.DeleteAsync();
        if(Directory.Exists(demo.Root)) throw new InvalidOperationException("Demo deletion failed.");
        var theme=services.GetRequiredService<ThemeService>(); var original=theme.Current; var width=window.Width; var height=window.Height; var keyboardSteps=0;
        var contrasts=new Dictionary<string,double>();
        foreach(var t in new[]{AppTheme.Light,AppTheme.Dark})
        {
            theme.Apply(t); var bg=((SolidColorBrush)window.FindResource("SurfaceBrush")).Color;
            foreach(var key in new[]{"TextBrush","MutedBrush"}) { var ratio=Contrast(((SolidColorBrush)window.FindResource(key)).Color,bg); contrasts[$"{t}/{key}"]=ratio; if(ratio<4.5) throw new InvalidOperationException("Insufficient text contrast."); }
            foreach(var scale in new[]{1.25,1.5,2.0})
            {
                window.Width=1920/scale; window.Height=1080/scale; await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ContextIdle);
                var content=(FrameworkElement)window.Content; var bitmap=new RenderTargetBitmap((int)(content.ActualWidth*scale),(int)(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(Path.Combine(paths.Root,$"Accessibility-{t}-{scale*100:0}.png")); encoder.Save(file);
                window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                for(var i=0;i<12;i++) { if(Keyboard.FocusedElement is UIElement el && el.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))) keyboardSteps++; }
            }
        }
        if(keyboardSteps<12) throw new InvalidOperationException("Keyboard traversal failed.");
        window.Width=width; window.Height=height; theme.Apply(original);
        var reopened=File.Exists(Path.Combine(paths.Root,"phase8-flow.json"));
        if(reopened && !Directory.GetFiles(paths.BackupsDirectory,"before-restore-*.db").Any()) throw new InvalidOperationException("Restore on restart missing safety backup.");
        if(!reopened) await backup.StageRestoreAsync(await backup.CreateAsync());
        await File.WriteAllTextAsync(Path.Combine(paths.Root,reopened?"phase8-restart.json":"phase8-flow.json"),JsonSerializer.Serialize(new { Passed=true,Reopened=reopened,Backup=true,DeferredRestoreStaged=!reopened,DemoIsolation=true,Exports=8,KeyboardSteps=keyboardSteps,Contrast=contrasts,DpiRenderScales=new[]{125,150,200},PhysicalMonitorSwitchTested=false },new JsonSerializerOptions {WriteIndented=true}));
    }
    private static double Contrast(Color a,Color b) { static double L(Color c) { static double F(byte n) {var v=n/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);} return .2126*F(c.R)+.7152*F(c.G)+.0722*F(c.B); } var x=L(a);var y=L(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05); }
}

