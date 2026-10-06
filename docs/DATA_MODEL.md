# 数据模型 · Phase 1

所有核心实体继承 Entity，拥有 Guid Id、UTC DateTimeOffset CreatedAt / UpdatedAt。界面按本地日期显示，日期输入和 CSV 使用 yyyy-MM-dd。金额、数量和比例使用 decimal；SQLite 按 TEXT 保存精度，计算在 .NET 完成。UI 权重显示百分数，存储为 0–1。

| 实体 | 核心字段与规则 |
|---|---|
| PortfolioAccount | Name、BaseCurrency、InitialCapital、CurrentCash、TargetCashWeight、TargetEtfWeight、TargetActiveWeight、TargetExperimentalWeight、DefaultMaxPositionWeight |
| Security | Ticker、CompanyName、Market、Exchange、Currency、SecurityType、Sector、Industry、Country、ISIN?、Notes、LatestPrice?、PriceDate? |
| Transaction | PortfolioAccountId、SecurityId?、Date、Type、Quantity、Price、CashAmount、Fees、Currency、Notes、Sequence |
| Position | PortfolioAccountId、SecurityId、Quantity、TotalCost、TargetWeight、MaxWeight、Bucket；AverageCost / 市值 / 权重 / 浮盈亏为计算值 |
| WatchlistItem | SecurityId、Stage、Priority、Reason、NextAction、LastResearchDate?、RiskStatus、Notes |
| AppSetting | Key、Value；保存主题与成功导入批次指纹，不保存凭据 |

为保留 Phase 0 数据列，BaseCurrency 对应 Currency；Ticker/CompanyName/SecurityType 对应 Symbol/Name/Type；Date/Price 对应 OccurredAt/UnitPrice；Watchlist Notes 对应 Note。代码提供这些语义别名，EF 不重复映射。

PortfolioAccount 一对多 Position / Transaction；Security 一对多 Position / Transaction / WatchlistItem。所有外键 Restrict。证券 Exchange+Symbol 唯一；持仓 Account+Security 唯一；观察记录 SecurityId 唯一。交易即使清仓也保留，清仓 Position 数量与成本归零并在持仓表隐藏。

交易类型值为 Buy=1、Sell=2、Deposit=3、Withdraw=4、Dividend=5、Fee=6。买卖需要证券且数量/单价为正，现金金额为零；其他交易数量/单价为零，现金金额为正。Dividend 可关联证券，其他现金流不关联证券。Fee 的 Fees 必须零，费用只填写 CashAmount。所有 Fees 非负。币种必须与账户一致。

成本核算：买入总成本 += 数量×价格+手续费；卖出移除平均成本×卖出数量，清仓时移除全部余额；卖出现金 += 数量×价格-手续费。分红增加现金，出金与独立费用减少现金。每次重放不得超卖或透支。

市值 = 数量×LatestPrice；没有价格时用 AverageCost 并标记暂估。未实现盈亏 = 市值-总成本；盈亏率 = 未实现盈亏/总成本，成本零时 null；权重 = 市值/(现金+证券市值)，总资产零时为零。Dashboard 展示未实现盈亏，不将外部入金当作收益。

迁移 PortfolioMvp 为已有记录补齐字段，旧 Fund/Index 统一为 Other，未知旧市场归 Other；旧账户保留名称/币种，资金须按实际情况核对。没有交易历史的旧手工仓位保留且阻止新交易覆盖，迁移到新账户时需通过期初买入或完整交易导入。禁止直接 SQL 修改业务数据以绕过领域规则。

## Phase 2 新增实体

- CompanyResearch：SecurityId 唯一；十个研究章节分别存 TEXT；Revision 为并发令牌。Content 是领域值对象，不重复映射。
- FinancialMetric：SecurityId、Period、PeriodType、MetricType、Value、Currency、SourceId?、IsEstimated、Notes；SecurityId + PeriodType + Period + MetricType + Currency 唯一。SourceId 为 ResearchSource 外键，应用要求来源属于同证券。
- ResearchSource：SecurityId、Title、Publisher、PublishedDate?、Url、LocalFilePath、SourceType、ReliabilityLevel、Notes、ExtractedText；数据库 CHECK 约束等级 1–6。
- ResearchScore：SecurityId + Dimension 唯一；Weight（0–100）、AIScore?、UserScore?（0–100）、Reason、Evidence；数据库限制分数与权重范围，应用事务保证七项权重合计 100。

四个实体都继承 Entity，具备 Guid Id / CreatedAt / UpdatedAt，外键 Restrict。Security 对 Research 一对一，对 Metric / Source / Score 一对多。来源被指标引用时禁止删除。

风险配置存于 AppSetting `Research.RiskThresholds` JSON。财务指标比例用 0–1 小数；评分权重用 0–100 百分数，区别于 Portfolio 权重。支持全部 21 种 MetricType、3 种 PeriodType、15 种 SourceType；明细见 RESEARCH_AND_FINANCIAL.md。

## Phase 3 新增实体

Thesis：SecurityId、Title、InvestmentSummary、WhyMarketMayBeWrong、ExpectedHoldingPeriod、BaseCaseNarrative、BullCaseNarrative、BearCaseNarrative、ExpectedCatalysts、KeyRisks、Status、CreatedDate、ReviewDate?（DateOnly）、Version、Archived、IsCurrent、LastReviewedAt?。Security 一对多 Thesis；SecurityId 在 IsCurrent=1 时唯一。

ThesisAssumption：ThesisId、Description、Metric、Baseline?、ExpectedValue?、WarningThreshold、KillThreshold、CurrentValue?、Unit、Direction、Status、Evidence、LastReviewedAt?。

KillCondition：ThesisId、Title、Description、Metric、WarningThreshold、TriggerThreshold、CurrentValue?、Unit、Direction、Status、Evidence、LastReviewedAt?。当前所有条件视为关键条件。空值不参与阈值判断，UI 显示未评估。

ThesisVersion：ThesisId、Version、SnapshotJson、ChangeReason、Actor；ThesisId+Version 唯一；CreatedAt 是快照时间。JSON 保存完整 ThesisDetail，包括当时的假设和 Kill 条件。删除当前条件仍保留旧快照。

以上四实体都继承 Entity，包含 Guid Id / CreatedAt / UpdatedAt。外键 Restrict；生命周期 CHECK 约束保护 Active 必须是当前、归档必须已结束。详见 INVESTMENT_THESIS.md。

## Phase 4：ValuationModel / ValuationScenario
ValuationModel：Guid Id、SecurityId 外键、Name、ModelType、Revision、Revenue、NetDebt、ShareCount、CurrentPrice、Currency、DataSourceDate、PriceDate、Source、CreatedAt、UpdatedAt。ValuationScenario：Guid Id、ValuationModelId 外键、Kind、InputsJson（SchemaVersion=1）、AssumptionDate、CreatedAt、UpdatedAt。模型 + 情景唯一；模型固定三情景，写入事务维护集合完整性。所有结果按输入重新计算，不持久化可过期结果。外键 Restrict；删除模型时事务内删除其情景。JSON 保留 decimal 精度，后续结构变更须显式升级 SchemaVersion。Revision 为乐观并发标记，当前无估值历史快照。

## Phase 5：风险与决策
RiskTag 保存 Name、NormalizedName、Category、IsBuiltIn，分类 + 规范名称唯一。SecurityRiskTag 保存 SecurityId、RiskTagId，关联唯一。DecisionRecord 保存 PortfolioAccountId、SecurityId、Kind、Choice、Reason、AnswersJson、ContextJson、可选 SizingJson；含 Guid Id、CreatedAt、UpdatedAt。外键 Restrict，决策只追加，ContextJson 保存含日期和版本的当时风险快照（SchemaVersion=1）。Risk.NearMaxRatio 为非敏感 AppSetting。

## Phase 6：AI 审计
PromptTemplate：Guid Id、Name、AnalysisType、Body、IsBuiltIn、Revision、CreatedAt、UpdatedAt；每种内置任务过滤唯一索引，副本可多份。
AIAnalysis：SecurityId 可空（全局任务）、AnalysisType、PromptTemplateId、PromptSnapshot、ContextSnapshot、SettingsSnapshot（无密钥）、Model、Provider、SourceReferences、StructuredOutput、RawOutput、ResponseEnvelope、ResponseModel、ResponseId、Status、ErrorMessage、FinishedAt 及基类日期。Security / Template 外键 Restrict，结束后不可修改或删除。
AIResearchReport：SecurityId、Title、SectionIdsJson、ReportMarkdown、Status、CompletedSections 与基类字段；逐步保存部分结果，结束后只读。Running / Completed / InvalidOutput / Cancelled / TimedOut / Failed / Interrupted 明确区分。
AI.Settings AppSetting 仅包含服务、模型、地址、超时与输出上限；API Key 只在 Secrets/*.dpapi，SQLite 不存明文密钥。审计快照是普通本地研究数据，非全库加密。

## Phase 7
InvestmentJournal：PortfolioAccountId、SecurityId?、DecisionRecordId?、RootId、Version、CorrectionReason，以及 Date、Action、Price?、Quantity?、PortfolioWeight?、Reason、MarketConcern、WhyMarketMayBeWrong、ExpectedDevelopment、KillConditionSummary、ExpectedHoldingPeriod、ExpectedHoldingDays?、Emotion、Confidence?、Notes。包含 Entity 的 Guid Id / CreatedAt / UpdatedAt；保存后只读，更正追加版本。

InvestmentReview：AccountId、SecurityId?、RootId/Version、Kind、PeriodStart/End、Draft/Completed、SnapshotJson、ActualResults、AssumptionsJson、Decision?、Conclusion。假设状态含 Unassessed 草稿态及四种人工评估结果。两个新表均有 RootId/Version 唯一索引；第一版日志来源决策唯一，外键 Restrict 保护历史。AIAnalysis 新增可选 PortfolioAccountId；不反推旧记录归属。所有写入使用现有事务机制，详见 JOURNAL_AND_REVIEW.md。
