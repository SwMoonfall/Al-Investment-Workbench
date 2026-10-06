# 确定性估值引擎 · Phase 4

## 使用

选择“估值”页面的证券，建立模型，填写基础数据及来源。每个模型保留 Bear、Base、Bull 三个情景，初始参数相同，需用户调整。可编辑基础数据、编辑各情景、复制 Base 到 Bear/Bull、查看逐年 DCF、删除模型。多个模型可关联同一证券。

保存是显式且原子的；取消表单不写入。Revision 拒绝过期编辑。复制会覆盖目标情景并把假设日期更新为今天，需界面确认。模型类型建立后固定；若需另一方法，建立独立模型。

## 单位与日期

- 所有金额使用模型币种的完整金额；股数用完整股数。不能混用元与万元、股与百万股。
- 比例输入小数，0.10 表示 10%；UI 输出百分比。
- 当前价格为每股价格，必须大于零；股数必须大于零；净债务可为负（净现金）。
- Actual：用户录入的基础数据，未经系统核验，包括基期收入、净债务、股数、当前价格及来源说明。币种必须等于证券币种；暂不自动外汇换算。
- Assumption：各情景的增长、利润率、倍数、期限、折现率等。保存假设日期。来源日期、当前价格日期单独保存和展示；证券报价之后改变不会静默改变已保存模型。
- Calculated：由 Domain 纯代码计算的结果。反推的参数是求解结果，不冒充 Actual。
- 模型新建表单中的金额和参数仅为可修改的起点，不是推荐假设；应替换为核实后的数据。

## PE

ImpliedPrice = FutureEPS × TargetPE + NetCashAdjustment / ShareCount。

NetCashAdjustment 是额外调整的**总金额**，可为零；不再减去模型净债务，避免重复调整。EPS 是股东收益口径，应明确其适用股数和预测时点，净现金调整需避免重复计入 PE 定价。

UpsideDownside = ImpliedPrice / CurrentPrice − 1。

AnnualizedReturn = (ImpliedPrice / CurrentPrice)^(1 / HoldingYears) − 1。

期限由用户输入，不默认作为交易规则。负的每股价值保留，年化不适用；零价值年化为 -100%。不包含股息、税费或稀释变化。

## EV/EBITDA 与 EV/FCF

EnterpriseValue = EBITDA（或 FCFF）× TargetMultiple。

EquityValue = EnterpriseValue − NetDebt；ImpliedPrice = EquityValue / ShareCount。

可直接输入预测 EBITDA/FCFF 总额，留空则使用基期 Revenue × 对应 Margin；直接输入优先且界面明确标识。显式 0 与留空不同。EV/FCF 的 FCF 必须是**企业自由现金流 FCFF**，不可把股东现金流当作企业现金流再减债务。负现金流、负企业价值和负股权价值原样呈现，不截断为零。与 PE 相同，仅按用户指定期限计算目标价格回报。

## Simplified DCF

采用企业自由现金流，期末折现，预测 1–30 年；所有情景独立计算：

Revenue[t] = Revenue[t−1] × (1 + RevenueGrowth)

EBIT[t] = Revenue[t] × OperatingMargin

Tax[t] = max(EBIT[t], 0) × TaxRate

D&A[t] = Revenue[t] × DepreciationAssumption

Capex[t] = Revenue[t] × CapexAssumption

ΔNWC[t] = (Revenue[t] − Revenue[t−1]) × WorkingCapitalAssumption

FCFF[t] = EBIT[t] − Tax[t] + D&A[t] − Capex[t] − ΔNWC[t]

PV[t] = FCFF[t] / (1 + DiscountRate)^t

终值年 N+1 以 TerminalGrowth 增长收入，再重新计算 EBIT、税、折旧、Capex、ΔNWC 和 FCFF。不能简单将末年 FCF 乘以 (1+g)，因为预测增速与永续增速不同会改变营运资本增量。

TerminalValue = FCFF[N+1] / (DiscountRate − TerminalGrowth)

PVTerminalValue = TerminalValue / (1 + DiscountRate)^N

EnterpriseValue = ΣPV[t] + PVTerminalValue

EquityValue = EnterpriseValue − NetDebt；ImpliedPrice = EquityValue / ShareCount。

DiscountRate 视作 WACC，必须大于零且大于 TerminalGrowth。负 EBIT 不确认即时现金税盾；未建模亏损结转。稳定利润率用于整个预测期与终值，没有逐年利润率曲线。折旧、资本开支、营运资本按收入比例，未单独建模固定资产寿命或 ROIC。

**DCF 输出是今日现值。** 现值对市价的差额不是未来价格回报；年化显示“不适用”。把它直接按预测期年化会混淆折现与回报。Reverse 同样不显示年化回报。

## Reverse Valuation

固定其余输入，通过同一 DCF 函数反求收入 CAGR 或稳定营业利润率，使每股现值等于当前价格。利润率反推应用于全部年份及终值。

- 二分法；默认最多 200 次，可设 1–1000。
- 容忍度是每股价格的绝对误差，默认 0.000001；可设 0.0000000001–1。
- 边界必须显式提供，下界小于上界。允许 CAGR -99%–500%，利润率 -100%–100%。默认 CAGR 搜索 -50%–100%，用户可调整。
- 先检查端点；端点满足误差即成功。端点没有夹根返回 NoBracket，含义是该区间没有可用的符号变化，不代表全域无解或唯一解。
- 达上限返回 IterationLimit；精度不足或溢出返回 NumericalFailure；非法设置返回 InvalidInput。失败不返回虚构的估值或求解值。
- 不因区间足够小便宣称成功，必须满足价格误差。循环始终有限。
- 非线性情景可能多根；本版本不搜索全部根，也不证明唯一性。反推是当前价格所要求的假设，不是增长预测。

## 实现与验证

Domain/Valuation 是无外部依赖的确定性计算。Application/ValuationService 组织三情景比较。Infrastructure/ValuationStore 使用现有 DatabaseWriter 事务，单独两表和第五个迁移；Portfolio 核算不变。

ValuationModel 保存共享基础数据、证券外键、类型和 Revision。ValuationScenario 按模型 + Kind 唯一，保存带 SchemaVersion=1 的不可变输入记录 JSON 和假设日期。每次读取重新计算，避免结果缓存与输入不一致。JSON 只包含数字、枚举、备注，不含程序代码。未来扩展要显式处理 SchemaVersion，未知版本拒绝。Revision 用于并发检查，并非历史快照；估值历史版本尚未提供。

测试覆盖独立手算 PE/EV/DCF、终值营运资本、永续现金流解析值、负现金流、净现金、零股数、极端值、反推已知解/端点/无夹根/迭代上限、SQLite 重开、复制隔离、回滚、引用保护及 Phase 3 升级备份。WPF 验收通过真实编辑表单的 ViewModel 命令操作，渲染实际窗口；不是鼠标自动化。

全部计算不依赖 LLM，不调用 AI API，不触发交易。
