using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIInvestmentWorkbench.App.ViewModels;
using AIInvestmentWorkbench.Application.AI;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Infrastructure.Persistence;
using AIInvestmentWorkbench.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace AIInvestmentWorkbench.App.Diagnostics;
internal static class PerformanceScenario
{
    internal static async Task RunAsync(IServiceProvider services,Window window,AppPaths paths)
    {
        var portfolio=services.GetRequiredService<IPortfolioStore>(); var factory=services.GetRequiredService<IDbContextFactory<InvestmentDbContext>>(); var stats=new Dictionary<string,double>();
        var watch=Stopwatch.StartNew();
        await Task.Run(async ()=>
        {
            if((await portfolio.AccountsAsync()).Count>0) return;
            var id=await portfolio.SaveAccountAsync(new(null,"PERF · 合成压力测试","CNY",500000,.1m,.4m,.4m,.1m,.15m));
            var securities=Enumerable.Range(1,600).Select(i=>new SecurityDraft(null,$"PERF{i:0000}",$"合成证券 {i}",Market.ChinaA,"PERF","CNY",SecurityType.Stock)).ToArray();
            var imported=await portfolio.ImportAsync(new(ImportKind.Security,id,"perf-securities",securities,[],[]),false); if(!imported.Success) throw new InvalidOperationException("Performance security seed failed.");
            var tx=Enumerable.Range(0,1500).Select(i=>new ImportTransaction($"PERF{i%600+1:0000}","PERF",TransactionType.Buy,DateTimeOffset.UtcNow.AddDays(-30).AddSeconds(i),1,10,0,0,"CNY","合成性能样本")).ToArray();
            imported=await portfolio.ImportAsync(new(ImportKind.Transactions,id,"perf-transactions",[],[],tx),false); if(!imported.Success) throw new InvalidOperationException("Performance transaction seed failed.");
            await using var db=await factory.CreateDbContextAsync(); var all=await db.Securities.ToListAsync();
            foreach(var s in all) foreach(var year in Enumerable.Range(2020,6)) foreach(var metric in new[]{MetricType.Revenue,MetricType.NetIncome,MetricType.OperatingCashFlow,MetricType.Capex}) db.FinancialMetrics.Add(new(s.Id,year.ToString(),PeriodType.Annual,metric,10000,"CNY"));
            await db.SaveChangesAsync();
            var template=await db.PromptTemplates.FirstAsync();
            for(var i=0;i<100;i++) { var a=new AIAnalysis(all.Single(s=>s.Symbol=="PERF0001").Id,AnalysisType.CompanyOverview,template.Id,"合成报告提示","fake","Offline", "[]","{}","{}",id); a.Finish(AIAnalysisStatus.InvalidOutput,new string('研',50000),"","合成性能报告"); db.AIAnalyses.Add(a); }
            var report=new AIResearchReport(all.Single(s=>s.Symbol=="PERF0001").Id,"PERF 长篇报告（合成数据）");
            report.Update("[]",string.Join("\n\n",Enumerable.Range(0,1500).Select(i=>$"## 第 {i} 条研究观察\n合成压力测试内容，不能用于投资决策。需要用户核实来源、假设和风险。")),10,AIAnalysisStatus.Completed); db.AIResearchReports.Add(report);
            await db.SaveChangesAsync();
        }); stats["SeedMs"]=watch.Elapsed.TotalMilliseconds;
        await services.GetRequiredService<WorkspaceContext>().ReloadAsync();
        services.GetRequiredService<ResearchSelection>().SecurityId=(await services.GetRequiredService<ISecurityRepository>().SearchAsync("PERF0001")).Single().Id;
        var main=services.GetRequiredService<MainViewModel>(); var delays=new List<double>(); var last=Stopwatch.GetTimestamp();
        var timer=new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(50) }; timer.Tick+=(_,_)=>{ var now=Stopwatch.GetTimestamp();delays.Add(Stopwatch.GetElapsedTime(last,now).TotalMilliseconds);last=now; }; timer.Start();
        foreach(var key in new[]{"Dashboard","Portfolio","Securities","Financial","Risk","Journal","AIResearch"})
        {
            delays.Clear(); last=Stopwatch.GetTimestamp(); watch.Restart(); await main.NavigateCommand.ExecuteAsync(key); await window.Dispatcher.InvokeAsync(window.UpdateLayout,DispatcherPriority.ContextIdle); stats[key+"LoadMs"]=watch.Elapsed.TotalMilliseconds; await Task.Delay(75); stats[key+"MaxUiPulseMs"]=delays.DefaultIfEmpty(0).Max();
            if(key=="AIResearch") { var ai=(AIResearchViewModel)main.Navigation.Current!; if(ai.Analyses.Count!=100 || ai.SelectedReport is null) throw new InvalidOperationException("Large AI history did not load."); watch.Restart(); await Phase2Scenario.CaptureTabsAsync(window, paths.Root, "PerformanceAI", "Light"); stats["LargeReportRenderAndCaptureMs"]=watch.Elapsed.TotalMilliseconds; }
        }
        timer.Stop(); await using var verify=await factory.CreateDbContextAsync();
        await File.WriteAllTextAsync(Path.Combine(paths.Root,"performance.json"),JsonSerializer.Serialize(new { Securities=await verify.Securities.CountAsync(),Transactions=await verify.Transactions.CountAsync(),FinancialMetrics=await verify.FinancialMetrics.CountAsync(),AIReports=await verify.AIAnalyses.CountAsync(),ResearchReports=await verify.AIResearchReports.CountAsync(),Stats=stats,MaxUiPulseDelayMs=stats.Where(x=>x.Key.EndsWith("MaxUiPulseMs",StringComparison.Ordinal)).Max(x=>x.Value),Passed=stats.Where(x=>x.Key!="SeedMs").All(x=>x.Value<10000)},new JsonSerializerOptions {WriteIndented=true}));
    }
}



