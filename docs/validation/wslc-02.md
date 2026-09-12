# 02：SDK 与运行时能力检测验证

验证日期：2026-09-12；任务开始基线为已完成 01 的 `d9e321e`。设计约定见 [运行时能力契约](../runtime-capabilities.md)。

## 变更

- `Microsoft.WSL.Containers` 从 2.9.3 升级至实际已发布的 2.9.9。版本属性同时供 PackageReference 和应用 AssemblyMetadata 使用，SDK 版本展示不再读取服务版本。无 RID 的默认构建按 .NET SDK 进程架构选择本机原生 DLL；显式 RID 发布仍由包的 targets 选择目标架构。
- CLI 版本优先读 `Client.Version`，回退 `version` / `--version` 的 `wslc <版本>` 文本。`ServiceVersion` 来自 SDK 的服务查询，三个来源在设置页分别显示。无法取得版本、SDK 无法加载和服务未响应分别表达，不让辅助探测失败阻止 CLI 库存。
- 新增 19 个功能项，分别提供 Supported / Unsupported / Unknown、原因资源键和帮助来源。缓存检测快照；普通库存刷新不重跑命令，用户可“重新检测”。检测使用版本和帮助，不执行功能命令。
- `IWslcSdkService` 封装真实 SDK。安装前重新读取缺失组件，显式传入 `InstallOptions.Components`，不执行 Repair；`SdkNeedsUpdate` 提示更新应用，不允许通过组件安装修复。令牌传给 WinRT，安装成功后重新检测并恢复库存及自动刷新。
- 初始化检测异常在 MainViewModel 转为可见错误，关闭窗口期间的取消不会从 Loaded 的异步调用传播为未处理异常。设置页运行时说明保存资源键和参数，语言切换通知刷新文字，不重新探测。

## 上游和版本证据

- [NuGet 版本索引](https://api.nuget.org/v3-flatcontainer/microsoft.wsl.containers/index.json) 在本次检查中只返回 `2.9.3`、`2.9.9`，没有使用虚构的 2.9.11 SDK 包。
- 已阅读 [官方 C# API 参考](https://wsl.dev/api-reference/csharp/)。安装调用以 [SDK 2.9.9 的公开 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 和 [WslcService 实现](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/WslcService.cpp) 为准：该包的异步安装签名接收 InstallOptions，文档网页的旧签名没有优先于实际包。
- [2.9.3 VersionCommand.cpp](https://github.com/microsoft/WSL/blob/2.9.3/src/windows/wslc/commands/VersionCommand.cpp) 表明旧文本由可执行文件名与 WSL 包版本组成；[2.9.9 原生 SDK 实现](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/wslcsdk.cpp) 表明 GetVersion 返回运行时服务版本。
- SDK 2.9.9 的 Container / Session / WslcService 公开 API 未提供原生 Restart 或全局 Events 流。本任务不反射内部 COM，也不把后端实现或版本号当作已公开功能。

## 环境

| 项目 | 值 |
| --- | --- |
| Windows | Windows 11 25H2，10.0.26200.9445 |
| `wslc version --format json` | `{"Client":{"Version":"2.9.10.0"}}` |
| `wslc version` | `wslc 2.9.10.0` |
| SDK `GetMissingComponents()` | 空集合（主代理通过独立 SDK 2.9.9 程序只读核对） |
| SDK `GetVersion()` | `2.9.10`（同上，无 session 创建） |
| SDK 包版本 | 2.9.9 |
| .NET SDK | 10.0.401，global.json 选择 10.0.300 / latestFeature |

## 构建与自动化验证

```powershell
dotnet restore ExWSLC.sln
dotnet build ExWSLC.sln --no-restore
dotnet test ExWSLC.sln --no-build
git diff --check
```

结果：restore 通过；构建 0 警告、0 错误；完整测试 **217 / 217** 通过，0 跳过；diff 空白检查通过。主代理完成最终 XAML 调整后再次执行构建和全套测试，结果相同。

新增回归覆盖：JSON / 旧文本版本、坏格式及任意文本、不可启动的 CLI 与可启动但探测失败的 CLI、局部 help 失败 / 超时、帮助形状和精确 token、各构建选项独立判断、健康配置完整组、网络父命令依赖、禁止版本阈值猜测 restart / events、SDK 不可加载、服务未响应但保留缺失组件、SDK 升级需求、缓存 / 显式重检、并发等待期间取消、安装前重新检测、失败 / 取消失效缓存、版本属性通知、中英资源、语言通知不探测、安装后自动刷新恢复、关闭期间取消和初始化失败恢复。01 的列表、Inspect、网络、挂载和既有 XAML 测试仍包含在全套测试中。

## 本机只读帮助核对

以下命令本次均成功，帮助使用中文标题与不变的命令 / 选项 token：

```powershell
wslc --help
wslc container --help
wslc container create --help
wslc network --help
wslc network connect --help
wslc image build --help
wslc system --help
```

| 功能项 | 本机帮助证据 |
| --- | --- |
| HealthChecks | create 的六个健康检查选项均存在 |
| NetworkConnect / NetworkDisconnect | network 包含 connect / disconnect |
| NetworkConnectIp / NetworkConnectAlias | connect 包含 `--ip` / `--network-alias` |
| ContainerCopy | container 包含 cp |
| BuildSecret / BuildOutput / BuildProgress / BuildPull | build 各自包含 `--secret` / `--output` / `--progress` / `--pull` |
| CreateMount / CreatePullPolicy / CreateStopTimeout / CreateStopSignal / CreateIp / CreateNetworkAlias | create 各自包含对应选项 |
| SystemInfo | system 包含 info |
| NativeRestart | container 帮助中未提供 restart |
| Events | 根和 system 帮助均未提供 events |

这些证据只证明公开 CLI 入口是否列出，不证明实际变更、构建或流处理已经成功。后续功能必须根据快照的 Support 和 ReasonKey 决定可用性，并处理真实操作失败。

## 主代理独立复查

主代理复查了版本、能力、安装与 UI 调用链，另由独立子代理复查稳定的检测和 SDK 代码，未留下阻断问题。以下均使用本次构建产物。

### 真实检测及库存

临时 C# 验证宿主复用 `App.ConfigureServices` 的 DI 注册，解析实际 `IRuntimeCapabilityService`。只读进程计数包装器只允许版本 / 帮助命令通过，实际执行仍交给 `WslcProcessRunner`；SDK 使用真实 `WslcSdkService`。

| 检查 | 结果 |
| --- | --- |
| CLI / 服务 / SDK 包版本 | `2.9.10.0` / `2.9.10` / `2.9.9` |
| 三类可用状态 | 均为 Supported |
| 缺失组件 | 空集合，安装按钮不可用 |
| 功能快照 | 19 项中 17 项 Supported；NativeRestart 和 Events 为 Unsupported，与上述帮助证据一致 |
| 首次 DetectAsync | 8 条 CLI 命令：1 条 JSON 版本、7 条帮助 |
| 第二次 DetectAsync | 总命令数仍为 8；返回同一个快照实例 |

同一宿主再次使用 `WslcContainerRuntime(new WslcProcessRunner())` 读取库存，容器 / 镜像 / 网络 / 卷 / stats 分别为 **3 / 1 / 3 / 0 / 3**，与逐项 CLI 对照全部一致。验证仅输出版本、能力和数量，没有保存库存原文。

### 发布产物

```powershell
dotnet publish src/ExWSLC.csproj -c Release -r win-x64 --self-contained true -o artifacts/wslc-02-publish/win-x64
dotnet publish src/ExWSLC.csproj -c Release -r win-arm64 --self-contained true -o artifacts/wslc-02-publish/win-arm64
```

两个发布命令均退出 0。分别用 `Get-FileHash` 比较发布目录中的 `wslcsdk.dll` 与 NuGet 2.9.9 的 `runtimes/win-<架构>/native/wslcsdk.dll`：

| RID | 原生 DLL 字节数 | SHA256 与相同架构的包文件一致 |
| --- | ---: | --- |
| win-x64 | 4,929,888 | 是 |
| win-arm64 | 5,627,744 | 是 |

发布产物和临时验证宿主保留在被忽略的 `artifacts/` 中，不随源码提交。

### 设置页渲染

通过临时 STA / WPF 宿主加载实际应用资源和 `SettingsPage`，用 `Measure`、`Arrange`、`RenderTargetBitmap` 生成视口截图。数据来自 `DesignWorkspaceFactory`；未知状态另用空能力快照。语言检查替换应用字典并发送现有 `LanguageChangedMessage`，不读写用户偏好。

- 修改前后均生成中 / 英文 × Light / Dark / System 的 1100 × 900 视口图。三种版本、可用图标及状态、重新检测入口与环境说明显示正确。
- 未知状态另外生成同样六种组合的 900 × 900 图。版本未知与 SDK 包版本分开显示，中性问号与本地化说明正常。
- 本机 System 解析为浅色，各 System 图与相应 Light 图的文件哈希一致。没有模拟操作系统主题在运行中发生变化。
- 渲染检查发现窄视口的英文维护说明被截断，已仅补 `TextWrapping="Wrap"` 并重新生成截图，说明现可完整换行。页面超出视口的内容保留垂直滚动。

| 示例 | 修改前 | 修改后 |
| --- | --- | --- |
| 中文浅色 | [before](images/wslc-02-before/settings-zh-CN-light-1100.png) | [after](images/wslc-02-after/settings-zh-CN-light-1100.png) |
| 英文深色 | [before](images/wslc-02-before/settings-en-US-dark-1100.png) | [after](images/wslc-02-after/settings-en-US-dark-1100.png) |

未知状态示例：[中文深色](images/wslc-02-unknown/settings-zh-CN-dark-900.png)、[英文浅色](images/wslc-02-unknown/settings-en-US-light-900.png)。这些图属于实际 WPF 控件的离屏渲染，不等同于完整人工点击、滚动和系统主题切换验收；下拉选项保留设计数据的选择值。

## 边界

本机为 WSL / WSLC 2.9.10.0，未在真实 2.9.3 或 2.9.11 安装上执行；旧版、异常和缺失组件由源码与模拟边界覆盖。没有实机安装或升级 WSL，没有创建、停止、删除容器、网络、卷或 SDK session。安装取消验证的是令牌传递和状态处理；底层原生安装可能继续当前步骤，不承诺立即停止或回滚。ARM64 的原生运行仍需 ARM64 Windows 验收。
