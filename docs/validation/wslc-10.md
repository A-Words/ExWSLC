# 任务 10：空闲感知刷新调度验证

验证日期：2026-09-12（Asia/Taipei）。实现、可控调度回归与实际 WPF 页面渲染已完成；未测量真实 VM 回收或资源节省，完整桌面交互仍需人工补验。

## 基线与改动

- 工作树：`C:\Users\A_Words\.codex\worktrees\73ef\ExWSLC`。
- 分支：`feat/runtime-diagnostics-sessions`。
- 起始 HEAD：`58edaff772dfcde08927616d20277a48a42f210f`，起始工作区干净。
- 用祖先检查确认包含 `f85b111`、任务 08 的 `96ba4b7` 与任务 09 的 `58edaff`，在设置组 C 当前 HEAD 上继续，没有合并其他分支。
- 新偏好 `PauseAutoRefreshWhenMinimized` 默认 false；旧 JSON 缺少此字段时同样为 false。只保存到 ExWSLC 的应用偏好文件。
- 设置页提供中英文说明、勾选项与当前调度状态；点击保存后生效。
- 共享改动限于 AppSettings、SettingsViewModel、RuntimeWorkspace、AutoRefreshService 与 MainWindow 状态订阅。没有新增 IContainerRuntime 或 SDK 接口，没有更改能力检测与缓存。

## 版本与上游证据

| 项目 | 当前核对值 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL / CLI | 2.9.10.0 |
| 会话管理服务 | 2.9.10，来自 system info 的 Server.SessionManagerVersion |
| C# SDK 包 | Microsoft.WSL.Containers 2.9.9，未变更 |
| 内核 | 6.18.40.1-1 |
| .NET SDK / 测试运行时 | 10.0.401 / 10.0.12 |

公开发行查询仍为稳定版 [2.7.14](https://github.com/microsoft/WSL/releases/tag/2.7.14)、预览版 [2.9.11](https://github.com/microsoft/WSL/releases/tag/2.9.11)，本机为 2.9.10.0。已重新查阅 [官方 C# API](https://wsl.dev/api-reference/csharp/)，应用没有增加 SDK 会话操作，版本来源仍按任务 02 分开报告。

已检查 [空闲回收 PR #41077](https://github.com/microsoft/WSL/pull/41077)、[2.9.10 WSLCSession](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslcsession/WSLCSession.cpp)、[2.9.10 WSLCSessionRuntime](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslcsession/WSLCSessionRuntime.cpp)、[2.9.10 ContainerTasks](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/tasks/ContainerTasks.cpp) 与主线同名 Runtime 文件。

实现前检查得到的证据链：

1. 本项目 `AutoRefreshService` 默认在上一轮完成后等待 5 秒，再调用 `RuntimeWorkspace.RefreshAllAsync`；每轮并发发出容器、镜像、网络、卷清单和统计共 5 个 CLI 查询。实际周期包含命令耗时，不能直接换算为固定每分钟调用数。
2. `WslcContainerRuntime` 的容器清单使用 `container list --all --no-trunc --format json`；统计使用 `stats --all --no-trunc --format json`。统计是快照，没有另一套后台统计循环。
3. 上游 2.9.10 的 `WSLCSession::ListContainers` 获取 `AcquireLease`。`VmLease` 在进入操作前调用 `AddActivity`，取消待触发的空闲计时；释放时归还活动引用。空闲回收需满足活动计数等条件。统计无指定 ID 时也先列出容器。
4. 本项目日志页通过独立 `FollowLogsAsync` 和独立的 Lifetime 链接令牌跟随；上游长操作可保持活动，运行中的容器本身也可能阻止空闲回收。

因此，“本项目频繁查询可能延后已有会话的空闲计时或唤起 VM”是版本源码支持的推断；本任务没有独立可销毁会话的 VM 生命周期实测，不能报告实际回收时间、内存下降或节能百分比。主线行为没有被当作本机实测结果。

## 调度行为

| 场景 | 行为 |
| --- | --- |
| 默认或偏好关闭 | 保留原周期，即使最小化也继续自动查询 |
| 开启并保存，窗口最小化 | 后续自动清单与统计查询暂停；已开始的查询完成 |
| 暂时失焦／切换到其他应用 | 不改变策略，未订阅 Deactivated 或 IsActive |
| 恢复 Normal／Maximized | 请求一次刷新，无需等到配置间隔到期 |
| 恢复时任务忙碌 | 合并待刷新请求，任务结束后唤醒，不取消任务 |
| 快速最小化／恢复 | 一个循环、一个待刷新标记，执行前重新检查资格 |
| 用户手动刷新 | 仍可执行；沿用已有忙碌互斥规则，不与执行中任务抢占 |
| 已有操作完成后的刷新 | 保留 ViewModel 的显式刷新，消耗待刷新请求，避免同一恢复请求重复执行 |
| 日志跟随、上传／下载、构建 | 调度策略不改这些操作的令牌；日志仍可能维持活动 |
| 初始窗口已最小化 | 仍检测能力；开启偏好时将首次清单推迟到恢复 |
| 关闭 | 解除窗口状态订阅，取消工作区 Lifetime，阻止新查询与取消后的清单发布 |

暂停只改变请求调度；不调用 terminate／shutdown，不停止容器或会话，不改原生 keep-alive／idle 设置。任务 08 诊断与 09 主动探测沿用现有手动入口和取消语义。

## 实际命令与验证结果

以下命令已执行：

```powershell
wslc version
wslc container list --help
wslc container stats --help
wslc container logs --help
wslc system info --format json
wsl --version
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
```

- 最终 build：0 警告、0 错误。
- 最终常规测试：313 项，311 成功、2 个已有 opt-in 实测默认跳过、0 失败。
- 新增 11 项调度／工作区回归，另扩展应用偏好的磁盘读写测试。
- 使用 `TickAsync` 可控调度入口及 TaskCompletionSource 控制在途查询／任务，无需真实等待空闲期限。异步唤醒断言只有 5 秒失败上限，生产间隔设为 300 秒，验证恢复立即唤醒而非等待定时器。
- 覆盖默认兼容、最小化暂停五类查询、恢复、手动刷新、快速切换、忙碌任务、日志令牌、刷新重入、刷新失败后恢复、退出取消、初始最小化、偏好变更、本地化键及窗口订阅释放。
- 其中暂停测试连续推进 12 个调度 tick，运行时 Mock 收到 0 个新调用；恢复一轮收到 5 个调用。这是调度测试计数，不是实际 WSLC 频率或资源节省测量。
- 初次测试中直接调用保存命令触发测试宿主缺少应用相对 XAML 资源；已将用例聚焦于偏好加载、应用调度和持久化，真实桌面保存交互保留为人工验收项。修正后全套测试通过。
- `git diff --check` 通过；提交前执行 `git diff --cached --check`。

本任务没有创建测试会话、容器或镜像，没有对已有用户资源执行变更操作。版本／帮助与 system info 为只读验证；未调用 prune、shutdown、原生设置修改或 WSL 升级。

## 页面渲染与边界

截图位于 `docs/validation/screenshots/wslc-10/`，共 16 张：6 张起始基线 `before-*`、6 张改动后的 `after-*`，覆盖中英文 × Light／Dark／System；另有两种语言的 550×700 窄视口及向下滚动截图。常规视口为 850×700。本机此次 System 解析为深色。

已检查两种语言和三种主题下勾选项、说明、状态、保存按钮及窄视口换行。[之前](screenshots/wslc-10/before-zh-CN-Light.png)、[之后](screenshots/wslc-10/after-zh-CN-Light.png)、[英文深色](screenshots/wslc-10/after-en-US-Dark.png)、[英文窄视口](screenshots/wslc-10/narrow-en-US-Light.png)。

渲染入口扩展 `--preferences`，仍使用实际 WPF SettingsPage 与设计偏好；注册表登录区为已有设计示例，无用户凭据。下拉选择是设计偏好，截图主题由渲染程序分别应用。

```powershell
$env:EXWSLC_LIVE_DIAGNOSTICS = '1'
dotnet run --project docs/validation/diagnostics-render/DiagnosticsRender.csproj -- docs/validation/screenshots/wslc-10 --preferences
Remove-Item Env:EXWSLC_LIVE_DIAGNOSTICS
```

此入口只查询诊断信息，使用 RenderTargetBitmap 渲染控件，不启动库存轮询、不写用户偏好，也不是完整桌面截图。当前会话的原生桌面控制接口不可用；尚未人工验证完整窗口的真实最小化／恢复、失焦、鼠标／键盘保存、系统主题实时切换、高 DPI。对应调度逻辑已通过回归，真实 VM 空闲回收和资源节省仍未实测。
