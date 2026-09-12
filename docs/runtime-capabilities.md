# WSLC 能力契约

任务 02 将运行时检测集中在 `RuntimeCapabilityService`，由 `IRuntimeCapabilityService` 提供快照。库存及后续变更操作继续通过 `IContainerRuntime`；本契约不实现健康检查、复制、构建或事件等后续功能。

- `CliVersion` 来自 `wslc version --format json` 的 `Client.Version`，回退只接受已核实的 `wslc <version>` / `--version` 文本；无法读出版本不等于 CLI 不可用。
- `ServiceVersion` 是 SDK 的 `WslcService.GetVersion()` 返回的运行时服务版本。`SdkPackageVersion` 来自构建时注入的 NuGet 包版本元数据，二者没有替代关系。
- CLI、SDK、服务分别报告 `Supported`、`Unsupported` 或 `Unknown`。确定无法启动 CLI 或缺少 WSL / VirtualMachinePlatform 必需组件才阻止库存；探测失败仍允许实际库存命令报告自己的结果。
- `RuntimeFeature` 的每一项通过快照索引器取得能力、可本地化的 `ReasonKey` 和 `Source`。本阶段这些功能项描述 CLI 路径；`Supported` 表示本机公开入口存在，不保证具体操作、镜像、网络或内核配置一定成功。`Unsupported` 仅用于成功且结构可识别的帮助中缺少命令 / 选项；帮助失败、空输出或无法识别的格式是 `Unknown`。SDK 2.9.9 未公开的 restart / events 后端不能覆盖 CLI 的探测结论。
- 探测只执行版本和 `--help`，不创建 SDK session 或资源。选项按完整 token 匹配，帮助说明中提及的选项不算支持证据；不依赖本地化标题。构建选项、创建选项、网络连接选项分别判断；native restart 和 events 不能根据内部 COM 或版本号推断。
- `HealthChecks` 是完整配置组，要求 `--health-cmd`、`--health-interval`、`--health-retries`、`--health-start-period`、`--health-timeout`、`--no-healthcheck` 全部存在；其余高级选项逐项判断。环境说明保存为 `MessageKey` / `MessageArguments`，设置页按当前语言展示，切换语言不重新运行探测。
- 任务 04 复用网络连接/断开/IP/别名能力，增加 `NetworkConnectDriverOptions`、`NetworkCreateSubnet`、`NetworkCreateGateway`、`NetworkCreateIpRange`。只增加一次缓存内的 `network create --help` 探测；创建参数分别判断，不将网络创建 `--opt` 当作连接端点 `--driver-opt`。UI 与运行时都要求新参数对应能力为 Supported，Unknown 也不发送；不改变原有无 IP 覆盖的创建路径。
- 任务 07 复用 `CreateMount`、`CreatePullPolicy`、`CreateStopTimeout`、`CreateStopSignal`；增加创建帮助中的 `CreateTmpfs` 和独立 `container stop --help` 的 `StopTimeout` / `StopSignal`。创建的 `--stop-timeout` 与停止的 `--time` 分别检测。未设置时不发参数，保留容器默认停止配置；Unknown / Unsupported 不发送覆盖参数。结构化 bind / named volume 使用 `--mount`，tmpfs 使用 `--tmpfs`，原简单输入继续使用 `--volume`。
- 成功取得的检测快照（包括明确的 Unknown）在进程内缓存，普通库存刷新不探测；设置页“重新检测”调用 `RefreshAsync`。检测被取消或抛异常不写缓存。检测和安装串行，单次 CLI 探测超时为 5 秒；SDK 同步查询移到后台线程，在调用前后检查取消。安装开始即失效旧快照，失败 / 取消也不保留可能已过时的安装状态。
- SDK 调用封装在可模拟的 `IWslcSdkService`。用户确认安装后重新读取缺失组件，只向 `InstallOptions.Components` 传入可安装的 `WslPackage` / `VirtualMachinePlatform`，`Repair=false`。`SdkNeedsUpdate` 要更新应用打包的 SDK，不能通过安装入口修复。取消请求传给 WinRT，但底层安装不保证立即停止或回滚。安装成功重新检测、刷新库存并恢复自动刷新。

依据：已查阅 [官方 C# API](https://wsl.dev/api-reference/csharp/)，签名以 [2.9.9 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 和 [WslcService 实现](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/WslcService.cpp) 为准；该版本公开 SDK 未提供 native restart 或运行时全局 events 流。

任务 06 的构建表单消费现有 BuildSecret / BuildOutput / BuildProgress / BuildPull 快照，不增加探测或缓存。BuildOutput 只表示 `--output` 入口存在，不代表任意 exporter 都可用；本机 2.9.10 虽在帮助举例 `type=local`，实际拒绝目录 exporter，因此表单只提供本地镜像与 tar。Unsupported 阻止对应操作；Unknown 保留实际执行入口，重检后允许用户清除旧选项。具体命令、参数边界与验收见 [任务 06 验证记录](validation/wslc-06.md)。
## 任务 08：诊断快照

`IContainerRuntime.GetSystemInfoAsync(RuntimeCapabilities, CancellationToken)` 接收同一能力服务的快照，通过 CLI 读取 `system info --format json`。不新增 SDK 探测或版本缓存：`RuntimeFeature.SystemInfo` 明确不支持时直接回退；Unknown 允许实际查询。设置页“刷新诊断”复用缓存检测，原“重新检测”仍负责失效并重检能力。

诊断快照在内存中独立保存采集时间，并分开呈现 `system info` 客户端／会话管理服务版本与能力快照的基础版本；不覆盖原有版本来源。查询超时为 10 秒，沿用工作区任务与取消入口。失败更新为当前失败快照并保留基础版本；取消保留上一次快照及原时间。首次进入设置页且工作区空闲时自动采集，否则提供手动刷新；库存自动刷新不采集诊断。

解析器只接收版本、设置路径及会话 Name／ID／CreatorPid，未知字段忽略。返回的类型化对象已脱敏：默认设置路径缩写为 `%LOCALAPPDATA%\wslc\settings.yaml`，其他路径遮蔽；默认会话名称保留 `wslc-cli-` 前缀并隐藏账户，GUID 会话名保留，自定义自由文本名遮蔽。版本只接受已知格式；不识别格式显示未知。界面、复制及导出使用相同字段白名单。原始 stdout、stderr 和异常消息不写入诊断任务详情，也不持久化快照。

验收记录及已知边界见 [任务 08 验证](validation/wslc-08.md)。

## 任务 09：宿主机回环诊断

`RuntimeFeature.HostLoopback` 在能力快照中保持 `Unknown`，来源为 `session.hostLoopback`。版本或帮助不能证明已有会话、SDK 创建的会话启用了此能力；原有缓存／重检流程不增加连接探测。

`IContainerRuntime.GetHostLoopbackConfigurationAsync` 只读取当前用户 `%LOCALAPPDATA%\wslc\settings.yaml`。缺失、null 或 `default` 得到默认候选域名 `host.wslc.internal`，`none` 明确禁用；合法自定义 DNS 名仅表示配置可识别，不代表既有会话支持。无法读取、歧义 YAML 或超出安全解析边界时返回未知，不调用可能创建文件的 `wslc settings`。页面进入时可读取配置，网络检查始终需要用户选择运行中容器、明确端口并手动执行。

`IContainerRuntime.ProbeHostLoopbackAsync` 接收不可变目标及同一能力快照；执行前重新读取配置并定点 inspect 容器，配置变化则要求重新检查。通过 `ArgumentList` 传入固定脚本与独立目标参数，仅解析 IPv4 并尝试首个地址的一次 TCP 连接，不发送请求或读取响应。整体期限 15 秒、容器内期限 8 秒，沿用任务与取消机制；工具不存在时返回缺少工具，不安装依赖。

结果分别保留 DNS 阶段与最终连接状态。成功只证明解析端点接受了连接，不证明会话配置、应用协议或认证有效。结果只存于内存，复制摘要隐藏容器名称、自定义域名和原始进程输出。实现与实测边界见 [任务 09 验证](validation/wslc-09.md)。

## 任务 10：最小化时的刷新调度

`AppSettings.PauseAutoRefreshWhenMinimized` 默认 false，旧设置文件也保留现有刷新体验。开启并保存后，MainWindow 只将是否最小化传给 `RuntimeWorkspace.SetWindowMinimized`；失焦不暂停。窗口类型和 WPF 事件不进入运行时接口。

`AutoRefreshService` 仍只有一个循环，定时入口检查工作区忙碌、运行时可用及暂停状态。恢复窗口或最小化期间关闭该偏好，会合并为一个待刷新请求并唤醒循环；任务忙碌时保留请求，任务结束后唤醒。手动刷新和已有操作完成后的刷新不受暂停偏好阻止，开始刷新时消耗待刷新请求。已开始的查询不会因最小化取消；退出仍使用工作区 Lifetime 取消并阻止新刷新。

仅自动清单与统计受此策略控制。任务 08 的手动诊断、09 的主动探测、日志跟随及上传／下载／构建等操作保持原语义。没有新增 `RuntimeFeature`、修改能力缓存、调用会话终止或修改原生 idle/keep-alive 设置。暂停应用查询不等于停止容器或保证 VM 回收。源码证据、验证与桌面验收边界见 [任务 10 验证](validation/wslc-10.md)。
