using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.Dialogs;
using AIInvestmentWorkbench.App.Navigation;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
namespace AIInvestmentWorkbench.App.Diagnostics;

internal static class Phase2Scenario
{
    internal static async Task RunAsync(IServiceProvider services, Window window, AppPaths paths)
    {
        var store = services.GetRequiredService<IResearchStore>(); var dialogs = services.GetRequiredService<ResearchDialogs>();
        var security = (await services.GetRequiredService<ISecurityRepository>().SearchAsync("TEST")).Single(x => x.Symbol == "TEST");
        var selection = services.GetRequiredService<ResearchSelection>(); selection.SecurityId = security.Id;
        var navigation = services.GetRequiredService<NavigationService>();
        var financial = navigation.Pages.OfType<FinancialViewModel>().Single();
        var initial = await store.ReadAsync(security.Id); var reopened = initial.Research is not null;
        if (!reopened)
        {
            var form = dialogs.SectionEditor(security.Id, null, "Overview"); Fill(form, "Body", "验证用股份 · 虚构研究资料\n主营业务：为工业企业提供设备和服务。\n研究问题：现金转换、库存周转与资本配置。\n本页面内容仅用于软件验收，不是投资建议。");
            await RenderForm(form, "ResearchEditor.png");
            await Save(form);
            var research = (await store.ReadAsync(security.Id)).Research;
            form = dialogs.SectionEditor(security.Id, research, "UserNotes"); Fill(form, "Body", "Phase2 evidence：核对原始财报，不让 AI 代替基础计算。"); await Save(form);
            var fileReader = services.GetRequiredService<IResearchFileReader>();
            foreach (var extension in new[] { ".txt", ".md", ".csv", ".pdf" })
            {
                var path = Path.Combine(paths.Root, "research-source" + extension);
                if (extension == ".pdf")
                {
                    var pdf = new PdfDocumentBuilder(); var page = pdf.AddPage(PageSize.A4); var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
                    page.AddText("Phase2 evidence: fictional annual report 2025", 12, new PdfPoint(25, 700), font); await File.WriteAllBytesAsync(path, pdf.Build());
                }
                else await File.WriteAllTextAsync(path, "Phase2 evidence：用于测试的研究资料，收入与现金流需核对来源。\n来源类型 " + extension);
                form = dialogs.SourceEditor(security.Id, path: path, text: await fileReader.ReadAsync(path));
                Fill(form, "Title", "虚构研究来源 " + extension); Fill(form, "Publisher", "验证资料"); Fill(form, "Date", "2026-01-01"); Fill(form, "Level", "1"); await Save(form);
            }
            await financial.LoadAsync();
            var csv = "Period,PeriodType,MetricType,Value,Currency,IsEstimated,Notes\n";
            for (var i = 0; i < 3; i++)
            {
                var values = new Dictionary<MetricType, decimal> { [MetricType.Revenue] = 1000000 + i * 100000, [MetricType.GrossProfit] = 400000 + i * 30000,
                    [MetricType.OperatingIncome] = 180000 - i * 10000, [MetricType.NetIncome] = 120000, [MetricType.OperatingCashFlow] = 60000 - i * 10000,
                    [MetricType.Capex] = 70000, [MetricType.Debt] = 200000 + i * i * 100000, [MetricType.AccountsReceivable] = 100000 + i * i * 50000,
                    [MetricType.Inventory] = 80000 + i * i * 40000, [MetricType.ShareCount] = 1000000 + i * 40000, [MetricType.StockBasedCompensation] = 150000 };
                foreach (var value in values) csv += $"{2023 + i},Annual,{value.Key},{value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},CNY,false,虚构财务数据\n";
            }
            var csvPath = Path.Combine(paths.Root, "financial-metrics.csv"); await File.WriteAllTextAsync(csvPath, csv);
            await financial.Import.LoadFileAsync(csvPath); await financial.Import.ExecuteAsync(true);
            if (!financial.Import.CanImport) throw new InvalidOperationException(financial.Import.Report);
            await financial.Import.ExecuteAsync(false);
            await RenderForm(dialogs.MetricEditor(security.Id, "CNY", (await store.ReadAsync(security.Id)).Sources), "FinancialEditor.png");
            var scores = (await store.ReadAsync(security.Id)).Scores;
            form = dialogs.ScoreEditor(security.Id, scores, scores[0]); Fill(form, "Score", "72"); Fill(form, "Reason", "业务具有持续需求，仍需核对竞争优势。"); Fill(form, "Evidence", "Phase2 evidence · 虚构研究来源 .pdf"); await Save(form);
            form = dialogs.WeightsEditor(security.Id, (await store.ReadAsync(security.Id)).Scores); Fill(form, "BusinessModel", "25"); Fill(form, "Valuation", "10"); await Save(form);
            await RenderForm(dialogs.ThresholdsEditor(await store.ReadThresholdsAsync()), "RiskThresholdsEditor.png");
        }
        var snapshot = await store.ReadAsync(security.Id);
        if (snapshot.Research?.Revision != 2 || snapshot.Metrics.Count != 33 || snapshot.Sources.Count != 4 || snapshot.Scores[0].UserScore != 72 || snapshot.Scores[0].Weight != 25)
            throw new InvalidOperationException("Research persistence verification failed.");
        await financial.LoadAsync();
        if (financial.Formulas.Last().FreeCashFlow != -30000 || financial.Risks.Count != 7) throw new InvalidOperationException("Financial formulas or rules failed.");
        var search = navigation.Pages.OfType<SearchViewModel>().Single(); search.Query = "Phase2 evidence"; await search.SearchCommand.ExecuteAsync(null);
        if (search.Results.Count < 5) throw new InvalidOperationException("Global search missed research evidence.");
        await File.WriteAllTextAsync(Path.Combine(paths.Root, reopened ? "research-restart.json" : "research-flow.json"), JsonSerializer.Serialize(new { Passed = true, Reopened = reopened, snapshot.Research.Revision, Metrics = snapshot.Metrics.Count, Sources = snapshot.Sources.Count, UserScore = snapshot.Scores[0].UserScore, Risks = financial.Risks, SearchHits = search.Results.Count }, new JsonSerializerOptions { WriteIndented = true }));
        static void Fill(EditorViewModel form, string key, string value) => form.Fields.Single(x => x.Key == key).Value = value;
        static async Task Save(EditorViewModel form) { await form.SaveCommand.ExecuteAsync(null); if (form.Error.Length > 0) throw new InvalidOperationException(form.Error); }
        async Task RenderForm(EditorViewModel form, string name)
        {
            var editor = new EditorWindow(form) { Owner = window }; editor.Show();
            await window.Dispatcher.InvokeAsync(editor.UpdateLayout, DispatcherPriority.ContextIdle);
            SmokeTest.SaveRender(editor, Path.Combine(paths.Root, name)); editor.Close();
        }
    }
    internal static async Task CaptureTabsAsync(Window window, string root, string page, string theme)
    {
        var tabs = FindTabs(window); if (tabs is null) return;
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i; await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            SmokeTest.SaveRender(window, Path.Combine(root, $"{page}-Tab{i}-{theme}.png"));
        }
        tabs.SelectedIndex = 0;
    }
    private static TabControl? FindTabs(DependencyObject parent)
    {
        if (parent is TabControl tabs) return tabs;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        { var result = FindTabs(System.Windows.Media.VisualTreeHelper.GetChild(parent, i)); if (result is not null) return result; }
        return null;
    }
}
