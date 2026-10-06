# Portfolio Risk & Decision System · Phase 5

## 操作入口

左侧“风险 Risk”打开当前账户风险工作台。Portfolio 选中持仓后点击“风险 / 决策”会带入证券。账户通过主窗口右上角切换，页面上方选择研究证券。风险总览始终包含当前账户全部持仓，不因研究证券选择而缩小范围。

四个标签页：风险总览、证券风险标签、仓位规划与清单、决策历史。

## 风险标签与暴露

RiskTag 是可复用标签，通过 SecurityRiskTag 多对多关联证券。同一证券可以关联多个标签；重复添加相同标签不会重复计入。自定义标签按“分类 + 忽略大小写的名称”唯一。

分类：Sector、Market、Currency、Theme、Custom。默认提供 Technology（Sector）、ChinaA/HongKong/US/Other（Market）、CNY/HKD/USD（Currency），以及 Growth、Value、Cyclical、Defensive、Semiconductor、AI Capex、InterestRateSensitive、Commodity、HighValuation（Theme）。Sector / Market / Currency 是分类，不是无含义的同名风险标签。

未显式分配 Sector、Market、Currency 中某分类时，使用证券资料的对应字段；缺行业显示“未分类”。分配该分类的标签后，以标签替代对应字段作为暴露归组。多个同分类标签表示完整仓位同时属于多个组，不自动拆分权重。移除所有该分类关联后恢复资料字段。

Position Weight = Position MarketValue / (全部证券 MarketValue + CurrentCash)

Exposure(tag) = Σ 每个含该标签证券的 Position Weight

- 同一证券同一分类同名标签只算一次；不同标签可重叠，主题暴露之和可能大于 100%，不能相加作为总资产。
- 行业、市场、主题、自定义暴露不包含现金。币种暴露包含账户现金，并按账户币种归组；现金不计入证券数。
- Currency 默认是证券计价币种。手工 Currency 标签可以作为底层币种风险代理，但不是自动汇率换算，也不是财报地域收入或 ETF 底层资产穿透。
- 当前交易账本仍为单账户单币种，风险模块不绕过此约束，不进行没有汇率的跨币种市值相加。
- 未设置价格时按现有组合模块的成本口径暂估，显示估价数量与价格日期，不把暂估伪装成实时行情。
- 空仓时集中度为零，纯现金账户显示相应币种现金暴露；零资产账户不执行除零运算。

## 集中度与风险提示

显示最大单证券仓位（含 ETF）、最大股票仓位（不含 ETF / Other）、Top 3、Top 5、最大行业暴露、最大市场暴露。Top 3/5 按当前持仓权重降序取前 N 个，不足 N 个取全部；分母仍为含现金总资产。

默认达到 MaxWeight 的 90% 提示“接近上限”，超过 MaxWeight 提示“超出上限”。预警比例可在页面修改并持久化；不是自动卖出或加仓规则。零 MaxWeight 配合正仓位会超限；零仓位不产生无意义的接近提示。

Risk Dashboard 汇总当前账户持仓相关的未归档 Draft / Active / Warning Thesis。展示触发 Kill 条件、Thesis Warning 数量和依据；Invalidated / Closed / 已归档历史不累计到开放逻辑警示。决策清单另外保留所选证券最近 Thesis 状态，并突出唯一当前 Thesis。

会计 High Attention 复用 Phase 2 确定性规则与已保存阈值，按证券、Annual / Quarterly / TTM、报告币种分别计算；估计值排除。显示规则、期间、币种和证据。无资料或不完整指标明确计入“数据不足”，不解读为风险正常。同一问题跨报告口径可能多条，并非独立证券数。不会输出 Fraud。

不计算 VaR、统计相关矩阵或自动推断相关系数。“组合相关性是否上升”保留为用户核对问题；主题重叠只能作为判断依据之一。

## 仓位规划

SuggestedPosition = BasePosition × ConfidenceFactor × ValuationFactor × RiskFactor

参数均由用户输入。BasePosition 为总资产比例，0.05 表示 5%；系数不得为负，可以为零。结果是目标**总仓位**，不是追加买入量。结果不会自动截断到 MaxWeight；超过 MaxWeight 或 100% 总资产时明确提示。

页面始终显示：**“计算结果仅为仓位规划辅助，最终仓位由用户决定。”**

修改参数、切换证券或刷新资料后需要重新计算，防止展示旧结果。计算本身不产生交易；计算后打开加仓清单会将规划参数与决策一并保存。未计算时也可使用清单，明确没有保存仓位规划。

## 持久化决策清单

加仓清单包含八项：Thesis Active、Kill Triggered、基本面改善、估值改善、只是股价下跌、接近 MaxWeight、组合相关性上升、风险明显增加。最终用户选择 Add 或 NoAction。

退出清单包含五项：Thesis 被证伪、估值过高、组合集中度、高优先级机会、只是短期跌价。最终用户选择 Hold、Trim、Exit 或 NoAction。

所有问题都必须明确选择“是 / 否 / 未知”，并选择最终决策、填写非空理由。系统提供当前仓位、阈值、标签、当前及最近 Thesis 状态、触发条件、会计关注和集中度作为核对信息，不替用户填答或决定结果。即使有触发条件，仍由用户决定并记录理由，不阻止人类保留不同判断。

每次确认保存都创建独立 DecisionRecord，包含：账户、证券、清单类型、用户选择、完整答案、理由、时间、可选仓位规划参数，以及整个当时风险快照。未持有的证券也可建立决策，权重为零，上限使用账户默认值，保留该证券标签与研究状态。

预览与保存均从数据库一致性事务读取。保存时重新计算上下文指纹；如果相关持仓、报价、风险暴露、阈值或开放 Thesis 版本变化，则拒绝过时清单，不写入半份记录。用户关闭、刷新后重新核对。正常保存原子提交，**没有创建、修改或删除交易的代码路径**。

历史只追加，不提供覆盖或删除接口；DbContext 拒绝修改/删除 DecisionRecord。历史按保存快照展示，不随新的行情、标签、阈值与研究改写。记录纠错应新增清单并在理由引用旧记录。数据库管理员仍可直接修改 SQLite 文件，应用层不可变性不是防篡改审计认证。

## 数据与架构

- Domain/Risk：RiskTagCategory、暴露聚合、集中度、仓位公式、上限提示、清单契约和验证。
- Domain/Entities：RiskTag、SecurityRiskTag、DecisionRecord；全部 Guid Id / CreatedAt / UpdatedAt。
- Application/Interfaces/PortfolioRiskContracts：仓储接口、风险与决策 DTO；快照 SchemaVersion=1。
- Infrastructure/Services/PortfolioRiskStore：SQLite 读事务、标签写入、决策一致性检查、快照存储，复用 DatabaseWriter。
- App：RiskViewModel、RiskView、PortfolioRiskDialogs，复用现有导航、编辑器、主题和异常处理。
- 第六个迁移增加 RiskTags / SecurityRiskTags / DecisionRecords；外键 Restrict，标签名称及证券关联唯一；决策类型与选项约束。
- Risk.NearMaxRatio 保存在 AppSettings。默认标签启动时幂等补齐，不创建示例账户或示例决策。
- PortfolioLedger / PortfolioStore 核算逻辑保持不变。数据库、日志和迁移前备份位置不变。

测试覆盖多标签聚合和去重、主题重叠、Top 3/5、股票与 ETF 区分、现金/零资产、仓位公式、边界、上限配置、Kill 联动、会计数据不足、全部决策选择、持久化重开、过时表单拒绝、不可覆盖历史、账户隔离、升级备份及无交易副作用。WPF 验收通过实际 ViewModel 表单命令与真实窗口渲染，非鼠标自动化。
