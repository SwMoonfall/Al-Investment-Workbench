# Release 1.0 · Windows x64

## 发布方案

自包含目录发布，内含 .NET Windows Desktop Runtime；不使用裁剪或单文件打包，保留 WPF/EF/PDF/SDK 所需组件。可直接运行整个 app 文件夹，也可使用 Setup 按当前用户安装。安装位置默认为 `%LOCALAPPDATA%\Programs\AIInvestmentWorkbench`，用户数据独立位于 `%LOCALAPPDATA%\AIInvestmentWorkbench\Workspaces\Default`。

采用 Inno Setup 7.1.0，官方安装程序签名发布者 Pyrsys B.V.。同一 AppId 支持后续版本覆盖安装，创建开始菜单项和图标；卸载不删除用户数据库。安装时如程序占用文件，会提示关闭，不强制终止进程；升级后数据库由应用首次启动执行副本迁移。

当前产物没有应用代码签名证书，也没有自动更新服务。安装验证在本机隔离路径完成；面向公众发布前应使用发行方证书签名并扩展干净 Windows 设备验收。商用使用 Inno Setup 需按其官方许可条款处理，构建工具不随安装包分发。

## 构建命令

Windows + .NET 10 SDK + 官方 Inno Setup 编译器：

```powershell
./scripts/Publish-Release.ps1 -InnoCompiler 'C:\path\to\ISCC.exe'
```

脚本依次执行：

```powershell
dotnet clean AIInvestmentWorkbench.sln --configuration Release
dotnet restore AIInvestmentWorkbench.sln
dotnet build AIInvestmentWorkbench.sln --configuration Release --no-restore
dotnet test AIInvestmentWorkbench.sln --configuration Release --no-build --logger trx
dotnet publish src/AIInvestmentWorkbench.App --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o outputs/phase8/app
ISCC.exe installer/Workbench.iss
```

版本号在 Directory.Build.props 与安装脚本中同步维护；不要仅改安装器版本而未更新程序集。脚本失败立即停止，日志位于 outputs/phase8，安装包在 outputs/phase8/installer，SHA256.txt 记录校验值。默认查找工作目录中已安装的构建工具，也可显式指定编译器。图标源由 scripts/Create-Icon.ps1 可重复生成。

## 验收流程

- 基线全量测试，再运行 Release clean / restore / build / test / publish 与安装器编译。
- 隔离数据目录运行 `app/AIInvestmentWorkbench.App.exe --smoke-test --data-root <验证目录>`，实际打开 WPF 页面和编辑器：账户、证券、观察、指标、来源、评分、Thesis、假设、Kill、三情景估值、交易、风险、Journal、离线公司研究与反证、季度复盘、备份。
- 同目录第二次运行，执行待恢复备份替换、恢复前安全备份、重启读取与全流程回归。
- `--performance-test --data-root <另一隔离目录>` 验证 600 证券、1500 交易、14400 财务指标与 100 个合成 AI 分析。采集启动与页面耗时、UI 心跳延迟。
- 检查 Light/Dark 文字对比度、键盘焦点遍历，按 125% / 150% / 200% 进行 WPF DPI 渲染和相应逻辑视口检查。物理显示器间切换仍需多设备验证。
- 安装旧版测试包 → 安装新版 → 从安装目录启动 → 卸载，检查开始菜单、版本和用户数据保留。

所有验收数据均为合成数据；AI 使用 FakeAIProvider，无真实网络请求和费用。运行参数必须提供独立数据根目录，禁止把验收指向正式账户。正常启动没有自动创建业务示例。

## 发行检查边界

备份包括数据库和已提取文本，不包括外部原件与 DPAPI 密钥；文件恢复限制见 BACKUP_AND_RESTORE.md。默认日志保留需用户管理。没有实时行情、券商交易、跨币种组合核算或自动交易。升级不意味着可直接用旧程序打开新模式数据库。安装器不提供静默降级保证。

官方参考：[.NET 发布](https://learn.microsoft.com/dotnet/core/deploying/)、[Inno Setup 下载与许可](https://jrsoftware.org/isdl.php)、[编译命令](https://jrsoftware.org/ishelp/topic_compilercmdline.htm)、[Setup 命令参数](https://jrsoftware.org/ishelp/topic_setupcmdline.htm)。最终实测结果以 outputs/phase8/PHASE8_REPORT.md 为准。
