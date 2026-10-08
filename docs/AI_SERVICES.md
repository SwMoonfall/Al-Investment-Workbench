# AI服务：提供商与模型管理

AI服务是左侧导航中的独立页面。Source 保存连接信息，模型通过稳定的 Source ID 关联。添加模型不会自动启动 AI；必须选择一个已启用模型并点击“设为当前模型”。研究助理后续请求从当前模型关联的 Source 读取连接配置。

## 使用流程

1. 点击“新增提供商”，填写名称，选择类型，按需配置 Base URL、API Key、超时和输出上限。
2. 点击“保存配置”仅保存本地连接配置；点击“保存并获取模型”先保存，再请求远端模型目录。后一步失败不会撤销已保存的配置。
3. 在模型列表选择模型，点击“添加 / 启用”。也可填写自定义模型 ID；同一 Source 下重复添加只会启用已有模型，保留模型 ID。不同 Source 可拥有相同的远端模型名称。
4. 可禁用或移除本地模型。禁用、移除当前模型，或删除其 Source，会清空当前模型选择，后续分析要求重新选择。
5. “调用测试”对选中的本地模型发送最小文本请求，不发送研究资料，也不改变当前模型；可能产生 API 费用。模型列表可见不保证模型有调用权限或支持结构化研究。

## 支持的协议与动态字段

| 类型 | 默认根地址 | 模型发现 | 原生调用 | 专用字段 |
| --- | --- | --- | --- | --- |
| OpenAI | `https://api.openai.com/v1/` | `GET models` | 官方 SDK Responses | Organization、Project（可选） |
| GoogleGenAI | `https://generativelanguage.googleapis.com/v1beta/` | `GET models`，分页并筛选 generateContent | `models/{id}:generateContent` | Google AI Studio API Key 提示 |
| Anthropic | `https://api.anthropic.com/v1/` | `GET models`，游标分页 | `POST messages` | Anthropic API Version |
| OpenAICompatible | 必填 | `GET models` | Chat Completions 或 Responses | 兼容协议选择 |

所有地址要求 HTTPS，不能包含用户名、密码、查询参数或片段。Google 使用 `x-goog-api-key`，Anthropic 使用 `x-api-key` 与 `anthropic-version`，OpenAI 和兼容层使用 Bearer。Google 的 `models/` 前缀会规范化。结构化研究分别使用 Responses JSON Schema、Google responseJsonSchema、Anthropic output_config.format 或兼容服务的 response_format；模型与服务需支持相应能力，不自动降级或重复发送收费请求。

## 保存、草稿与关系变化

- 已保存 Source ID 不随名称、地址或类型编辑而改变。新 Source 在首次保存前取得稳定的草稿 ID；保存成功后才允许操作模型。
- 切换提供商或页面保留各自草稿；刷新页面不覆盖草稿。模型操作要求先保存或放弃当前草稿。关闭应用时有未保存草稿会提示。
- 更换 Provider 类型后保存会清除原类型的模型关联和当前模型选择；编辑连接配置会清除该 Source 的远端发现缓存。
- Source、类型和规范化地址共同决定密钥作用域。更换地址不会将已保存密钥发送给新地址。输入框留空保留当前作用域的密钥；“删除密钥”显式删除。
- 密钥使用 Windows DPAPI，数据库只存非敏感配置。保存失败会尝试恢复原密钥。删除 Source 清理当前作用域密钥；以前地址留下的加密文件及迁移前密钥不会再被该 Source 使用。
- 旧单一 OpenAI 配置在首次进入 AI服务页面时迁移为一个 Source 和当前模型，复制旧密钥到新作用域。旧密钥无法解密时仍迁移连接，用户可重新输入密钥。
- 数据库沿用 AppSettings 表，`AI.ProviderCatalog` 存当前模型 ID，`AI.ProviderSource.*` 和 `AI.ProviderModel.*` 分别存实体，单次事务提交；不需要新增数据库迁移，避免单条设置 4,000 字符限制。
- 请求期间显示进度、锁定页面编辑并提供取消。网络错误、空结果、无权限、超时和取消有明确状态；远端列表刷新失败仍保留本地模型。API 错误正文不显示或写入日志。

## 验证

单元测试使用模拟 HTTP 检查四种类型的认证、请求路径、结构化载荷、响应解析、分页、重复模型、错误、超时与取消；持久化测试覆盖迁移、100 个模型、密钥隔离、非法关联和禁用回退。WPF ViewModel 测试覆盖保存后发现失败、草稿切换、并发操作保护、禁用/移除/删除和保存重试。

实际 WPF 离线验收：

```powershell
. ./scripts/Use-LocalDotnet.ps1
dotnet build AIInvestmentWorkbench.sln -m:1 -p:UseSharedCompilation=false
dotnet test AIInvestmentWorkbench.sln --no-build -m:1
./scripts/Test-AIServices.ps1
```

桌面验收为四种类型渲染实际页面，检查密码框保存后清空、页面切换保留草稿、浅色/深色主题和 WPF 绑定错误。所有测试只使用模拟 Provider 和隔离数据库；未使用真实 API Key，真实账户的授权、计费、代理和模型能力需要配置后测试。

接口核对（2026-10-07）：[OpenAI Models](https://developers.openai.com/api/reference/resources/models/methods/list)、[OpenAI Responses](https://developers.openai.com/api/reference/responses/overview)、[Google Models](https://ai.google.dev/api/models)、[Google generateContent](https://ai.google.dev/api/generate-content)、[Anthropic Models](https://platform.claude.com/docs/en/api/models/list)、[Anthropic Messages](https://platform.claude.com/docs/en/api/messages/create)。
