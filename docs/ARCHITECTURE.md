# Phase 3 架构

## 分层

App → Application / Infrastructure / AI；Infrastructure → Application → Domain；AI → Application；Domain 仅引用 .NET 基础库，不依赖 WPF、EF 或 AI SDK。

App 是 DI composition root，Generic Host 管理生命周期。保留原 Solution、项目引用、主题、日志与异常处理入口。新增工作空间账户选择、通用 MVVM 编辑窗口、证券库及 CSV 导入页。窗口后台仅负责界面初始化、DialogResult 和保存过程关闭保护；业务行为由 ViewModel 命令调用应用服务。

## 用例与仓储

SecurityApplicationService 负责证券编辑与领域校验；ISecurityRepository / SecurityRepository 负责检索、持久化、唯一标识检查及引用删除保护。

PortfolioApplicationService 提供账户、交易、报价、仓位策略与观察清单用例；IPortfolioStore 的 SQLite 实现负责操作所需的事务边界。PortfolioLedger 是无基础设施依赖的纯领域计算器。

每次操作通过 IDbContextFactory 创建独立 DbContext，查询使用 AsNoTracking。DatabaseWriter 以写入队列和数据库事务串行处理账户、证券、交易、观察清单与导入，避免现金/数量更新丢失；数据库事务保证进程中断时也不会留下半批写入。

## 交易与投影

Transaction 为独立不可变历史记录。新增交易时，在同一事务中读取原历史、按 Date + Sequence + CreatedAt + Id 稳定排序、重放核算，再同步 CurrentCash 和 Position 后提交。Sequence 是账户内单调递增的录入顺序；回溯日期按发生日重放，同一天按录入顺序计算。

采用移动加权平均成本：买入费用计成本；卖出扣除对应平均成本，清仓移除全部剩余成本避免舍入残差。拒绝超卖、透支及账户/证券/交易币种不一致。旧版没有交易的手工仓位不会被新交易静默覆盖。

报价保存在 Security；账户相关 TargetWeight、MaxWeight 和 Bucket 保存在 Position。更新报价与仓位规则作为一个原子操作。总资产 = 现金 + 各持仓数量 × 估值价格；无报价明确回退平均成本，标记暂估。各币种账户不混合汇总。

## CSV

CsvImportService 在 Application 层解析 UTF-8 文本、引号/换行、字段映射和数字/枚举；数据层负责存在性、唯一性、现金与仓位全历史校验。验证与正式导入调用同一条写入路径：验证会执行并回滚事务；正式导入重新校验后提交。任一错误回滚所有记录，不支持跳过错误行。

标准化内容 + 类型 + 账户生成 SHA-256 指纹，同事务写入 AppSetting，防止相同批次重复导入。更改内容或映射需重新验证。导入在后台线程执行，避免 Microsoft.Data.Sqlite 同步 I/O 阻塞 WPF；页面保持忙碌状态，记录当前批次的目标账户。

## UI 与图形

NavigationService 切换 PageViewModel，DataTemplate 创建 View；MainWindow 没有页面切换业务逻辑。WorkspaceContext 保存当前账户，页面按选定账户加载快照。证券库和观察清单为全局。DataGrid / ICollectionView 提供类型化排序、搜索和过滤。

Light / Dark 使用 DynamicResource 覆盖背景、卡片、文本、边框、表格、输入框、下拉框、按钮和滚动条。资产配置使用 WPF 原生 ProgressBar，避免图表依赖渗入领域层。

## 升级、错误与安全

默认数据根目录不变：LocalApplicationData/AIInvestmentWorkbench/Workspaces/Default。启动前检查迁移历史，升级前备份；新增 PortfolioMvp 迁移保留 Phase 0 表、ID 和记录，补齐新字段及旧枚举映射。

预期业务错误显示可读说明；未知错误记录异常类型/堆栈并提示，Dispatcher/AppDomain/TaskScheduler 提供全局兜底。禁止记录凭据、研究内容、SQL 参数或原始服务响应。AI Provider 默认禁用；Phase 6 支持用户明确启用后的 Responses 调用。

测试包括领域计算、文件型 SQLite 原子回滚、并发写入、版本升级、CSV 字段映射和实际 WPF 编辑表单/页面渲染。没有生产内存数据库。

## Phase 2 研究模块

IResearchStore 定义研究、指标、来源、评分、阈值与全局搜索用例，ResearchStore 在 Infrastructure 以独立 DbContext 和既有 DatabaseWriter 事务执行。FinancialCsvService 重用既有 CSV 解析器，处理财务字段映射；未改动 PortfolioLedger 或 PortfolioStore 的核算流程。

CompanyResearch 一证券一文档，Revision 为 EF 并发令牌并在领域更新时核验。研究 UI 只读显示正文，独立模态编辑窗口明确保存，防止切换证券丢失未提交编辑。SecurityPageViewModel 使用请求序号阻止旧查询覆盖新证券资料；ResearchSelection 支持跨研究、财务及搜索页面定位证券。

FinancialCalculations 与 AccountingRiskEngine 仅依赖 Domain，使用 decimal、明确的期间与币种，不调用 AI。图表控件 TrendChart 只负责绘制，计算不进入 View 或图形代码。缺失期间保留空值，图线断开。

IResearchFileReader / IPdfTextExtractor 分离文件读取与 PDF 提取，PdfPig 仅在 Infrastructure 引用。无法提取文字的 PDF 抛出可测试的业务错误，保存元数据前必须成功读取文本。文件导入本身不发送网络；Phase 6 用户开始研究时可以发送所选来源的有限摘录。研究资料外键限制删除，证券被研究数据引用时给出明确错误。

ResearchAndFinancialData 迁移仅增加四张表和索引，不修改 Portfolio 表结构或交易数据；升级前沿用在线备份。测试覆盖 Phase 1 真实账本升级后资产、现金、仓位和交易 ID/内容不变。

## Phase 3 投资逻辑

新增 Thesis 聚合、ThesisAssumption、KillCondition、ThesisVersion。IThesisReader 与 IThesisUserCommands 分离，ThesisStore 通过原有 DatabaseWriter 序列化写入并原子追加快照。Thesis.Version 检测过期编辑，数据库过滤唯一索引保护每证券唯一当前逻辑。历史快照包含完整聚合，应用拒绝历史实体的更新与删除。

ThesisRules 处理 decimal 方向性阈值和 DateOnly 到期判断；Thesis 的警示恢复与最终失效均保留人工确认。所有修改要求 HumanUser 来源，AI/系统不得修改 Kill 条件。AI Provider 不持有命令接口。详见 INVESTMENT_THESIS.md 的边界说明。

ThesisViewModel / ThesisDialogs 提供独立 MVVM 编辑、版本对照。ThesisReviewPanel 在 Dashboard / Portfolio / Watchlist 附加只读复盘与状态，点击可定位具体 Thesis；不改变 PortfolioLedger、PortfolioStore 或持仓 DTO。投资逻辑新增迁移只增四表及约束，既有数据沿用备份升级策略。

## Phase 4：独立估值模块
Domain/Valuation 提供纯计算与有限迭代反推；Application/ValuationService 组织三情景，IValuationStore 定义持久化协议。Infrastructure/ValuationStore 使用原子事务和 Revision 检查。App/ValuationViewModel 与编辑对话框负责展示和人工输入。新增两表和独立迁移，未修改 PortfolioLedger 或 PortfolioStore。没有 AI 计算依赖。详见 VALUATION_ENGINE.md。

## Phase 5：组合风险与决策
独立 Domain/Risk 负责标签聚合、集中度、仓位规划和清单验证。PortfolioRiskStore 在一致性读事务中获取现有持仓投影、研究风险与 Thesis，保存时重建上下文指纹并通过 DatabaseWriter 原子追加 DecisionRecord。历史 JSON 带 SchemaVersion=1，DbContext 禁止修改/删除决策记录。App 新增 Risk 页面与清单编辑器；Portfolio 仅增加导航入口，账本核算不变。


## Phase 6：AI 增强层
Application/AI 定义无 SDK 类型的 IAIProvider、ISecretStore、IAIContextBuilder、IAIStore。AIAnalysisService 负责快照、严格解析、取消、超时和十步工作流；Infrastructure 提供只读上下文投影、AIStore、Windows DPAPI；AI 项目独占 OpenAI 2.14.0 Responses SDK。Provider 无 Thesis、Portfolio、估值、决策写入接口。App 增加 AI Settings、专用 PasswordBox 窗口与 AIResearch 页面。无网络启动依赖。详见 AI_INTEGRATION.md。

## Phase 7：日志与复盘
JournalRules 定义期间、到期、输入校验及已卖出份额持有时间。JournalReviewStore 只追加原始日志、更正与复盘快照；读写接口分离，AIContextBuilder 仅使用读取接口。App 通过 JournalViewModel / JournalDialogs 提供确认保存，Dashboard 提供季度入口。原有账本和估值算法不变。详见 JOURNAL_AND_REVIEW.md。

## Phase 8：维护边界
DatabaseFiles / BackupService 负责在线一致性备份、独立文件验证和启动前原子替换；DatabaseInitializer 只在副本执行迁移。DailyBackupWorker 在数据库可用后启动，错误不阻断本地功能。DemoService 使用独立工作空间、标记及互斥锁。ExportService 在后台生成原子文件，CSV 文本防公式注入。MaintenanceViewModel 集中提供 Settings 维护入口；WPF 表格/待办虚拟化与耗时读取移至后台，不改变确定性业务规则。
