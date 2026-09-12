# 任务 04：网络连接、断开与 IP 配置

## 工作树与基线

- 工作树：`C:\Users\A_Words\.codex\worktrees\1f9e\ExWSLC`。
- 起始 HEAD：`97fdaec341b00326882f238e5370f14b38d73ef1`（任务 03），起始分支 `feat/container-health`，工作树干净。
- 新分支：`feat/network-operations`。确认 `f85b111` 与 `d9e321e` 均为祖先，没有合并其他分支。
- 日期：2026-09-12。交付提交 SHA 见本文件所在提交与交付消息。

## 运行环境与来源

| 层次 | 本次核实 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL | 2.9.10.0，内核 6.18.40.1-1 |
| CLI | `wslc version --format json`：2.9.10.0 |
| 服务 | 实机 `RuntimeCapabilityService` 调用 SDK `GetVersion`：2.9.10；设置页同值 |
| SDK | 实际项目及已还原包 `Microsoft.WSL.Containers` 2.9.9，未升级 |
| .NET | SDK 10.0.401 |
| 公开发行 | GitHub releases 查询：稳定版 2.7.14、最新预览版 2.9.11；本机 2.9.10 为预览版 |
| 上游主线 | 查询时 master `eaa69e766cf375d96053207a4ba8858f54ea1536` |

逐项读取本机 `network connect --help`、`network disconnect --help`、`network create --help`；连接支持 `--ip`、可重复 `--network-alias`、可重复 `--driver-opt`。断开只有网络名与容器 ID，没有 force 参数。创建支持单个 `--subnet`、`--gateway`、`--ip-range`，保留可重复 `--opt`、`--label`。

依据：[连接/断开 PR 41011](https://github.com/microsoft/WSL/pull/41011)、[连接参数 PR 41070](https://github.com/microsoft/WSL/pull/41070)、[IP 范围 PR 41138](https://github.com/microsoft/WSL/pull/41138)、[2.9.10 NetworkConnectCommand](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/commands/NetworkConnectCommand.cpp)、[NetworkCreateCommand](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/commands/NetworkCreateCommand.cpp)、[NetworkService](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/services/NetworkService.cpp)。

查阅 [官方 C# 参考](https://wsl.dev/api-reference/csharp/)、[SDK 2.9.9 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 及本机 NuGet include；公开 SDK 提供容器 networking mode 设置，未提供这些网络管理操作的 C# 入口。上游 CLI 使用内部 COM 不等于公开 SDK 可用，因此新增操作继续通过 CLI，不更改 SDK 集成。

## 实现与共享接口

- `IContainerRuntime` 增加 `ConnectNetworkAsync(NetworkConnectionSpec)` 与 `DisconnectNetworkAsync(NetworkDisconnectionSpec)`。请求固定容器 ID、网络名称；可选 IPv4、别名列表和端点驱动选项均作为独立 `ArgumentList` 元素传入。没有 shell 拼接，没有虚构 disconnect force。
- `NetworkCreateSpec` 增加 nullable `Subnet`、`Gateway`、`IpRange`，留空不传参。IPv4 使用完整点分十进制；创建支持 IPv4/IPv6 网络 CIDR 的语法和包含关系验证，要求不含主机位，网关/范围必须属于子网。IPv6 运行时是否接受仍以实际返回为准。
- 创建驱动参数仍映射 `--opt`，连接端点参数映射 `--driver-opt`，标签仍为 `--label`。端点选项使用每行 key=value，拒绝重复键；位置参数禁止以选项前缀开头及控制字符。
- 能力检测复用原有缓存，只增加网络创建帮助与四项逐项能力；UI 与 runtime 双重检查 Supported。默认网络创建不需要新增能力，兼容旧调用。
- 容器网络详情新增连接表单与每个已连接网络的断开入口；复用工作区网络清单，过滤已连接网络及 host/none 内建模式。无网络、无详情、非法地址、旧版能力、不支持的容器网络模式均有反馈；地址占用和驱动限制保留运行时错误。
- 断开先锁定操作与捕获目标，再确认；确认后即使切换容器，仍操作原目标。两种操作共用锁和现有任务/取消机制。自动库存刷新不禁用输入字段，但禁止提交；进行连接/断开时锁定表单。
- 成功后失效目标网络与 Inspect 缓存，刷新容器/网络清单，当前仍是目标网络页时等待详情重载。旧快照保留但明确标为过期，刷新失败不能显示成新状态；重试成功后清除标记。切换后的迟到结果不能覆盖当前容器，Inspect 显式 ID 不符也拒绝。
- 普通操作失败保留有效详情并显示错误。取消可能与服务端提交竞态，因此失效目标缓存并刷新确认，不声称回滚成功。连接/断开状态消息包含目标容器与网络名。
- 设计时 runtime、能力与网络创建示例已同步。保留原有连接详情及端口暴露显示，不加入网络编排、防火墙或应用设置持久化扩展。

## 自动与实机验证

最终 `dotnet build ExWSLC.sln` 通过，0 警告、0 错误；`dotnet test ExWSLC.sln` 共 324 项，322 通过、0 失败、2 默认跳过（任务 03/04 的 opt-in 实机入口），3.83 秒。`git diff --check` 与提交前 `git diff --cached --check` 通过。

```powershell
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
git diff --cached --check

$env:EXWSLC_NETWORK_LIVE = '1'
$env:EXWSLC_NETWORK_IMAGE = 'docker.1ms.run/nginx:latest'
$env:EXWSLC_NETWORK_UI_HOLD_SECONDS = '360' # 可选；最多 600 秒，用于只读 UI 验收
dotnet test ExWSLC.sln --no-build --filter-class ExWSLC.Tests.NetworkOperationsLiveTests --show-live-output on --report-xunit --report-xunit-filename network-live.xml
```

显式 opt-in 测试复用已有镜像，不拉取镜像。创建前只读检查现有网络 IPAM，选择不重叠的 `10.203.x.0/24`；所有写操作仅作用于任务编号与唯一后缀命名的测试资源。

实机通过 1/1，含 360 秒截图等待共 6 分 06.56 秒。资源后缀 `3ceebc8d1b`：

| 测试资源/操作 | 观测结果 |
| --- | --- |
| `exwslc-04-target-3ceebc8d1b` | 子网 `10.203.201.0/24`、网关 `.1`、范围 `.128/25`；Inspect 确认 MTU 驱动选项 1400 与标签 exwslc.task=04 |
| `exwslc-04-origin-3ceebc8d1b` | 两个测试容器的原始独立网络 |
| `exwslc-04-client-3ceebc8d1b` | 连接目标后 `10.203.201.140/24`，别名 exwslc-api、exwslc-secondary，原始网络仍连接 |
| 端点驱动选项 | `com.docker.network.endpoint.sysctls=net.ipv4.conf.IFNAME.log_martians=1` 命令成功；只确认运行时接受，未在容器内读取 sysctl |
| `exwslc-04-conflict-3ceebc8d1b` | 申请同一 IPv4，实际返回 `Address already in use` / `E_FAIL` |
| 断开客户端目标网络 | Inspect 确认目标附件消失、origin 附件保留 |

finally 使用独立两分钟取消令牌逐个 `container remove --force`，再逐个 `network remove`，汇总清理错误。最终 `container list --all --format json --filter name=exwslc-04-` 和 `network list --format json --filter name=exwslc-04-` 均为空。没有 prune、shutdown、宿主机设置、用户网络/容器修改。

首次实机入口在资源创建前发现 host/none 的 `IPAM.Config` 为 null，调整只读选段逻辑后重跑通过。单元测试涵盖位置参数/重复参数边界、CIDR/地址关系、独立能力门控、无网络/已连接/旧版、确认取消与切换目标、失败保留、成功后刷新失败、取消后重检和请求身份一致性。XAML 测试实际加载并布局网络模板及创建对话框。

## UI 与边界

通过 Windows computer-use 实际运行 WPF 应用，检查 1100×800 窗口的两种语言和系统/浅色/深色主题设置。连接高级字段和创建高级字段均可滚动访问；中英文状态提示、IPv4、别名与断开入口可见。系统主题在本次过程中从浅色切换为深色，前后截图按实际设置标注。

截图目录：`docs/validation/screenshots/wslc-04/`：

- `before-network-zh-system.jpg`：基线网络详情，系统浅色。
- `after-connect-zh-system.jpg`、`after-attachment-zh-system.jpg`：中文系统深色，隔离测试资源与实际地址/别名。
- `after-connect-en-dark.jpg`：最终英文连接表单，确认自动刷新期间输入仍可编辑；提交单独受忙碌状态保护。
- `after-create-en-dark.jpg`、`after-create-zh-light.jpg`：网络创建 IP 配置。

高级字段展开后在 1100×800 窗口需滚动查看下方内容，已检查可达。创建对话框提交按钮订阅命令可执行状态，随名称及工作区忙碌状态更新。验收后恢复 zh-CN/System/5 秒偏好。

- 没有降级本机 WSL 验证旧版实机；Unsupported/Unknown 由回归测试覆盖。
- 未穷举语言×主题、DPI、窗口尺寸及屏幕阅读器。
- IPv6 IPAM 仅验证语法/包含关系，未做 IPv6 实机网络；未验证全部网络驱动及端点选项的实际内核效果。
- 没有强杀进程清理实验；正常失败/取消执行 finally，进程强杀无法保证 finally 运行。
- CLI 网络列表不提供子网/网关时仍显示原有占位符，实际 IPAM 验收读取 network inspect；不将创建参数假装为运行时清单。
