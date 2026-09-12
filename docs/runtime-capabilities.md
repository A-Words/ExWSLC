# WSLC 能力契约

任务 02 将运行时检测集中在 `RuntimeCapabilityService`，由 `IRuntimeCapabilityService` 提供快照。库存及后续变更操作继续通过 `IContainerRuntime`；本契约不实现健康检查、复制、构建或事件等后续功能。

- `CliVersion` 来自 `wslc version --format json` 的 `Client.Version`，回退只接受已核实的 `wslc <version>` / `--version` 文本；无法读出版本不等于 CLI 不可用。
- `ServiceVersion` 是 SDK 的 `WslcService.GetVersion()` 返回的运行时服务版本。`SdkPackageVersion` 来自构建时注入的 NuGet 包版本元数据，二者没有替代关系。
- CLI、SDK、服务分别报告 `Supported`、`Unsupported` 或 `Unknown`。确定无法启动 CLI 或缺少 WSL / VirtualMachinePlatform 必需组件才阻止库存；探测失败仍允许实际库存命令报告自己的结果。
- `RuntimeFeature` 的每一项通过快照索引器取得能力、可本地化的 `ReasonKey` 和 `Source`。本阶段这些功能项描述 CLI 路径；`Supported` 表示本机公开入口存在，不保证具体操作、镜像、网络或内核配置一定成功。`Unsupported` 仅用于成功且结构可识别的帮助中缺少命令 / 选项；帮助失败、空输出或无法识别的格式是 `Unknown`。SDK 2.9.9 未公开的 restart / events 后端不能覆盖 CLI 的探测结论。
- 探测只执行版本和 `--help`，不创建 SDK session 或资源。选项按完整 token 匹配，帮助说明中提及的选项不算支持证据；不依赖本地化标题。构建选项、创建选项、网络连接选项分别判断；native restart 和 events 不能根据内部 COM 或版本号推断。
- `HealthChecks` 是完整配置组，要求 `--health-cmd`、`--health-interval`、`--health-retries`、`--health-start-period`、`--health-timeout`、`--no-healthcheck` 全部存在；其余高级选项逐项判断。环境说明保存为 `MessageKey` / `MessageArguments`，设置页按当前语言展示，切换语言不重新运行探测。
- 任务 04 复用网络连接/断开/IP/别名能力，增加 `NetworkConnectDriverOptions`、`NetworkCreateSubnet`、`NetworkCreateGateway`、`NetworkCreateIpRange`。只增加一次缓存内的 `network create --help` 探测；创建参数分别判断，不将网络创建 `--opt` 当作连接端点 `--driver-opt`。UI 与运行时都要求新参数对应能力为 Supported，Unknown 也不发送；不改变原有无 IP 覆盖的创建路径。
- 成功取得的检测快照（包括明确的 Unknown）在进程内缓存，普通库存刷新不探测；设置页“重新检测”调用 `RefreshAsync`。检测被取消或抛异常不写缓存。检测和安装串行，单次 CLI 探测超时为 5 秒；SDK 同步查询移到后台线程，在调用前后检查取消。安装开始即失效旧快照，失败 / 取消也不保留可能已过时的安装状态。
- SDK 调用封装在可模拟的 `IWslcSdkService`。用户确认安装后重新读取缺失组件，只向 `InstallOptions.Components` 传入可安装的 `WslPackage` / `VirtualMachinePlatform`，`Repair=false`。`SdkNeedsUpdate` 要更新应用打包的 SDK，不能通过安装入口修复。取消请求传给 WinRT，但底层安装不保证立即停止或回滚。安装成功重新检测、刷新库存并恢复自动刷新。

依据：已查阅 [官方 C# API](https://wsl.dev/api-reference/csharp/)，签名以 [2.9.9 IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl) 和 [WslcService 实现](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/WslcService.cpp) 为准；该版本公开 SDK 未提供 native restart 或运行时全局 events 流。
