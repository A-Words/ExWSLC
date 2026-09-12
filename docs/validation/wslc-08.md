# 任务 08：运行环境诊断与活动会话验证

验证日期：2026-09-12（Asia/Taipei）。实现、单元测试、只读 CLI 验证及 WPF 控件渲染已完成；完整桌面交互验收仍未完成，具体边界见下文。

## 基线与提交范围

- 起始 HEAD：`72d0943784f994caf5a0b96c43e45994a821226b`，起始工作区干净，处于 detached HEAD。
- 已用 `git merge-base --is-ancestor` 确认包含 `f85b111` 和 `d9e321e`，满足任务 01、02 前置条件。
- 本任务分支：`feat/runtime-diagnostics-sessions`。未合并其他分支、未推送、未改写既有提交。
- 新增类型化诊断查询、设置页只读诊断卡、活动会话、手动刷新／取消、脱敏复制与文本导出、设计数据和双语资源。
- 共享接口增量：`IContainerRuntime.GetSystemInfoAsync`、`IUserInteractionService.SetClipboardText`、`RuntimeWorkspace.GetCapabilitiesAsync`。生产实现、设计实现与测试 Mock 已同步。
- 沿用 `RuntimeFeature.SystemInfo`、`CapabilitySupport`、能力缓存／重检、`RunTrackedAsync` 和工作区取消机制。未创建第二套版本探测。

## 环境与上游核对

| 项目 | 实测值 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL / CLI | 2.9.10.0 |
| 会话管理服务 | 2.9.10 |
| 打包 C# SDK | Microsoft.WSL.Containers 2.9.9 |
| 内核 | 6.18.40.1-1 |
| Direct3D | 1.611.1-81528511 |
| DXCore | 10.0.26100.1-240331-1435.ge-release |
| .NET SDK / 测试运行时 | 10.0.401 / 10.0.12 |

公开发行、上游主线、本机 CLI 与 SDK 分开核对：查询 GitHub releases 时最新稳定版为 [2.7.14](https://github.com/microsoft/WSL/releases/tag/2.7.14)，预览版包含 [2.9.11](https://github.com/microsoft/WSL/releases/tag/2.9.11)，本机仍为 2.9.10.0。没有自动升级，也没有据发行版本号推断本机能力。

已阅读 [system info PR #41408](https://github.com/microsoft/WSL/pull/41408)、[2.9.10 命令实现](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/tasks/SessionTasks.cpp) 和主线同文件。实现查询服务版本与会话清单，不创建会话；当前 JSON 模式在服务不可访问时整条命令失败，因此应用回退到任务 02 已取得的基础版本。单元测试另外覆盖未来或异常情况下返回部分 JSON 的行为。

SDK 核对使用 [官方 C# API](https://wsl.dev/api-reference/csharp/)、[2.9.9 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 和本地 NuGet 2.9.9 包的 README、头文件及程序集。保持原有 SDK 服务版本查询；新的综合信息通过 CLI 实现，没有更换包版本或新增 SDK session。

## 实际命令与结果

以下只读命令均已执行：

```powershell
wslc version
wslc version --format json
wslc system --help
wslc system info --help
wslc system info --format json
wsl --version
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
```

- `system --help` 包含 `info`，子命令帮助包含 `--format`，真实 JSON 的根节点为 `Client` 与 `Server`。
- 实测版本和配置路径与诊断页面逐项一致；会话的 ID、CreatorPid 与同次查询相符。会话名称按下方规则遮蔽。原始个人路径与会话名称未保存到仓库。
- 最终 build：0 警告、0 错误。
- 最终常规测试：246 项，245 成功、1 个 opt-in 实测跳过、0 失败。
- 实测单独显式启用：1 成功、0 失败。该测试不创建资源，也不触发库存或环境安装。
- `git diff --check` 通过；提交前再检查 `git diff --cached --check`。
- 初次测试命令附加 `--nologo` 被本仓库 Microsoft.Testing.Platform 拒绝，已按仓库约定改回上述命令。测试编写阶段发现的异常子类型断言与条件跳过配置错误已修正后重跑。

只读验收入口：

```powershell
$env:EXWSLC_LIVE_DIAGNOSTICS = '1'
dotnet test ExWSLC.sln --filter-method '*LiveQuery*'
Remove-Item Env:EXWSLC_LIVE_DIAGNOSTICS
```

## 重点行为验证

| 行为 | 验证结果 |
| --- | --- |
| 完整 JSON、真实字段类型 | 解析与实测逐项核对通过 |
| 部分 JSON、错误字段类型、未知字段 | 保留有效字段，不识别字段显示未知，通过 |
| 空会话／会话查询不可用 | 空数组与缺失数组分别显示，通过 |
| CLI 非零退出、异常、坏 JSON | 不暴露原始错误，保留基础版本及可用部分字段，通过 |
| 无 CLI／旧版无 system info | 明确不支持时不执行命令，显示回退原因，通过 Mock 验证 |
| 能力检测异常 | 设置页仍可发起独立查询，通过 |
| 刷新与取消 | 防止重复刷新，沿用任务取消；取消保留原快照及时间，通过 |
| 超时 | 10 秒期限已设置；异常到超时状态的映射通过单测，真实卡死进程未人为制造 |
| 复制／导出 | 同一脱敏摘要，导出到隔离临时文件并清理；剪贴板／文件错误提示通过 Mock 和文件测试 |
| 本地化 | 双语键完整，语言变化通知所有诊断绑定且不重新查询，通过 |

## 白名单与脱敏边界

诊断只展示采集时间、受约束的版本字符串、脱敏配置路径、会话 Name／ID／CreatorPid。自定义 JSON 字段、凭据、环境变量、命令行、编译信息、stdout／stderr 和异常消息均不进入摘要。

- 本机默认配置路径只显示 `%LOCALAPPDATA%\wslc\settings.yaml`；其他配置路径显示 `[redacted]`。
- 默认会话名显示 `wslc-cli-[user]`；GUID 名称保留；其他自由文本会话名显示 `[redacted]`。这是首版的保守规则，不提供原文显示开关；可用真实 ID 与创建进程区分会话。
- 未知版本格式显示未知，不猜测版本号。基础 CLI、SDK 查询到的服务版本与本次 system info 版本分开标注来源。
- 查询失败会发布本次失败快照，避免把上次的活动会话当作当前清单。用户取消则保留旧快照、旧时间并显示取消提示。
- 首次进入设置页且工作区空闲时采集，否则手动刷新。诊断不跟随库存轮询；“重新检测”沿用原能力服务的缓存失效规则。
- 仅用户选择导出时写摘要文件；没有写入应用设置或原生 `settings.yaml`。未扫描其他用户配置，未新增切换／终止／迁移会话操作。

## 截图与 UI 验证边界

截图位于 `docs/validation/screenshots/wslc-08/`，共 16 张：

- `before-{en-US,zh-CN}-{Light,Dark,System}.png`：起始基线的真实 WPF SettingsPage、设计数据。使用改动前构建保留的程序集渲染。
- `after-{en-US,zh-CN}-{Light,Dark,System}.png`：本次真实 CLI 查询的脱敏数据、实际 SettingsPage 控件，850×700 页面视口。
- `narrow-{en-US,zh-CN}-Light.png` 及对应 `-bottom.png`：550×700 视口，核对按钮换行、单列字段、长版本与会话滚动可达。

已查看六种语言／主题组合的诊断布局及窄视口样本，未见诊断卡文字遮挡；系统主题在本机解析为浅色。修改前后例图：[之前](screenshots/wslc-08/before-zh-CN-Light.png)、[之后](screenshots/wslc-08/after-zh-CN-Light.png)、[英文深色](screenshots/wslc-08/after-en-US-Dark.png)。

可复现的渲染入口（从仓库根目录运行，默认输出到忽略的 artifacts）：

```powershell
$env:EXWSLC_LIVE_DIAGNOSTICS = '1'
dotnet run --project docs/validation/diagnostics-render/DiagnosticsRender.csproj
Remove-Item Env:EXWSLC_LIVE_DIAGNOSTICS
```

这些图片通过 WPF `RenderTargetBitmap` 渲染实际页面，不是完整主窗口桌面截图。当前会话的原生桌面控制接口不可用，因此没有把渲染检查算作完整运行验收。尚未验证：完整主窗口内的真实鼠标／键盘导航、真实剪贴板与保存对话框、操作系统主题实时切换通知、高 DPI、真实旧版 CLI 或 WSL 服务故障下的桌面流程。对应逻辑已通过回归测试；上述桌面项目仍需人工补验。

整个验收过程只读取当前用户可见的系统信息，没有创建、删除、停止已有容器或会话，也没有执行全局 prune、shutdown、原生配置变更或 WSL 升级。
