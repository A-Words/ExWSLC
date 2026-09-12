# 任务 03：容器健康检查

## 工作树与基线

- 工作树：`C:\Users\A_Words\.codex\worktrees\1f9e\ExWSLC`
- 起始 HEAD：`72d0943784f994caf5a0b96c43e45994a821226b`，起始工作树干净、detached HEAD。
- 分支：`feat/container-health`；确认 `f85b111`（任务 02）及 `d9e321e`（任务 01）均为祖先，没有合并其他分支。
- 日期：2026-09-12。提交 SHA 见交付消息及本文件所在提交。

## 环境与依据

| 层次 | 本次核实 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL | 2.9.10.0，内核 6.18.40.1-1 |
| CLI | `wslc version` / `wslc version --format json`：2.9.10.0 |
| 服务 | 实机 `RuntimeCapabilityService` 调用 SDK `GetVersion`：2.9.10；设置页也显示该值 |
| SDK | 项目及已还原 NuGet 包 `Microsoft.WSL.Containers` 2.9.9，未升级 |
| .NET | SDK 10.0.401，测试运行时 10.0.12 |
| 公开发行 | GitHub releases 查询：稳定版 2.7.14，最新预览版 2.9.11；本机 2.9.10 为预览版 |
| 上游主线 | 查询时 master `eaa69e766cf375d96053207a4ba8858f54ea1536`；没有用主线或版本号推断本机支持 |

核对 `wslc container create --help`、`container run --help`、`container ls --help`、`container inspect --help`。本机 create/run 均列出 `--health-cmd`、`--health-interval`、`--health-timeout`、`--health-start-period`、`--health-retries`、`--no-healthcheck`。

查阅 [官方 C# API](https://wsl.dev/api-reference/csharp/)、[SDK 2.9.9 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 和本机包的 docs/include；该 SDK 未公开健康配置入口，因此继续走 CLI，不增加 SDK 调用。

实现与边界依据：[健康检查 PR 41012](https://github.com/microsoft/WSL/pull/41012)、[列表字段 PR 41375](https://github.com/microsoft/WSL/pull/41375)、[2.9.10 Inspect schema](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/inc/wslc_schema.h)、[时间解析](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/common/timestamp.cpp)、[CLI 参数验证](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/arguments/ArgumentValidation.cpp)、[零值转发语义](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslcsession/WSLCContainer.cpp)。

## 最终行为与共享接口

- `ContainerCreateSpec` 增加 `HealthMode` 与五个 nullable 字符串字段。默认继承不附加任何健康参数；禁用只发送 `--no-healthcheck`；自定义仅发送有值字段。空白 UI 输入转为 null，显式 `0` 保留。WSLC 对零值使用镜像或运行时默认值，UI 已说明，不宣称零值可以清除镜像宽限期。
- 自定义可仅覆盖时长或重试次数并继承镜像命令；至少填写一个字段。互斥模式、空自定义、非法时间、负数、重试上溢均在创建前给出本地化提示。时间支持组合和小数单位 ns/us/µs/μs/ms/s/m/h，非负且限制为 Int64 纳秒；输入限 128 字符。重试范围 0–2147483647。
- 复用 `RuntimeFeature.HealthChecks` 完整参数组。UI 使用工作区能力快照，运行时通过注入的 `IRuntimeCapabilityService.DetectAsync` 再检查缓存；Unknown/Unsupported 均禁止覆盖，继承仍可运行。没有能力服务时也不发送覆盖。仍使用 `IProcessRunner` 和 `ProcessStartInfo.ArgumentList`，命令作为单独参数传递。
- `IContainerRuntime` 方法签名没有变化。`WslcContainerRuntime` 构造函数新增可选能力服务依赖，现有库存测试调用兼容。设计时模型补充健康示例。
- `ContainerSummary.HealthStatus` 与 `ContainerInspectDetails.Health` 区分五种状态。列表缺失/null/未知字段为未知；running 容器的空字符串或显式 none 显示未配置。停止/创建状态的空健康字段不能证明未配置，显示未知。Inspect 读取 `State.Health`，缺失时仅凭明确的 `Config.Healthcheck=null` 或 `Test=["NONE"]` 判定未配置。不会从 Running 推断健康。
- 健康页追加为索引 6，保留原有 0–5 页签。复用配置/原始 Inspect 的请求、取消、缓存和容器身份检查。刷新清空共享 Inspect 快照并按需重载；失败时不保留旧健康记录。迟到结果、容器切换和不同 ID 的响应不会覆盖当前选择。
- 读取 `Status`、`FailingStreak`、`Log[].Start/End/ExitCode/Output`；最新记录在前，最多 10 条，每条最多 4096 字符加省略标记。缺失计数和退出码保持未知；原始 Inspect 仍保留完整 JSON。
- 没有自动恢复、通知推送、重启策略或应用设置持久化扩展。

## 验证命令与实机资源

```powershell
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
git diff --cached --check

$env:EXWSLC_HEALTH_LIVE = '1'
$env:EXWSLC_HEALTH_IMAGE = 'docker.1ms.run/nginx:latest'
$env:EXWSLC_HEALTH_UI_HOLD_SECONDS = '420' # 可选，留时间检查 UI，最大 600 秒
dotnet test ExWSLC.sln --no-build --filter-class ExWSLC.Tests.ContainerHealthLiveTests --show-live-output on
```

实机入口只在显式 opt-in 时运行，复用已有基础镜像，不拉取或修改它。创建临时 Dockerfile，以 `HEALTHCHECK --interval=1s --timeout=1s --retries=1 CMD echo inherited-ready` 构建独立的 `exwslc-03-health-2063dad7f6:test`，再创建：

| 测试容器 | 覆盖参数 | 观测 |
| --- | --- | --- |
| `exwslc-03-inherit-2063dad7f6` | 无 | Inspect/list 均 Healthy；退出码 0；输出 inherited-ready |
| `exwslc-03-disabled-2063dad7f6` | `--no-healthcheck` | Inspect/list 均 NotConfigured；Health=null，配置 Test=[NONE] |
| `exwslc-03-custom-2063dad7f6` | `--health-cmd "echo intentional-health-failure; exit 1" --health-interval 1s --health-timeout 1s --health-start-period 0s --health-retries 1` | Inspect/list 均 Unhealthy；退出码 1；失败次数增长；输出 intentional-health-failure |

实际创建前缀为 `wslc run --detach`，传入独立 `--name`，容器主命令为 `/bin/sh -lc "sleep 600"`。读取使用 `container inspect <name>` 与 `container list --all --no-trunc --format json`。finally 使用各测试容器的 `container remove --force <name>`、`image remove <test-tag>`，删除本次临时目录；清理使用独立取消令牌并汇总失败。没有 prune、shutdown、原生设置更改或用户容器变更。

## UI 验收

实际运行工作树构建的 WPF 应用，通过 Windows computer-use 操作；窗口 1100×800。检查中文系统主题（当前解析为浅色）、中文显式浅色、英文深色；创建表单可滚动到所有字段，健康页可滚动查看历史记录，输出只读且支持选择。切换语言后状态和新文案跟随更新。检查时发现英文状态列截断，已加宽为 140。结束后恢复原有 zh-CN/System/5 秒偏好。

截图保存在 `docs/validation/screenshots/wslc-03/`：

- `before-create-zh-light.jpg`：基线创建页面（系统浅色）。
- `after-create-zh-light.jpg`、`after-create-en-dark.jpg`：自定义表单。
- `after-health-zh-light.jpg`、`after-health-en-dark.jpg`：真实隔离容器健康记录。
- `after-list-zh-system.jpg`、`after-list-en-system.jpg`：调整状态列后的列表，英文完整显示。

实机 opt-in 测试通过 1/1（含 420 秒界面观察窗口，共 7 分 14 秒）。结束后 `wslc container list --all --format json --filter name=exwslc-03-` 与 `wslc image list --format json --filter reference=exwslc-03-health-2063dad7f6:test` 均为空，临时目录不存在，确认无本次资源残留。

最终补充实机复验使用唯一后缀 `303123d872`，不设置观察等待，19.94 秒通过 1/1。再次验证继承/禁用/自定义，并停止继承容器，确认其列表健康字段为空时显示 Unknown，不能误判 NotConfigured。该轮三个容器、测试镜像和临时目录均已清理；最终两个列表截图来自这一语义修正后的构建。

## 最终自动验证结果

- `dotnet build ExWSLC.sln`：通过，0 警告、0 错误。
- `dotnet test ExWSLC.sln`：277 项，276 通过、0 失败、1 默认跳过（显式 opt-in 实机测试）；3.65 秒。随后 `--no-build` 完整复验同样通过，3.40 秒。
- `git diff --check` 与提交前 `git diff --cached --check`：通过。
- 回归覆盖五种状态、空/缺失/畸形字段、记录和输出上限、三种模式与参数边界、Unsupported/Unknown 阻断、取消后迟到结果、选择身份不一致、共享缓存和刷新失败清空；实际加载并布局健康 DataTemplate，双语资源检查也随全量测试通过。
- 收尾一次全量运行曾遇到自动刷新测试超时，以及无 UI dispatcher 的日志跟随回调竞态。新增健康测试夹具现于选择时抑制日志跟随，再恢复正常检查路径，避免与本任务无关的 async-void 日志请求；最终完整构建与测试通过。没有更改生产日志跟随实现。

## 验收边界

- 旧版 CLI 的 Unsupported/Unknown、畸形与缺失字段、数值边界、取消和容器切换由回归测试验证；没有降级本机 WSL 做旧版实机测试。
- 检查了两种语言及三种主题设置，但没有穷举所有语言×主题组合、DPI、窗口尺寸或屏幕阅读器。
- 未实机注入创建过程中强制终止测试进程的故障；正常失败/取消通过 finally 清理，进程被强杀无法保证执行 finally。
- 本任务只增加健康检查配置和可观察状态，不保证任意镜像应用自身的健康命令有效。
