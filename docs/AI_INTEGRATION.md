# Phase 6 · AI 研究增强层

AI 只生成研究建议。PortfolioLedger、ValuationEngine、ThesisRules 和人工决策清单仍由原有确定性代码负责。没有 AI 写交易、改估值、改 Thesis / KillCondition 或评分的入口，也没有工具调用或自动交易。

## 使用

1. 打开独立的“AI服务”页面，新增提供商并选择 OpenAI、GoogleGenAI、Anthropic 或 OpenAICompatible。按类型填写连接字段。
2. “保存配置”仅本地保存；“保存并获取模型”保存后获取远端模型。也可自定义模型 ID。
3. 密钥通过 PasswordBox 输入，以 Source ID、类型和地址隔离并使用 Windows DPAPI 加密。留空保留原密钥，地址变化后需要重新配置。
4. 添加 / 启用模型后设为当前模型；“调用测试”发送最小请求，可能产生费用。详情见 [AI服务管理](AI_SERVICES.md)。
5. AI 研究助理 → 选择证券和模板 → 可预览上下文、补充最多 6000 字符 → 开始单项分析。Portfolio Risk Review 使用当前账户，Journal Review 使用当前账户指定日期范围内的真实日志及关联逻辑；专用入口见“日志与复盘”。
6. “完整公司研究 · 10 步”顺序执行 Business Model、Growth Drivers、Financial Quality、Competitive Position、Accounting Risk、Bull / Base / Bear Case、Thesis Challenge、Monitoring KPI。每步独立审计并保存，取消按钮立即取消当前等待；失败时停止后续步骤，报告保留成功章节并标注不完整。
7. “结果与审计”查看分类结果、原始输出、来源、精确上下文、提示词、模型、地址和响应 ID。“研究报告”查看自动保存的统一报告，可导出 Markdown。刷新载入当前对象的历史；全局任务的分析历史与证券分析分开。

内置 15 个模板：用户要求的 12 个模板，加完整工作流所需 Growth Drivers、Base Case、Monitoring KPI。支持查看、修改、复制、恢复默认；Revision 阻止过期编辑。旧分析保留当时的提示词，不随模板编辑变化。恢复默认只影响选定模板。

## 技术边界

`Application/AI` 定义 IAIProvider、ISecretStore、IAIContextBuilder、IAIStore 及工作流；不引用具体 SDK 类型。`AI/OpenAIProvider` 使用官方 NuGet `OpenAI 2.14.0` 的 `OpenAI.Responses.ResponsesClient`、`CreateResponseOptions`、`CreateResponseAsync`，已通过安装包 README/XML、反射和模拟 HTTP 核对签名。Responses 类型在此版本带 OPENAI001，警告仅在适配器局部禁用。SDK 版本固定，不在业务层蔓延。

OpenAI Responses 请求设置 `store=false`、用户配置的模型/输出上限、严格 JSON Schema；关闭 SDK 日志、消息内容日志与 tracing；禁用自动重试和 HTTP 重定向。`store=false` 是响应存储选项，不等于承诺服务商零留存。遵循所选服务方政策。

应用和 Provider 双层 CancellationToken / Timeout（1–600 秒），工作流还用 WaitAsync 防止不遵守取消的替代 Provider 阻塞 UI。实际官方适配器向 HTTP 传递取消。无自动重复收费请求；用户可明确重试。取消不能保证服务端停止计费。

错误以安全分类显示：未配置、认证失败、余额不足、429 限流、网络、超时、取消、服务异常、响应未完成。服务商错误原文不会进入日志或用户错误消息；正常/未完成的响应封套和模型输出进入本地审计。无效 JSON 或未知引用保留原文，标记 InvalidOutput，不能显示为成功。

输出固定包含 FACTS / MANAGEMENT_CLAIMS / ASSUMPTIONS / AI_INFERENCES / RISKS / QUESTIONS 和六个挑战字段。所有条目有 text 与 source_ids。FACTS 必须引用提供的来源 ID；解析检查结构和引用存在性，**不保证事实真实或引用确实支持论断**。界面明确要求用户核实。

Thesis Challenge 的固定提示要求假设逻辑可能错误，以做空者、竞争对手、保守型基金经理视角挑战；即使用户修改模板，该框架和“不告诉用户买卖、不修改数据”约束仍保留。潜在 Kill 信号只在 AI 文本内，不写入 KillConditions。提示不能保证模型总遵守边界；没有业务写入工具才是执行隔离。

## 上下文与隐私

只读取指定对象的投影：证券资料、最多 5 个来源摘录（每个 2000 字符）、有限研究笔记；财务任务最多最近 120 条指标，FCF 与会计规则由代码计算。Thesis 任务提供当前逻辑、假设与 Kill 条件（各最多 15）；估值评估最多 3 个模型的输入日期和代码计算结果。组合任务提供当前账户摘要、前 10 持仓、最多 30 个暴露项目及有限风险条目，不发送完整交易历史。

上下文没有密钥、AppSetting、其他证券全文或原文件路径；不上传原始附件。截断摘录显式标记，缺失数据不可视为零。总上下文设大小上限，超限明确失败。每一步重新读取并保存当时快照，报告中不同章节可能对应稍有变化的资料，查看审计日期核对。

Sources、财务输入、用户笔记均为不可信资料，不执行其中指令。当前来源等级仍由用户核实。结构化文本在只读 TextBox 展示，不执行 HTML、脚本或自动打开链接。

旧 `AI.Settings` 会迁移至 Provider Source / Model 目录，仅存非敏感配置。`WindowsSecretStore` 使用 `ProtectedData` / `DataProtectionScope.CurrentUser`（包 10.0.9），路径为数据根目录 `Secrets/<scope SHA256>.dpapi`。明文只短暂存在当前进程内存，未存 SQLite、配置文件或日志。加密文件无法保证跨 Windows 用户/机器可用；数据库备份不包含密钥，迁移机器后重新输入。忽略 Secrets、*.dpapi 与临时加密文件的 Git 跟踪。

## 保存与故障恢复

迁移 `AIIntegration` 仅增加 PromptTemplates、AIAnalyses、AIResearchReports，不更改稳定投资表。沿用升级前 SQLite 备份与外键 Restrict。已结束分析在应用数据层禁止覆盖或删除；这是应用级保护，不是防本机数据库管理员篡改的签名系统。

请求前保存 Running 和输入快照，返回后原子保存结果。取消/超时后的审计写入使用未取消的数据库令牌。每个报告章节结束即更新报告；启动时将上次未结束的记录标记 Interrupted，不自动重试。故障不切换本地投资模块。磁盘故障、损坏或无法写入属于本地存储故障，不能承诺在此情况下成功保存网络结果。

## 验证与限制

测试全部离线：FakeAIProvider 覆盖组装、解析、来源、错误、取消、忽略取消时的超时、部分报告、完整流程、保存与重开；自定义 HttpMessageHandler 验证官方 SDK 实际 Responses 请求、strict schema、模型配置、store=false、认证/配额/429、无自动重试。真实 Windows DPAPI 加密回读也被测试。

显式 `--smoke-test --data-root <隔离目录>` 才注册离线 SmokeAIProvider；正常运行按当前 Source 类型分发至原生或兼容适配器，默认 Provider 为 Disabled。测试数据中的 offline-test-model 不是生产默认模型。

未使用真实 API Key 或真实网络推理，账户模型权限、实际收费、网络代理与自定义服务兼容性需用户配置后用连接测试核对。未实现 streaming、自动重试、分词预算估计或检索增强索引。Phase 7 已提供独立 Journal 与保存复盘的只读上下文，条目数量、价格口径与历史缺口见 JOURNAL_AND_REVIEW.md。没有 AI 自动评分或业务数据自动写回。

官方参考：[SDK 与语言库](https://developers.openai.com/api/docs/libraries)、[Responses 迁移指南](https://developers.openai.com/api/docs/guides/migrate-to-responses)、[Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs)。核对日期：2026-10-04。
