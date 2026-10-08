# AI Investment Workbench · AI 投资工作台

面向个人证券投资者的 Windows 本地投资研究工作台。核心目的不是自动荐股；AI 是研究助理，最终投资决策由用户完成。第一版不提供券商自动交易。

## AI服务管理更新（2026-10-07）

新增独立“AI服务”页面，支持多个提供商 Source、远端模型发现、本地模型启用与测试。原生支持 OpenAI、Google GenAI、Anthropic，并提供 OpenAI 兼容层。原单一配置自动迁移。详见 [AI服务管理](docs/AI_SERVICES.md)。

## 当前阶段：Phase 8 / Production Hardening & Installer

在原有 Solution 上增加风险标签、重叠暴露、集中度、风险总览、仓位规划、加仓/退出清单和持久化决策历史，详见 [风险与决策指南](docs/PORTFOLIO_RISK_DECISIONS.md)。保留 PE、EV/EBITDA、EV/FCF、简化 DCF、反向 CAGR / 稳定利润率估值及 Bear/Base/Bull 比较。估值完全由确定性代码驱动，详见 [估值说明](docs/VALUATION_ENGINE.md)。已完成投资逻辑、假设、用户专属 Kill 条件、完整版本历史和复盘提醒；保留 Portfolio 核算以及十章节公司研究、财务指标与趋势、资料来源、TXT/Markdown/CSV/PDF 文本导入、可配置研究评分、会计关注规则和全局搜索。数据保存在本机 SQLite，重启后恢复。新增可选 AI 研究增强层：官方 OpenAI Responses、加密密钥、任务上下文、提示词管理、结构化结果与审计、Thesis Challenge 及十步公司研究。默认关闭，需要用户配置模型和密钥；无实时行情 API。详见 [AI 使用与设计说明](docs/AI_INTEGRATION.md)。

Phase 7 新增投资日志、决策草稿确认、周/月/季度复盘、不可覆盖的版本历史、行为统计和首页复盘提醒。AI 日志分析结合投资期限与 Thesis 结果，单独保存，不改写原始记录。详见 [日志与复盘指南](docs/JOURNAL_AND_REVIEW.md)。

首次启动无账户时显示向导，示例为 500000 CNY（RMB），ETF 40%、主动 40%、观察/试验 10%、现金 10%；这些是可修改的示例，目标比例需合计 100%。不自动创建账户或示例交易。取消向导后可在总览点击“创建账户”。

Phase 8 增加每日自动备份、手动备份与重启恢复、副本迁移保护、八类导出、独立 Demo、关于信息、键盘/高缩放优化和 Windows 安装器。完整说明：[用户指南](docs/USER_GUIDE.md)、[备份恢复](docs/BACKUP_AND_RESTORE.md)、[AI 安全设计](docs/AI_SAFETY_DESIGN.md)、[发布流程](docs/RELEASE.md)。

## 快速使用

1. 创建账户，输入本位币、初始资金和目标权重。
2. 打开“证券库”，新增股票或 ETF，填写代码、交易所、市场与币种。
3. 打开“投资组合”，点击“新增交易”，录入买入、卖出、入金、出金、分红或费用。
4. 选中持仓，点击“编辑价格 / 权重”，填写最新价格和报价日期。无报价时明确按成本暂估。
5. 在 Dashboard 查看资产、现金、浮盈亏、主要持仓和实际/目标资产配置。
6. 在观察清单加入证券，编辑研究阶段、优先级、理由、风险与下一步行动。
7. 使用“CSV 导入向导”选择文件、预览、映射字段、验证、确认导入并保存结果报告。

多个账户通过右上角切换。证券库和观察清单为全局；财务总计始终仅包含当前账户。表格支持点击表头排序，Portfolio、证券库与观察清单均支持搜索和过滤。

## 技术栈与结构

.NET 10 LTS、C#、WPF、MVVM、EF Core 10、SQLite、Microsoft.Extensions.DependencyInjection / Hosting / Logging。资产配置与财务趋势使用原生 WPF 控件，不引入图表包；PDF 文本提取使用 PdfPig 0.1.16。

```text
AIInvestmentWorkbench.sln                原有 Solution
src/AIInvestmentWorkbench.App            WPF、编辑窗口、导航、主题、DI、导入向导
src/AIInvestmentWorkbench.Domain         实体、校验、加权平均成本交易账本
src/AIInvestmentWorkbench.Application    用例服务、仓储接口、DTO、CSV 解析与映射
src/AIInvestmentWorkbench.Infrastructure SQLite 仓储、原子写入、迁移、日志与备份
src/AIInvestmentWorkbench.AI             OpenAI Responses、Google GenAI、Anthropic 与兼容适配器
 tests/                                 Domain.Tests / Application.Tests
 docs/                                  架构、规格、模型、路线图、CSV 说明与示例
 scripts/                               本机 SDK 环境与 WPF 自动验收
```

## 构建、运行、测试

Windows 上需要 .NET 10 SDK。此机器原先只有运行时，Phase 0 已在 `work/dotnet` 安装独立 SDK；脚本只调整当前终端环境，没有该目录时使用系统 SDK。

```powershell
. ./scripts/Use-LocalDotnet.ps1
dotnet restore AIInvestmentWorkbench.sln
dotnet build AIInvestmentWorkbench.sln --no-restore
dotnet test AIInvestmentWorkbench.sln --no-build
dotnet run --project src/AIInvestmentWorkbench.App --no-build
```

实际 WPF 表单与端到端流程验证（隔离数据库，不修改真实账户）：

```powershell
./scripts/Test-Smoke.ps1
```

此脚本会创建 50 万测试账户、录入证券和买卖、设置报价、维护观察记录、导入三类 CSV，再通过研究编辑表单、四类来源文件、33 条财务指标 CSV、评分权重与全局搜索完成研究闭环，再执行 Thesis 条件、状态、历史与复盘验证，再验证五类估值、三情景、双目标反推与保存，再验证风险聚合、六种决策记录与无交易副作用，再验证离线 AI 单项分析、十步报告、错误/取消/超时与审计保存，再验证决策到日志、原始记录与更正、周/月/季度复盘、独立 AI 分析及重启恢复，渲染十四页及研究/财务/逻辑/风险/AI 标签的两种主题。输出写入独立 `work/smoke-<guid>`。对同一目录重新执行以下命令可验证关闭重启后的数据：

```powershell
dotnet src/AIInvestmentWorkbench.App/bin/Debug/net10.0-windows/AIInvestmentWorkbench.App.dll --smoke-test --data-root <上述测试目录>
```

交付的 Release 程序位于 `outputs/phase8/app/AIInvestmentWorkbench.App.exe`。请保留整个 app 目录；Windows x64 发布包自带 .NET Runtime，无需另装运行时。安装包位于 outputs/phase8/installer。

## 核算口径

- 采用移动加权平均成本，多头、单账户单币种；金额全程 decimal。
- 买入手续费计入持仓成本；卖出按平均成本扣减，卖出费用扣现金并计入已实现损益；清仓成本精确归零。
- 按交易日期、账户内录入顺序重放全部历史；补录过去日期会重新校验后续余额，拒绝超卖和任何历史时点的现金透支。
- CurrentCash 与 Position 是交易历史的持久化投影，与交易在同一事务提交；InitialCapital 是单独的期初资金。修改初始资金会重新核算历史。
- Dashboard 的 PnL 明确为未实现浮盈亏，不是累计投资收益或年化业绩。
- 无报价使用成本暂估并展示提示；人工价格及日期作用于所有持有该证券的账户。
- Positions CSV 按平均成本转换为买入历史、扣减现金，不覆盖已有持仓。不要再导入同一笔原始买入造成重复。

## 数据库与日志

- 数据库：`%LOCALAPPDATA%\AIInvestmentWorkbench\Workspaces\Default\Data\investment.db`
- 日志：`%LOCALAPPDATA%\AIInvestmentWorkbench\Workspaces\Default\Logs\workbench-yyyyMMdd.log`（UTC 日期）
- 迁移前备份：同工作空间的 `Backups`

应用启动使用已提交的 EF migrations 在副本中升级；验证通过后使用 SQLite 在线备份保留原库，再原子替换。相同数据目录只允许一个应用实例。未知迁移版本拒绝打开，不自动重建或删除数据。

Phase 0 没有交易历史的手工仓位会保留且阻止新交易覆盖；应保留原账户，在新账户使用 CSV 期初持仓或完整交易历史进行核对。曾在 `%LOCALAPPDATA%\AIInvestmentWorkbench\Data` 留存的另一版数据库继续保留，不自动导入。

开发迁移：

```powershell
dotnet tool restore
dotnet ef migrations add ChangeName --project src/AIInvestmentWorkbench.Infrastructure --startup-project src/AIInvestmentWorkbench.Infrastructure --output-dir Persistence/Migrations
```

数据库未加密。AppSetting 仅保存主题与导入指纹等非敏感信息。禁止将密码/API Key 写入数据库或日志；日志省略原始异常 Message/Data，保留类型和堆栈并屏蔽常见凭据模式。日志暂不自动清理。

## 当前限制

不支持外汇换算、空头、融资、拆股/合股、税务或券商自动交易；交易仅追加，尚无历史修改、删除或冲正 UI。单账户记账可以处理六种交易类型，但不是完整税务会计账本。证券有关联历史时禁止删除。报价为人工维护。

CSV 仅支持 UTF-8、逗号分隔、日期 yyyy-MM-dd；数字以小数点表示小数，不使用千分符。最多 5 MB / 10000 条，提供整批原子导入及重复批次检测，无部分成功模式。详细字段和示例见 `docs/CSV_IMPORT.md`。

## 研究与财务快速开始

1. 在“研究”选择证券，选择章节并点击编辑，明确保存。
2. “来源与文件”导入 TXT、Markdown、CSV 或 PDF，核实标题、发布日期与 1–6 级来源等级。
3. “财务”增加指标或通过“财务 CSV 导入”映射、验证、整批导入；切换 Annual / Quarterly / TTM 和报告币种。
4. 查看四类趋势、FCF 与利润率计算、七项会计关注规则。可在页面调整阈值。
5. “研究评分”录入用户评分、理由、证据及七项权重；“搜索”检索全部本地研究资料。

详细字段、公式、阈值、文件限制和示例见 [研究与财务指南](docs/RESEARCH_AND_FINANCIAL.md)。财务 CSV 示例见 [financial-metrics.csv](docs/samples/financial-metrics.csv)。

Phase 2 不提供 OCR、AI 自动评分、财务表格自动识别、实时财务数据源或汇率换算。PDF 仅提取文字；原文件留在原路径，数据库保存提取文本。研究为当前文档，不是历史版本库；全局搜索尚无全文索引。估算值保留但不参与趋势/规则；用户负责统一财年、币种和单位。

## 投资逻辑与复盘

Phase 3 新增可验证、可证伪的 Thesis：观点、市场分歧、三情景、催化剂、风险、假设与 Kill Conditions。用户维护当前值、证据和阈值，条件触发仅提示 Warning，Invalidated 必须由用户确认。每只证券最多一个当前逻辑，历史版本只追加。

在“逻辑 Thesis”新建、编辑、确认状态和查看历史；Dashboard 显示到期复盘，Portfolio / Watchlist 显示关联证券的逻辑状态。详细设计和操作说明见 [投资逻辑指南](docs/INVESTMENT_THESIS.md)。数据库位置不变，升级前自动备份。



## 估值快速开始
在估值页选择证券、建立模型、核对完整金额和股数，再编辑 Bear/Base/Bull。Base 可复制到另两情景。DCF 下方可查看逐年现金流和终值；反向估值可选择 CAGR 或稳定利润率，失败会明确说明原因。模型独立保留来源日期、价格日期与假设日期，重启后从保存输入重新计算。公式和限制见 [估值指南](docs/VALUATION_ENGINE.md)。

## 风险与决策快速开始

从 Portfolio 选中持仓后点击“风险 / 决策”，或直接进入风险页。按当前账户查看行业、市场、币种、主题与自定义风险暴露；为所选证券关联标签，输入四个参数计算目标仓位，再完成加仓或退出清单。所有最终选择和理由写入决策历史，不执行交易。相关性问题由用户判断，未实现统计相关矩阵或 VaR。详见 [风险与决策指南](docs/PORTFOLIO_RISK_DECISIONS.md)。
