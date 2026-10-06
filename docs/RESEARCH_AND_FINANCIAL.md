# Phase 2 · 研究与财务数据

## 使用流程

1. 在证券库建立证券；研究资料按证券保存，与当前账户无关。
2. 打开“研究”，选择证券。公司研究下有 Overview、BusinessModel、Industry、Competition、Management、GrowthDrivers、Catalysts、Risks、AccountingNotes、UserNotes 十个章节。选择章节后编辑，在独立窗口明确保存；取消/关闭放弃本次修改。保存比较 Revision，拒绝旧版本覆盖。
3. 在“来源与文件”新增来源或导入文件，填写标题、发布者、发布日期、类型、等级和备注。可查看保存的全文、修改元数据、删除未被财务指标引用的来源。
4. 在“财务”录入指标，按 Annual / Quarterly / TTM 和报告币种分开查看。指标支持新增、编辑、删除、期间筛选、关键词搜索、表头排序及 CSV 导入。
5. “趋势与公式”显示收入、毛利率、营业利润率、FCF 趋势和计算结果。“会计关注规则”显示七项确定性规则的依据，可调整阈值。
6. 在“研究评分”选择维度、填写用户评分/理由/证据。七项权重可独立调整，保存时总和必须为 100%。
7. “搜索”支持证券、研究正文、来源全文、各类 Notes、观察清单、评分理由与证据；可打开对应证券的研究或财务页面。

## 财务数据口径

- Annual：`2025`，一个财年的数据；Quarterly：`2025-Q2`，单季度数据；TTM：`2025-Q2`，截至该季度的滚动十二个月。输入方负责财年口径一致，不自动把累计季度报表拆成单季或把四季汇总为 TTM。
- 金额为原币基本单位，不是万/百万；EPS 为每股金额，ShareCount 为股数，所有比例为小数，`0.25` 表示 25%。Currency 记录财报币种（也用于比例/股数系列归类），可以不同于证券交易币种，不进行换汇。
- Capex 是正数现金支出。`FCF = OperatingCashFlow - Capex`；`GrossMargin = GrossProfit / Revenue`；`OperatingMargin = OperatingIncome / Revenue`；`NetDebt = Debt - Cash`。
- 同比严格使用上一年度同期间，季度 Q2 对比上年 Q2。`Growth = (Current - Prior) / Prior`。基期非正、分母非正、缺少基础指标返回未定义，不显示成零；不混合证券、币种或期间类型。
- 表格保留录入的原值与 IsEstimated。趋势、公式和风险规则只采用非估算值。趋势优先用基础指标计算，缺少输入时可回退已录入的对应毛利率、营业利润率或 FCF；计算 FCF 与录入 FCF 在表格并列，便于核对差异。缺失期间断线，无插值。
- 同一证券、期间类型、期间、指标、币种只允许一条记录；实际/估算不重复建行，需明确编辑原记录。不支持多版本重述或同时保存多个口径。

## 财务 CSV

在财务页面的“财务 CSV 导入”选择 UTF-8 CSV，预览前 100 行、映射字段、验证、确认导入，错误报告可保存为文本。所有行关联顶部所选证券。支持最多 5 MB / 10000 行，不覆盖已有指标。

| 字段 | 要求 |
|---|---|
| Period | 必填，年度 YYYY；季度/TTM YYYY-Q1～YYYY-Q4 |
| PeriodType | 必填，Annual / Quarterly / TTM |
| MetricType | 必填，使用下方枚举名 |
| Value | 必填，十进制数字，不含千分符、百分号和单位 |
| Currency | 必填，三个英文字母；RMB 归一为 CNY |
| SourceId | 可空；已存在且属于同一证券的来源 GUID |
| IsEstimated | 可空，默认 false；true / false |
| Notes | 可空，最多 10000 字符 |

支持指标：Revenue、RevenueGrowth、GrossProfit、GrossMargin、OperatingIncome、OperatingMargin、NetIncome、EPS、OperatingCashFlow、Capex、FreeCashFlow、Cash、Debt、NetDebt、Equity、ROE、ROIC、ShareCount、StockBasedCompensation、Inventory、AccountsReceivable。

验证与提交使用相同校验路径；预览验证在真实 SQLite 事务执行后回滚，正式提交重新检查。任一无效/重复行、无效来源或约束失败均整批回滚。重复导入被唯一键检查拒绝。字段映射或目标证券变更使验证结果失效。示例见 `samples/financial-metrics.csv`。

## 来源等级与文件

ResearchSource 包含 SecurityId、Title、Publisher、PublishedDate?、Url、LocalFilePath、SourceType、ReliabilityLevel、Notes、ExtractedText。等级限制 1～6，1 最高、6 最低，由用户核实；没有根据网站或文件名自动判定可信度。

来源类型：AnnualReport、QuarterlyReport、ExchangeFiling、InvestorPresentation、EarningsCall、CompanyWebsite、FinancialDatabase、News、BrokerResearch、ExpertOpinion、SocialMedia、UserNotes、CSV、PDF、Other。

TXT / Markdown / CSV 使用 UTF-8 文本读取；PDF 使用固定版本 [PdfPig 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16) 和其 [ContentOrderTextExtractor](https://github.com/UglyToad/PdfPig) 提取文字。文件导入仅提取文本，不会解析成财务数字；财务 CSV 导入是单独的映射流程。仅扫描图像的 PDF 返回“该PDF可能是扫描件，当前版本不支持OCR。”；损坏/受密码保护文件给出可读错误，不保存空来源。

文件最多 20 MB，PDF 最多 1000 页，提取文本最多 200 万字符。原始路径被保存，原文件保持原位、不复制、不修改；来源全文持久化到数据库，因此原文件移动后仍能阅读已提取文本。没有 OCR、版面还原、财务表格自动识别、链接抓取、附件备份或文件同步。混合扫描/文字的 PDF 仅提取可读取文字，仍需核对原件。

## 评分

| 维度 | 默认权重 |
|---|---:|
| BusinessModel | 20 |
| CompetitiveAdvantage | 15 |
| FinancialQuality | 20 |
| ManagementCapitalAllocation | 10 |
| GrowthPotential | 15 |
| Valuation | 15 |
| RiskUnderstandability | 5 |

每个证券独立保存权重、AIScore?、UserScore?、Reason、Evidence、UpdatedAt。0 是有效分数，空白为未评分。UserScore 优先，否则使用 AIScore，并显示“AI 评分（未经用户确认）”。未覆盖全部有权重维度时只显示已评分权重及已贡献分数，不伪造完整总分。Phase 2 没有 AI 生成或编辑 AI 分数的入口。

## 会计关注规则

只使用当前所选期间类型/币种的最新非估算期间。Normal / Warning / HighAttention 为规则等级；资料不足另显示“未评估”，不把缺失数据视为正常。提示用于核实风险，不判断财务造假。

| 规则 | 默认 Warning | 默认 HighAttention |
|---|---|---|
| 应收/库存同比增速减收入同比增速 | ≥ 15 个百分点 | ≥ 30 个百分点 |
| OCF / 正净利润 | ≤ 80% | ≤ 50% |
| 债务同比增长 | ≥ 25% | ≥ 50% |
| 股票数量逐期增加 | 连续 3 期累计 ≥ 2% | 连续 3 期累计 ≥ 5% |
| SBC / 收入 | ≥ 10% | ≥ 20% |
| FCF 持续偏弱 | 连续 3 期全部 ≤ 0 | 连续 3 期全部 < 0 |

所有阈值、FCF 原币金额上限、连续期数可配置并保存到 AppSetting `Research.RiskThresholds`，对全部证券生效。FCF 金额上限不换汇；修改为非零时应考虑报告币种。增长基期须为正；连续期数要求期间连续，不跳过缺报期；TTM 相邻季度的十二个月窗口有重叠，解释时需注意。

## 实现边界

研究正文当前为一证券一份文档，Revision 用于并发保护，没有历史版本浏览/恢复。来源和财务采用本机单实例显式保存；评分批量写入保证权重一致。全局搜索在本地进行不区分大小写的文本匹配，尚无全文索引与分页，大型资料库需后续性能优化。股票研究与评分不会更改交易、仓位或现金。
