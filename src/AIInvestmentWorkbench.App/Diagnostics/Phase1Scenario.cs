using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Navigation;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Application.Services;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AIInvestmentWorkbench.App.Diagnostics;
internal static class Phase1Scenario
{
    public static async Task RunAsync(IServiceProvider services, Window mainWindow, AppPaths paths)
    {
        var portfolio = services.GetRequiredService<PortfolioApplicationService>();
        var securities = services.GetRequiredService<SecurityApplicationService>();
        var dialogs = services.GetRequiredService<WorkbenchDialogs>();
        var workspace = services.GetRequiredService<WorkspaceContext>();
        var reopened = workspace.Accounts.Count > 0;
        var importer = services.GetRequiredService<NavigationService>().Pages.OfType<ImportViewModel>().Single();
        if (!reopened)
        {
            Guid accountId = Guid.Empty, securityId = Guid.Empty;
            var accountForm = dialogs.AccountEditor(null, id => accountId = id, initial: true);
            await RenderFormAsync(accountForm, mainWindow, Path.Combine(paths.Root, "AccountWizard.png"));
            await SaveAsync(accountForm);
            if (accountId == Guid.Empty) throw new InvalidOperationException("Account form did not persist.");
            await workspace.ReloadAsync(accountId);
            var securityForm = dialogs.SecurityEditor(null, id => securityId = id);
            Fill(securityForm, ("Ticker", "TEST"), ("Name", "验证用股份"), ("Sector", "工业"), ("Industry", "测试行业"), ("Country", "中国"));
            await RenderFormAsync(securityForm, mainWindow, Path.Combine(paths.Root, "SecurityEditor.png"));
            await SaveAsync(securityForm);
            var form = await dialogs.TransactionEditorAsync(workspace.SelectedAccount!, securityId);
            Fill(form, ("Quantity", "100"), ("Price", "10"), ("Fees", "5"));
            await RenderFormAsync(form, mainWindow, Path.Combine(paths.Root, "TransactionEditor.png"));
            await SaveAsync(form);
            form = await dialogs.TransactionEditorAsync(workspace.SelectedAccount!, securityId);
            Fill(form, ("Quantity", "100"), ("Price", "12"), ("Fees", "5")); await SaveAsync(form);
            form = await dialogs.TransactionEditorAsync(workspace.SelectedAccount!, securityId);
            Fill(form, ("Type", "Sell"), ("Quantity", "50"), ("Price", "15"), ("Fees", "2")); await SaveAsync(form);
            var holding = (await portfolio.ReadAsync(accountId)).Holdings.Single();
            form = dialogs.PriceEditor(workspace.SelectedAccount!, holding);
            Fill(form, ("Price", "14"), ("Target", "10"), ("Max", "20")); await SaveAsync(form);
            form = await dialogs.WatchlistEditorAsync();
            Fill(form, ("Security", securityId.ToString()), ("Stage", "Researching"), ("Priority", "High"), ("Reason", "验证研究流程"), ("Next", "阅读财报")); await SaveAsync(form);
            var date = DateTime.Today.ToString("yyyy-MM-dd");
            await ImportAsync(ImportKind.Security, "Ticker,CompanyName,Market,Exchange,Currency,SecurityType\nTESTETF,验证用ETF,ChinaA,XSHG,CNY,Etf");
            await ImportAsync(ImportKind.Positions, $"Ticker,Exchange,Quantity,AverageCost,Date,Currency\nTESTETF,XSHG,100,20,{date},CNY");
            await ImportAsync(ImportKind.Transactions, $"Ticker,Exchange,Type,Date,Quantity,Price,Amount,Fees,Currency\nTESTETF,XSHG,Sell,{date},10,25,0,1,CNY\n,,Deposit,{date},0,0,500,0,CNY");
        }
        await workspace.ReloadAsync();
        var snapshot = await portfolio.ReadAsync(workspace.SelectedAccount!.Id);
        if (snapshot.TotalAssets != 501187m || snapshot.Cash != 497287m || snapshot.PositionCount != 2 || snapshot.WatchlistCount != 1 || snapshot.Transactions.Count != 6)
            throw new InvalidOperationException($"Unexpected persisted portfolio: assets={snapshot.TotalAssets}, cash={snapshot.Cash}, positions={snapshot.PositionCount}, transactions={snapshot.Transactions.Count}.");
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "restart-verification.json" : "portfolio-flow.json"), JsonSerializer.Serialize(new
        {
            Passed = true, Reopened = reopened, snapshot.TotalAssets, snapshot.Cash, snapshot.InvestedAssets,
            snapshot.UnrealizedPnL, snapshot.PositionCount, snapshot.WatchlistCount, TransactionCount = snapshot.Transactions.Count,
            CheckedAt = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions { WriteIndented = true }));

        async Task ImportAsync(ImportKind kind, string text)
        {
            var path = Path.Combine(paths.Root, $"sample-{kind}.csv");
            await File.WriteAllTextAsync(path, text); importer.Kind = kind; await importer.LoadFileAsync(path);
            await importer.ValidateAsync();
            if (!importer.CanImport) throw new InvalidOperationException(importer.Report);
            await importer.CommitAsync();
            if (!importer.Report.StartsWith("成功导入", StringComparison.Ordinal)) throw new InvalidOperationException(importer.Report);
        }
    }
    private static void Fill(EditorViewModel form, params (string Key, string Value)[] values)
    { foreach (var value in values) form.Fields.Single(x => x.Key == value.Key).Value = value.Value; }
    private static async Task SaveAsync(EditorViewModel form)
    {
        await form.SaveCommand.ExecuteAsync();
        if (form.Error.Length > 0) throw new InvalidOperationException(form.Error);
    }
    private static async Task RenderFormAsync(EditorViewModel form, Window owner, string path)
    {
        var window = new EditorWindow(form) { Owner = owner }; window.Show();
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        SmokeTest.SaveRender(window, path); window.Close();
    }
}
