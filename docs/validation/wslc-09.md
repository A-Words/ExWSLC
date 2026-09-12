# 任务 09：容器访问 Windows 本地服务诊断验证

验证日期：2026-09-12（Asia/Taipei）。实现、回归测试、隔离容器实测与 WPF 页面渲染已完成；完整桌面交互仍需人工补验。

## 基线与范围

- 工作树：`C:\Users\A_Words\.codex\worktrees\73ef\ExWSLC`。
- 分支：`feat/runtime-diagnostics-sessions`。
- 起始 HEAD：`96ba4b7dcf1bbac49d328a94ae2e8f181df13fad`（任务 08）；工作区起始干净，已确认包含 `f85b111`、`d9e321e`，满足 01、02、08 前置条件。
- 在设置页诊断区域增加原生配置读取、运行中容器选择、明确端口输入、主动 DNS／TCP 检查、取消、脱敏复制和分状态下一步。未改动容器主详情页。
- 共享接口：`IContainerRuntime.GetHostLoopbackConfigurationAsync`、`ProbeHostLoopbackAsync`，新增 `RuntimeFeature.HostLoopback` 与可注入的 `IHostLoopbackSettingsReader`；生产、设计实现与测试同步更新。
- 复用 `CapabilitySupport`、能力缓存、工作区任务／取消与剪贴板接口。新增 YamlDotNet 16.3.0 解析原生配置，Microsoft.WSL.Containers 仍为 2.9.9。
- 不写入应用设置或原生 YAML，不安装容器工具，不修改 DNS／防火墙，不启动已有用户容器，不推送或合并分支。

## 环境与官方依据

| 项目 | 本机值 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL / CLI | 2.9.10.0 |
| 会话管理服务 | 2.9.10 |
| C# SDK 包 | Microsoft.WSL.Containers 2.9.9 |
| WSL 内核 | 6.18.40.1-1 |
| .NET SDK / 运行时 | 10.0.401 / 10.0.12 |

公开发行核对结果为稳定版 [2.7.14](https://github.com/microsoft/WSL/releases/tag/2.7.14)、预览版 [2.9.11](https://github.com/microsoft/WSL/releases/tag/2.9.11)，本机仍为 2.9.10.0。未自动升级，也未用公开最新版本替代本机证据。

已查阅 [回环支持 PR #41264](https://github.com/microsoft/WSL/pull/41264)、[2.9.10 原生设置读取](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/common/WSLCUserSettings.cpp)、[2.9.10 网络实现](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/common/ConsommeNetworking.cpp)、[官方 C# API](https://wsl.dev/api-reference/csharp/) 及 [2.9.9 SDK 会话路径](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/service/exe/WslCoreVm.cpp)。原生路径通过虚拟地址将域名映射到 Windows 回环；SDK 创建会话的版本行为不同，不能假定自动启用。没有新增 SDK 会话或更换 SDK 包。

本机当前用户原生文件存在，但没有显式 `session.hostLoopback`，因此页面显示默认候选域名及会话能力未验证。读取配置成功不证明已有会话采用此配置；全局 `RuntimeFeature.HostLoopback` 保持 Unknown。`none`、自定义值与默认值按版本源码解释；不支持的 DNS 值或歧义结构返回未知。

## 实际命令与检查方式

开始实现前执行了版本和相关帮助核对，并检查原生文件的目标键；没有调用会启动编辑器的裸 `wslc settings`：

```powershell
wslc version
wslc version --format json
wslc settings --help
wslc exec --help
wslc container inspect --help
wslc system info --format json
wsl --version
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
```

实际探测先执行 `wslc container inspect <selected-id> --format json`，验证规范 ID 与运行状态，再执行 `wslc exec <canonical-id> /bin/sh -c <fixed-script> exwslc-host-probe <host> <port> <fixed-tcp-script>`。所有参数经 `ArgumentList` 分别传入，用户目标不会拼接进程序文本。

优先使用容器已有 Python 3（隔离模式）；否则使用已有 bash、getent、timeout。仅解析 IPv4、选择第一个地址、最多建立一个 TCP 连接，连接后立即关闭，不发送服务请求或读取响应正文。整体期限 15 秒，容器内期限 8 秒，TCP 连接期限 3 秒；Bash DNS 子进程另有 3 秒限制。取消终止本地命令，容器内探测仍受自身期限限制。

实测入口显式启用，使用本机已有测试镜像：

```powershell
$env:EXWSLC_LIVE_HOST_LOOPBACK = '1'
$env:EXWSLC_HOST_LOOPBACK_TEST_IMAGE = 'docker.1ms.run/nginx:latest'
dotnet test ExWSLC.sln --filter-method '*SelectedTestContainer*'
Remove-Item Env:EXWSLC_LIVE_HOST_LOOPBACK
Remove-Item Env:EXWSLC_HOST_LOOPBACK_TEST_IMAGE
```

测试创建 `exwslc-09-<GUID>` 和对应 `-tools` 容器，Windows 测试服务只绑定 `127.0.0.1` 的一个自动分配端口。真实验证依次通过：

1. DNS 成功、指定端口连接成功；服务端读到 EOF，确认没有发送应用数据。
2. 关闭同一测试监听器，DNS 仍成功、TCP 失败。
3. 只在隔离容器中设置无工具 PATH，返回缺少诊断工具。
4. 停止该测试容器后返回未运行；删除后返回不可用。

每次测试使用 finally 与独立清理期限移除自建容器、关闭监听器；成功后复核没有 `exwslc-09-*` 残留，用户原有 3 个已停止容器及 2 个镜像的清单保持不变。未执行全局 prune 或 shutdown。

最初尝试拉取 `python:3.13-alpine` 及其镜像站版本均失败（非零退出），当时尚未创建容器或监听器。随后使用已有 nginx 镜像成功验证 Bash 路径，无需拉取镜像或安装工具。Python 路径尚未完成真实容器验收。

## 回归测试结果

- 最终 build：0 警告、0 错误。
- 常规测试：302 项，300 成功、2 项 opt-in 实测默认跳过、0 失败。
- 任务 09 实测单独启用：1 成功；加强容器内超时处理后再次通过。
- `git diff --check` 通过；提交前执行 `git diff --cached --check`。

| 行为 | 验证证据 |
| --- | --- |
| 默认、自定义、none、null、大小写、注释 | YAML 解析回归通过；本机默认候选实测通过 |
| 未知能力、禁用配置、执行前配置变化 | 回归验证停止探测或允许 Unknown 下的实际检查，不猜测会话支持 |
| 非法端口、容器 ID、域名、shell 注入输入 | 在 IO 前拒绝；合法目标保持独立参数，固定脚本不变 |
| 重复键、合并键、别名、循环、过深／过大 YAML | 返回未知，不回显原始配置或凭据 |
| DNS 失败、DNS 成功但 TCP 失败 | 标记映射回归；后者另有真实本地服务验收 |
| 容器停止、删除、exec 期间状态变化 | 回归覆盖；停止／删除另有真实隔离资源验收 |
| 工具缺失 | 回归与隔离容器实测通过 |
| 超时、取消、操作恢复 | 标记／异常映射及 ViewModel 回归通过；未人为制造真实卡死或桌面取消流程 |
| 在途请求期间修改表单 | 请求与结果仍绑定原容器、域名和端口，回归通过 |
| 复制、错误脱敏、语言切换 | 自定义域名及容器名称不出现在摘要，原始 stdout／stderr／异常不进入任务详情；切换语言不重查 |

连接成功只证明解析端点接受 TCP，不证明应用协议、认证或既有会话配置正确。下一步文案按状态提示检查端口、服务监听、原生配置或已有容器工具，没有建议无条件监听所有网卡。结果仅保留在当前 ViewModel 内存中；实测记录另写入被 Git 忽略的 artifacts，用于生成可复现截图。

## 截图与未验证项

截图目录 `docs/validation/screenshots/wslc-09/` 共 16 张：

- `before-{en-US,zh-CN}-{Light,Dark,System}.png`：任务 08 基线的实际 WPF 页面。
- `after-{en-US,zh-CN}-{Light,Dark,System}.png`：850×700 页面，显示真实测试连接结果的快照。
- `narrow-{en-US,zh-CN}-Light.png` 及 `-bottom.png`：550×700 视口与向下滚动结果。

已检查新增卡片的两种语言、三种主题和窄视口换行；本次 System 模式解析为深色。[之前](screenshots/wslc-09/before-zh-CN-Light.png)、[之后](screenshots/wslc-09/after-zh-CN-Light.png)、[英文深色](screenshots/wslc-09/after-en-US-Dark.png)、[窄视口](screenshots/wslc-09/narrow-zh-CN-Light.png)。已有“运行时维护”卡在 550 像素英文视口下文字挤压，本次未修改该区域。

完成上述 opt-in 实测后，可从仓库根目录复现渲染：

```powershell
$env:EXWSLC_LIVE_DIAGNOSTICS = '1'
dotnet run --project docs/validation/diagnostics-render/DiagnosticsRender.csproj -- docs/validation/screenshots/wslc-09 artifacts/wslc-09-live-connected.json
Remove-Item Env:EXWSLC_LIVE_DIAGNOSTICS
```

截图使用实际 WPF SettingsPage 与 RenderTargetBitmap；测试容器已清理，选择框使用明确标注的测试快照名称。渲染入口读取系统信息与原生配置，不启动网络探测，也不写用户偏好。这些图片不是完整桌面窗口截图。

当前会话的原生桌面控制接口不可用。仍需人工验证完整主窗口的鼠标／键盘导航、真实剪贴板、操作系统主题实时切换、高 DPI 和桌面取消。真实自定义／禁用原生配置、SDK 创建会话、旧 CLI、IPv6、Python 容器路径未实测；没有为补验而修改用户原生配置或升级 WSL。
