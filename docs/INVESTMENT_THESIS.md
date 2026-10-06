# Phase 3 · 可验证、可证伪的投资逻辑

## 从观点到复盘

1. 打开“逻辑 Thesis”，选择证券、新建 Thesis。标题与 Investment Summary 必填；记录市场可能错在哪里、预期持有期、Base / Bull / Bear、催化剂、关键风险和复盘日期。
2. 在“假设”把叙事拆成指标：描述、Metric、Baseline、ExpectedValue、WarningThreshold、KillThreshold、CurrentValue、Unit、方向、证据。基线/预期值/当前值可空，当前值为空显示 Unknown。用户可随时更新当前值与证据。
3. 在“Kill Conditions”明确反证。所有条件均视为关键条件，用户填写标题、描述、指标、方向、Warning / Trigger 阈值、当前值、单位和证据。未填写当前值显示“未评估”，不会被当成安全。
4. 核对后通过“用户确认状态”选择 Active。每只证券只有一个当前逻辑；替换时必须先人工关闭或确认旧逻辑失效，系统不静默替换。
5. 到复盘日，在 Dashboard 的“需要复盘”进入逻辑；Portfolio 和 Watchlist 也展示关联证券的逻辑状态与复盘日期。核实当前值与证据后点击“完成复盘”，填写结论和下一次日期。完成复盘不会清除触发条件，也不会自动恢复 Active。
6. 修改摘要、条件、假设、当前值、日期、状态、归档或完成复盘都会保存完整新版本。版本历史并排显示所选旧版本与当前版本，包括已删除的旧条件、证据和修改日期。

编辑在模态窗口中显式保存；取消/关闭放弃本次修改。保存时检查原 Version，旧编辑不能覆盖新版本。状态决策、删除和归档均要求填写依据。历史 Thesis 保留在选择器中，标注“已归档”；没有直接删除整个 Thesis 的入口。

## 状态与当前逻辑

| 状态 | 含义及转换 |
|---|---|
| Draft | 尚未启用，可编辑，可由用户确认 Active / Warning / Invalidated / Closed |
| Active | 用户已启用的当前逻辑；关键条件预警或触发后自动提升 Warning |
| Warning | 需要核实；即使条件恢复正常或被删除也不会自动降级，必须由用户重新确认 Active |
| Invalidated | 仅用户能确认失效；内容与条件只读，可继续关闭或归档，不能原地重新启用 |
| Closed | 研究结束，只读，可以归档；重新建立观点需要新建 Thesis |

IsCurrent 与状态分开保存。Active 必须 IsCurrent=true，当前 Thesis 变为 Warning 仍保留该位置。Draft 在准备过程中出现触发条件会变成 Warning，但不会抢占当前逻辑位置。Invalidated / Closed 释放当前位置。数据库用 `SecurityId WHERE IsCurrent = 1` 的唯一索引保证同证券最多一个当前 Thesis。

存在 Warning / Triggered 条件时不能确认 Active；没有条件允许用户启用，但界面明确提示未建立反证条件，并使用中性标记。假设的 Triggered 是研究提示，不自动替代关键 Kill 条件的决策规则。

## 数值与颜色

- AtOrAbove：当前值 ≥ Trigger 为 Triggered，否则 ≥ Warning 为 Warning。
- AtOrBelow：当前值 ≤ Trigger 为 Triggered，否则 ≤ Warning 为 Warning。
- 达到边界即触发；上下阈值相等时 Triggered 优先。计算使用 decimal，不通过 AI 或浮点舍入。
- 上穿要求 Warning ≤ Trigger；下穿要求 Warning ≥ Trigger。指标、单位必须填写。
- 单位由用户明确：例如 `%` 时 `20` 表示 20%；`CNY` 为原币单位；定性事件可使用 `0/1`。所有值必须使用同一单位。本阶段不自动连接 FinancialMetric，尤其不要把财务表的 0.20 比例直接当成这里的 20%。
- Green：Active 且已有条件、全部已核实且 Normal；Amber：Warning / 条件预警；Red：存在 Triggered 或已经 Invalidated；Neutral：草稿、无条件、未评估或关闭。颜色与文字同时显示，Green 不代表投资无风险。

## 人类决策边界

IThesisReader 与 IThesisUserCommands 分离。创建、编辑、删除、阈值变化、状态决策和归档使用显式 HumanUser 来源；System / AI 来源在应用命令边界和条件实体编辑入口被拒绝。AI 模块仍为禁用 Provider，没有注册或注入 Thesis 写入能力，也没有自动调整条件的后台任务。

Actor 是本机应用内部的操作来源约束，不是操作系统身份认证。未来接入 AI 时只应向其提供读取与建议 DTO，建议必须由用户在编辑器确认后才能保存。任何关键条件 Triggered 都仅把未结束 Thesis 提升为 Warning；不会自动 Invalidated、下单、改仓位或改现金。

## 历史与事务

- 初始创建保存 v1；每次成功修改保存 v2、v3…完整快照，包括正文、状态、当前标记、假设、Kill 条件、阈值、当前值、证据和日期。
- Thesis.Version 为并发令牌；每次写入检查用户打开时的版本。更新实体与追加快照使用同一个 SQLite 事务，任何校验失败都不改变当前内容或版本历史。
- ThesisVersion 的 `(ThesisId, Version)` 唯一。历史只追加；DbContext 拒绝修改或删除已保存的快照。应用没有“恢复并覆盖历史”的入口。此机制防止应用误改，不是加密防篡改审计系统。
- 归档仅适用于 Invalidated / Closed，不删除历史。证券被任何 Thesis 引用时不能删除。

## 复盘日期

ReviewDate 使用 DateOnly，按本机日历的 Today 比较，`ReviewDate <= Today` 即到期。未归档的 Draft / Active / Warning 都会提醒；Invalidated / Closed / 已归档不再产生复盘任务。空日期显示未安排。

Dashboard 汇总全部证券的到期逻辑；Portfolio 限当前账户持仓证券，Watchlist 限观察清单证券。这是各页的独立状态面板，不改变原有持仓核算与筛选。每次进入/刷新页面重新读取；应用持续开启跨过午夜时，每分钟检测日期变化并刷新当前页。没有系统托盘推送或外部日历任务。

“完成复盘”要求下一日期晚于今天，将结论写入该次版本的 ChangeReason，并记录 LastReviewedAt。每个假设/条件也保存自己的最后有效数值复核时间，整体复盘不伪造逐条核实时间。

## 当前范围

单机、单用户、手工录入和核对；没有 AI 建议生成、指标自动匹配、复盘附件、邮件提醒或历史差异高亮。所有历史内容可只读对照，但已结束逻辑不能原地复活。新迁移只新增 Thesis 相关四表，升级前沿用 SQLite 在线备份；PortfolioLedger / PortfolioStore 的核算实现保持不变。
