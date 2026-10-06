using System.Text;
using System.Text.Json;
using AIInvestmentWorkbench.Application.Interfaces;
using AIInvestmentWorkbench.Domain.Entities;
using AIInvestmentWorkbench.Domain.Enums;
using AIInvestmentWorkbench.Domain.Rules;
using Microsoft.EntityFrameworkCore;
namespace AIInvestmentWorkbench.Infrastructure.Services;

public sealed partial class JournalReviewStore
{
    public async Task<ReviewEvidence> PreviewAsync(Guid accountId, Guid? securityId, ReviewKind kind, DateOnly anchor, CancellationToken ct = default)
    {
        var period = JournalRules.Period(kind, anchor); if (period.Start > DateOnly.FromDateTime(DateTime.Today)) throw new BusinessException("不能生成未来期间复盘。");
        if (kind == ReviewKind.Quarterly && securityId is null) throw new BusinessException("季度复盘请选择证券。");
        if (kind != ReviewKind.Quarterly) securityId = null;
        var p = await portfolio.ReadAsync(accountId, ct); if (p.Account is null) throw new BusinessException("请选择账户。");
        var risk = await risks.ReadAsync(accountId, ct); await using var db = await factory.CreateDbContextAsync(ct);
        var names = await db.Securities.ToDictionaryAsync(x => x.Id, x => x.Symbol + " " + x.Name, ct);
        if (securityId is { } selected && !names.ContainsKey(selected)) throw new BusinessException("证券不存在。");
        var sections = new List<ReviewSection>(); var observed = new Dictionary<string, string>(); var assumptions = new List<AssumptionReview>();
        void Add(string title, IEnumerable<string> rows) { var lines = rows.ToArray(); sections.Add(new(title, lines.Length == 0 ? ["无记录 / 数据不足，不能据此判断无风险。"] : lines)); }
        var captured = DateTimeOffset.UtcNow;
        Add("时间与口径", [$"复盘期间 {period.Start:yyyy-MM-dd}—{period.End:yyyy-MM-dd}；资料读取于 {captured.ToLocalTime():yyyy-MM-dd HH:mm:ss}。", "持仓数量变化来自实际交易日期；报价、配置、研究、风险、估值为读取时快照，不冒充历史期末状态。缺少历史快照时不能重建过去市值或被删除的记录。", period.End > LocalDate(captured) ? "当前期间尚未结束，只能保存 Draft。" : "期间已结束；用户仍需核对实际结果后确认完成。"]);
        var tx = await db.Transactions.AsNoTracking().Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct);
        var scope = securityId.HasValue ? new HashSet<Guid> { securityId.Value } : p.Holdings.Select(x => x.SecurityId).Concat(tx.Where(x => x.SecurityId.HasValue && LocalDate(x.OccurredAt) >= period.Start && LocalDate(x.OccurredAt) <= period.End).Select(x => x.SecurityId!.Value)).ToHashSet();
        Add("本期持仓数量变化（交易历史）", tx.Where(x => x.SecurityId.HasValue && (!securityId.HasValue || x.SecurityId == securityId)).GroupBy(x => x.SecurityId!.Value).Select(g =>
        {
            decimal Quantity(DateOnly through) => g.Where(x => LocalDate(x.OccurredAt) <= through).Sum(x => x.Type == TransactionType.Buy ? x.Quantity : x.Type == TransactionType.Sell ? -x.Quantity : 0);
            var start = Quantity(period.Start.AddDays(-1)); var end = Quantity(period.End); var events = g.Count(x => LocalDate(x.OccurredAt) >= period.Start && LocalDate(x.OccurredAt) <= period.End);
            return $"{names[g.Key]}：期初 {start:N4} → 截至期末已录入数量 {end:N4}；差额 {end - start:N4}；期间记录 {events} 条。";
        }));
        Add("当前资产配置 / 现金比例", [$"{p.TotalAssets:N2} {p.Account.BaseCurrency} 总资产；现金 {p.Cash:N2}，比例 {(p.TotalAssets > 0 ? p.Cash / p.TotalAssets : 0):P2}。{p.ValuationNotice}", .. p.Allocation.Select(x => $"{x.Name}：{x.Value:N2} / {x.Weight:P2} / 目标 {x.TargetWeight:P2}")]);
        Add("当前 Top holdings", p.Holdings.OrderByDescending(x => x.Weight).Take(10).Select(x => $"{x.Ticker} {x.CompanyName} · 数量 {x.Quantity:N4} · 市值 {x.MarketValue:N2} · 权重 {x.Weight:P2} · 价格日期 {x.PriceDate:yyyy-MM-dd} · {x.PriceSource}"));
        Add("当前行业 / 市场 / 币种 / 主题暴露（标签可重叠）", risk.Exposure.Exposures.Select(x => $"{x.Category} / {x.Name}：{x.Weight:P2}"));
        var allTheses = (await theses.IndicatorsAsync(LocalDate(captured), ct)).Where(x => !securityId.HasValue || x.SecurityId == securityId).ToArray();
        Add("当前 Thesis 状态与未来 Review Date", allTheses.Select(x => $"{x.Security} · {x.Title} · {x.StatusLabel} · {x.ReviewLabel} · {x.Notice}"));
        Add("当前 Thesis Warning", allTheses.Where(x => x.Status == "Warning").Select(x => $"{x.Security} · {x.Title} · {x.Notice}"));
        Add("当前 Kill Conditions", risk.Theses.Where(x => !securityId.HasValue || x.SecurityId == securityId).SelectMany(t => t.Conditions.Select(k => $"{t.Security} · {t.Title} / {k.Title} · {k.Status} · 当前 {k.CurrentValue} · {Clip(k.Evidence)}")));
        Add("当前会计 High Attention", risk.HighAttention.Where(x => !securityId.HasValue || x.SecurityId == securityId).Select(x => $"{x.Security} · {x.PeriodType} / {x.Currency} · {x.Rule} · {x.Evidence}"));
        foreach (var t in allTheses) observed["THESIS:" + t.Id] = $"{t.Security} {t.Title} {t.Status} {t.Notice}";
        foreach (var r in risk.HighAttention.Where(x => !securityId.HasValue || x.SecurityId == securityId)) observed[$"RISK:{r.SecurityId}:{r.PeriodType}:{r.Currency}:{r.Rule}"] = r.Evidence;
        var researchRows = await db.CompanyResearches.AsNoTracking().Where(x => !securityId.HasValue || x.SecurityId == securityId).ToListAsync(ct);
        Add("本期研究新增/更新记录（重大程度由用户核实）", researchRows.Where(x => LocalDate(x.UpdatedAt) >= period.Start && LocalDate(x.UpdatedAt) <= period.End).Select(x => $"{names[x.SecurityId]} · v{x.Revision} · 更新 {x.UpdatedAt.ToLocalTime():yyyy-MM-dd} · {Clip(x.Overview, 300)}"));
        foreach (var r in researchRows) observed["RESEARCH:" + r.Id] = $"{names[r.SecurityId]} · v{r.Revision} · {r.UpdatedAt:O}";
        var watches = await db.WatchlistItems.AsNoTracking().Where(x => !securityId.HasValue || x.SecurityId == securityId).ToListAsync(ct);
        Add("当前研究进度 / 观察清单", watches.Select(x => $"{names[x.SecurityId]} · {x.Stage} · {x.Priority} · 下一步 {Clip(x.NextAction, 400)} · 更新 {x.UpdatedAt.ToLocalTime():yyyy-MM-dd}"));
        Add("本期观察清单新增/更新", watches.Where(x => LocalDate(x.UpdatedAt) >= period.Start && LocalDate(x.UpdatedAt) <= period.End).Select(x => $"{names[x.SecurityId]} · 当前阶段 {x.Stage} · 优先级 {x.Priority} · {x.UpdatedAt.ToLocalTime():yyyy-MM-dd}"));
        foreach (var w in watches) observed["WATCH:" + w.Id] = $"{names[w.SecurityId]} · {w.Stage} · {w.Priority} · {w.UpdatedAt:O}";
        scope.UnionWith(watches.Select(x => x.SecurityId));
        var valuationLines = new List<string>();
        foreach (var id in scope)
            foreach (var model in await valuations.ListAsync(id, ct))
            {
                var text = $"{names[id]} · {model.Name} / {model.ModelType} · v{model.Revision} · 数据 {model.Actuals.DataSourceDate} / 价格 {model.Actuals.PriceDate}";
                var results = valuationCalculator.Compare(model);
                foreach (var result in results) valuationLines.Add(text + $" · {result.Scenario.Kind} / 假设 {result.Scenario.AssumptionDate} · " + JsonSerializer.Serialize(result.Result));
                observed["VALUATION:" + model.Id] = text + " " + JsonSerializer.Serialize(model);
            }
        Add("当前估值状态（确定性计算）", valuationLines);
        if (securityId is { } security)
        {
            var history = new List<ThesisHistoryEntry>();
            foreach (var t in (await theses.ListAsync(security, ct)).Where(x => LocalDate(x.CreatedDate) <= period.End)) history.AddRange(await theses.HistoryAsync(t.Id, ct));
            var original = history.Where(x => LocalDate(x.ChangedAt) < period.Start).GroupBy(x => x.Snapshot.Id).Select(g => g.MaxBy(x => x.Version)!).OrderByDescending(x => x.Snapshot.IsCurrent).ThenByDescending(x => x.ChangedAt).FirstOrDefault()
                ?? history.Where(x => LocalDate(x.ChangedAt) <= period.End).MinBy(x => x.ChangedAt);
            Add("本季度 Thesis 历史变更", history.Where(x => LocalDate(x.ChangedAt) >= period.Start && LocalDate(x.ChangedAt) <= period.End).OrderBy(x => x.ChangedAt).Select(x => $"{x.ChangedAt:O} · {x.Snapshot.Content.Title} · v{x.Version} · {x.Snapshot.Status} · {x.ChangeReason} · {x.Snapshot.ConditionNotice}"));
            Add("原始 Thesis / 期初证据", original is null ? ["没有期间开始前或期间内的 Thesis 快照。请在实际结果中说明缺口，不可将当前逻辑倒填为原始逻辑。"] : [$"{original.Snapshot.Content.Title} · v{original.Version} · {original.ChangedAt:O} · {(LocalDate(original.ChangedAt) < period.Start ? "期初可用版本" : "本期首次建立版本，并非期初已有")}", original.Snapshot.Content.InvestmentSummary, "预期持有期：" + original.Snapshot.Content.ExpectedHoldingPeriod, "当时风险：" + original.Snapshot.Content.KeyRisks]);
            if (original is not null) assumptions.AddRange(original.Snapshot.Assumptions.Select(x => new AssumptionReview(x.Id, x.Content.Description + $" / {x.Content.Metric} / 预期 {x.Content.ExpectedValue} {x.Content.Unit}", AssumptionOutcome.Unassessed, "")));
            var current = (await theses.ListAsync(security, ct)).FirstOrDefault(x => x.IsCurrent);
            Add("当前证券逻辑与风险（可能晚于本季度）", current is null ? ["当前无生效逻辑；这并不等于原始逻辑正确或错误。"] : [current.Label, current.Content.InvestmentSummary, current.ConditionNotice, .. current.KillConditions.Select(x => $"{x.Content.Title} · {x.Status} · {x.Content.Evidence}")]);
            var metrics = await db.FinancialMetrics.AsNoTracking().Where(x => x.SecurityId == security).ToListAsync(ct);
            var thresholds = await research.ReadThresholdsAsync(ct);
            var findings = metrics.GroupBy(x => (x.PeriodType, x.Currency)).SelectMany(g => AccountingRiskEngine.Evaluate(g.ToArray(), g.Key.PeriodType, g.Key.Currency, thresholds).Select(f => $"{g.Key.PeriodType} / {g.Key.Currency} · {f.Rule} · {f.Level} · {(f.Assessed ? f.Evidence : "数据不足，未评估")}")).ToArray();
            Add("当前所选证券的会计风险（含未持仓证券）", findings);
            observed["ACCOUNTING:" + security] = string.Join("\n", findings);
            foreach (var metric in metrics) observed[$"FIN:{security}:{metric.PeriodType}:{metric.Period}:{metric.MetricType}:{metric.Currency}"] = $"{metric.Value} / 估算 {metric.IsEstimated} / 来源 {metric.SourceId}";
            var q = $"{period.Start.Year}-Q{(period.Start.Month - 1) / 3 + 1}"; var prior = FinancialPeriod.PreviousYear(q);
            Add("本季度财务指标与同比变化", metrics.Where(x => x.PeriodType == PeriodType.Quarterly && x.Period == q).Select(x =>
            {
                var previous = metrics.SingleOrDefault(y => y.PeriodType == x.PeriodType && y.Period == prior && y.MetricType == x.MetricType && y.Currency == x.Currency && !y.IsEstimated);
                var growth = x.IsEstimated ? null : FinancialCalculations.Growth(x.Value, previous?.Value);
                return $"{q} {x.MetricType}：{x.Value} {x.Currency} · {(x.IsEstimated ? "估算，不能当作实际结果" : "用户录入实际值")} · {prior}：{previous?.Value.ToString() ?? "缺失"} · 同比 {(growth.HasValue ? growth.Value.ToString("P2") : "不可计算")} · 来源 {x.SourceId}";
            }));
        }
        var priorReviews = await ReviewsAsync(accountId, history: true, ct: ct);
        var baseline = priorReviews.Where(x => x.Review.SecurityId == securityId).Select(x => JsonSerializer.Deserialize<ReviewEvidence>(x.Review.SnapshotJson)!).Where(x => LocalDate(x.CapturedAt) < period.Start).MaxBy(x => x.CapturedAt);
        Add("研究 / 风险 / 估值 / 观察清单的可验证快照变化", baseline is null ? ["没有本期之前保存的可比复盘快照；不能重建旧值和删除记录。本期更新清单只表示记录曾更新，不表示变化方向。首次保存从现在建立历史。"] : new[] { $"比较基准：{baseline.CapturedAt:O}（不一定恰好期初），终点为本次读取时间。" }.Concat(observed.Where(x => !baseline.ObservedState.TryGetValue(x.Key, out var old) || old != x.Value).Select(x => $"新增/变化 {x.Key}：{Clip(baseline.ObservedState.GetValueOrDefault(x.Key) ?? "无记录", 400)} → {Clip(x.Value, 600)}")).Concat(baseline.ObservedState.Keys.Except(observed.Keys).Select(x => $"当前快照中不再出现：{x}；可能移出观察或范围改变，不自动判断风险消失。")));
        return new(accountId, securityId, kind, period.Start, period.End, captured, sections, assumptions, observed);
    }

    public async Task<string> BehaviorAsync(Guid accountId, DateOnly today, CancellationToken ct = default)
    {
        if (accountId == Guid.Empty) return "请选择账户。";
        await using var db = await factory.CreateDbContextAsync(ct); var tx = await db.Transactions.AsNoTracking().Where(x => x.PortfolioAccountId == accountId).ToListAsync(ct);
        var journals = await JournalsAsync(accountId, ct: ct); var secs = await db.Securities.ToDictionaryAsync(x => x.Id, ct);
        var text = new StringBuilder("投资记录行为统计；不进行心理诊断，不把短期亏损自动归因于错误决策。\n\n");
        var duration = JournalRules.AverageClosedHoldingDays(tx);
        text.AppendLine($"已卖出份额平均持有天数（FIFO、按数量加权）：{(duration.HasValue ? duration.Value.ToString("N1") : "暂无可计算数据")}；未平仓不计入。该匹配仅供行为统计，不改变移动平均成本核算。");
        text.AppendLine($"最新日志中的加仓意图 {journals.Count(x => x.Journal.Action == JournalAction.Add)} 次；减仓 {journals.Count(x => x.Journal.Action == JournalAction.Trim)} 次；退出 {journals.Count(x => x.Journal.Action == JournalAction.Exit)} 次。");
        text.AppendLine($"实际账本买入 {tx.Count(x => x.Type == TransactionType.Buy)} 笔 / 卖出 {tx.Count(x => x.Type == TransactionType.Sell)} 笔；日志不等于成交。\n");
        foreach (var high in new[] { true, false })
        {
            var group = journals.Where(x => x.Journal.Action is JournalAction.Open or JournalAction.Add && (high ? x.Journal.Confidence >= 70 : x.Journal.Confidence <= 30)).ToArray();
            var valid = group.Where(x => x.Journal.SecurityId.HasValue && x.Journal.Price > 0 && secs[x.Journal.SecurityId.Value].LatestPrice is > 0 && secs[x.Journal.SecurityId.Value].PriceDate is { } date && LocalDate(date) >= x.Journal.Date && LocalDate(date) <= today).ToArray();
            var returns = valid.Select(x => (secs[x.Journal.SecurityId!.Value].LatestPrice!.Value - x.Journal.Price!.Value) / x.Journal.Price.Value).ToArray();
            var mature = valid.Count(x => x.Journal.ExpectedHoldingDays.HasValue && LocalDate(secs[x.Journal.SecurityId!.Value].PriceDate!.Value).DayNumber - x.Journal.Date.DayNumber >= x.Journal.ExpectedHoldingDays.Value);
            text.AppendLine($"{(high ? "高信心 ≥70" : "低信心 ≤30")} Open/Add 日志 {group.Length} 条；可比报价 {returns.Length} 条；简单平均价格变化 {(returns.Length == 0 ? "无数据" : returns.Average().ToString("P2"))}；其中报价时已达到预期天数 {mature} 条。");
        }
        text.AppendLine("以上是每条日志价格观察，非投资收益率、胜率或因果判断；不含分红费用，不按资金加权。未录入预计天数的周期状态未知。请结合预期持有期与 Thesis 最终结果。\n");
        var allVersions = await db.ThesisVersions.AsNoTracking().ToListAsync(ct); var reviews = (await ReviewsAsync(accountId, history: true, ct: ct)).Where(x => x.Review.Status == ReviewStatus.Completed && x.Review.Kind == ReviewKind.Quarterly).ToArray();
        var relevant = tx.Where(x => x.SecurityId.HasValue).Select(x => x.SecurityId!.Value).Concat(journals.Where(x => x.Journal.SecurityId.HasValue).Select(x => x.Journal.SecurityId!.Value)).ToHashSet();
        var events = 0;
        foreach (var g in allVersions.GroupBy(x => x.ThesisId))
        {
            var history = g.OrderBy(x => x.Version).Select(x => (Version: x, Detail: JsonSerializer.Deserialize<ThesisDetail>(x.SnapshotJson)!)).ToArray(); var wasTriggered = false;
            foreach (var item in history)
            {
                var triggered = item.Detail.TriggeredCount > 0;
                if (triggered && !wasTriggered && relevant.Contains(item.Detail.SecurityId))
                {
                    events++; var at = item.Version.CreatedAt;
                    var completed = history.Where(x => x.Detail.LastReviewedAt >= at).Select(x => x.Detail.LastReviewedAt!.Value).Concat(reviews.Where(x => x.Review.SecurityId == item.Detail.SecurityId && x.Review.CreatedAt >= at && x.Review.PeriodEnd >= LocalDate(at)).Select(x => x.Review.CreatedAt)).Order().Cast<DateTimeOffset?>().FirstOrDefault();
                    text.AppendLine($"{secs[item.Detail.SecurityId].Symbol} · 首次记录触发 {at.ToLocalTime():yyyy-MM-dd} → {(completed.HasValue ? $"后续复盘 {completed.Value.ToLocalTime():yyyy-MM-dd}，间隔 {(completed.Value - at).TotalDays:N1} 天（{((completed.Value - at).TotalDays <= 7 ? "7 天内" : "超过 7 天")}）" : $"尚无后续完成复盘记录，已过 {Math.Max(0, today.DayNumber - LocalDate(at).DayNumber)} 天")}。");
                }
                wasTriggered = triggered;
            }
        }
        if (events == 0) text.AppendLine("没有可识别的历史触发事件，不能计算响应速度。");
        text.AppendLine("响应口径：本账户交易/日志涉及证券，历史首次从非触发到触发后是否存在完成复盘记录；7 天仅为展示分组，不评价决策质量，也不证明风险已解决。");
        return text.ToString();
    }
}
