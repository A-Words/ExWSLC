# 任务 05：容器与本地文件双向传输

## 基线与范围

- 工作树：`C:\Users\A_Words\.codex\worktrees\1f9e\ExWSLC`。
- 起始基线：`13ef82f970b84470e7445f144304bc211713b6ce`（任务 04），包含 01、02、03；开始时工作树干净。
- 分支：`feat/container-file-transfer`。没有合并、推送或改写旧提交。交付 SHA 见本文件所在提交与交付消息。
- 验证日期：2026-09-12。首版使用 `wslc container cp` 双向复制文件或目录，保留来源名称，目标限定为已存在目录。

## 环境与能力来源

| 层次 | 本次核实 |
| --- | --- |
| Windows | Windows 11，10.0.26200.9445，x64 |
| WSL / 内核 | 2.9.10.0 / 6.18.40.1-1 |
| CLI / 服务 | `wslc version --format json`：Client.Version=2.9.10.0；能力检测服务版本=2.9.10 |
| C# SDK | 项目和实际 NuGet 包 `Microsoft.WSL.Containers` 2.9.9；不修改 SDK 集成 |
| .NET | SDK 10.0.401；测试运行时 10.0.12 |
| 公开发行 | GitHub release 列表：稳定版 2.7.14、预览版 2.9.11；它们不是本机验收版本 |
| 上游主线 | 本次查询 master=`eaa69e766cf375d96053207a4ba8858f54ea1536`；运行行为以安装的 2.9.10 为准 |

已读取 [复制命令 PR 40835](https://github.com/microsoft/WSL/pull/40835)、[路径规范化 PR 41190](https://github.com/microsoft/WSL/pull/41190)、[2.9.10 ContainerTasks.cpp](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/tasks/ContainerTasks.cpp)、[ContainerService.cpp](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/services/ContainerService.cpp)、[官方 C# 参考](https://wsl.dev/api-reference/csharp/) 和 [2.9.9 SDK IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl)。公开 C# SDK 没有对应的 Copy/Archive 入口；内部 COM 的 UploadArchive/DownloadArchive 不作为公开 SDK 能力。本任务通过现有 CLI runner 实现。

本机 `wslc container cp --help` 支持 `SOURCE DEST`、双向 `CONTAINER:PATH`、stdin 上传，以及兼容性 `--archive/-a`；没有百分比或跟随链接选项。复用既有 `RuntimeFeature.ContainerCopy` 的缓存帮助检测，未新增探测、改变缓存或重做任务 01、02。UI 和 runtime 都要求 Supported，Unknown 不执行，用户可在设置重新检测。

## 实测语义与产品边界

实现前在独立 `exwslc-05-probe-3c79fd635f` 容器和同名临时目录探测，结束后清理。后续 opt-in 测试复验已采用的语义。

| 情形 | CLI 2.9.10 实测与处理 |
| --- | --- |
| 上传文件 → 已存在目录 | 成功，保留来源文件名；同名文件覆盖 |
| 上传 → 已存在文件 / 不存在的目录 | 分别返回 `extraction point is not a directory` / `ERROR_PATH_NOT_FOUND`；上传提示明确要求目录 |
| 上传目录 | 在目标下保留目录名；`本地目录\.` 实测仍复制目录本身，不提供“只复制内容”选项 |
| 下载文件 / 目录 → 目录 | 成功；保留来源名称；同名文件覆盖，目录合并且无关文件保留 |
| 下载容器目录 `/.` | CLI 可复制内容；首版双向统一为保留来源名，拒绝点路径段 |
| 下载链接 → 指定本地文件名 | 原生 CLI 返回 0 却创建 0 字节文件；首版禁止该路径，下载统一使用目录提取 |
| 符号链接 → 目录 | 实测相对链接保留，下载后 LinkTarget=`original`，上传本地链接后容器 `readlink` 仍为 `original`；不提供 dereference 开关，Windows 创建链接权限不足由原始错误反馈 |
| Windows 盘符路径 | `C:\...` 与容器端点区别明确；空格、中文、`&`、`;`、`$(literal)`，以及容器路径中的冒号按字面复制，字节一致 |
| 本地目标写入拒绝 | 持有目标文件独占句柄时，原生 tar 返回 `Can't unlink already-existing object: Permission denied`，已有字节保留 |
| 取消 | 暖缓存下启动 256 MiB 文件上传进程后取消，收到 OperationCanceledException，原始本地文件和目标 sentinel 保留；不承诺远端回滚 |

额外限制：仅接受 Windows 盘符绝对路径，排除 UNC、设备路径、备用数据流、盘符根目录和点路径段。当前上游把上传 basename 传给 tar，未加选项终止符，因此拒绝以 `-` 开头的上传源名称。未实现重命名、目标文件路径下载、远程浏览、仅目录内容复制或完整容器导出替代路径复制。

没有尝试切换 Windows 链接权限、升级 WSL 或修改原生 WSLC 设置。未验证其他 CLI 版本、跨文件系统元数据/ACL 完整保真、无链接创建权限账户，以及进程被外部强制终止后的服务端最终状态。运行时归档访问不等价于容器进程 UID；权限失败验收采用可复现的本地目标写入拒绝。

## 实现与共享接口

- `IContainerRuntime.CopyContainerPathAsync(ContainerCopyRequest, progress, token)`：不可变请求包含方向、容器 ID、本地路径、容器路径。更新设计时实现，实际参数通过 `WslcProcessRunner` 的 `ProcessStartInfo.ArgumentList`；不拼 shell 命令。
- `ContainerCopyOptions` 校验已支持的路径语义。下载追加目录分隔符，始终进入目录提取分支；上传保留源名称。运行时失败原样保留 stdout/stderr；取消退出码 -2 转为取消异常，使既有 TaskService 标为 Cancelled。
- 容器详情新增“文件”标签，提供上传/下载方向、本地路径、容器路径和既有 `IUserInteractionService` 文件/目录选择器。开始前确认两端路径、目录合并、同名替换及取消不回滚；不逐项伪造远端目标存在判断。
- 确认前捕获完整请求，先锁定当前传输；确认过程中切换容器或改变字段不会改变已捕获目标。结果包含原目标名和 ID。既有工作区任务与取消命令承载实际操作。
- 普通库存刷新不禁用输入框，避免丢失焦点；刷新或其他任务运行时禁用开始操作。收到取消或失败后不删除目标、不执行回滚。进度区只展示真实状态和 CLI 输出，没有百分比。
- 应用没有创建暂存文件。上传临时 tar 由该 CLI 的 DeleteOnClose 句柄管理；目录下载走管道，避开文件目标的临时归档分支。测试只创建各自唯一目录，在 finally 中独立清理本地与容器资源。

## 自动与真实验证

```powershell
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln --no-build
$env:EXWSLC_COPY_LIVE='1'
$env:EXWSLC_COPY_IMAGE='docker.1ms.run/nginx:latest'
dotnet test --project tests/ExWSLC.Tests.csproj --no-build --filter-class '*ContainerCopyLiveTests' --output Detailed
git diff --check
git diff --cached --check
```

- 最终构建：0 警告、0 错误。完整测试：362 总计，359 通过，3 个真实环境测试默认跳过；另已运行普通 `dotnet test ExWSLC.sln`。
- 定向新增回归覆盖双向参数、文件/目录、路径字面值、错误/能力门控、拒绝覆盖、取消、确认和执行期间切换容器、选择器取消、库存刷新期间输入、XAML 资源和双语资源完整性。
- 真实测试最终复跑 1/1 通过（约 3.5 秒测试体）；隔离容器 `exwslc-05-live-bc235d16a7` 与同名 `%TEMP%` 目录均清理。此前包含 UI 保留期的 `exwslc-05-live-d8f80bc0af` 也通过并清理。
- 真实命令均由测试通过 ArgumentList 发出，例如上传 `container cp <本地目录> <测试容器>:/tmp/exwslc-05/目标 : ; $(literal)`、下载 `container cp <测试容器>:/tmp/exwslc-05/links <本地目标目录>\`；带空格或特殊字符的两端各是一个参数。可复现的完整构造与断言见 `tests/ContainerCopyLiveTests.cs`。
- 没有拉取/删除用户镜像、修改用户容器或网络、运行 prune/shutdown。取消用例保留已有 sentinel，并在测试 finally 中只清理本测试的资源。

## 原生界面验收

从本工作树 Debug 构建启动真实 WPF 应用，以 1100×800 原窗口尺寸检查，中英文及系统/浅色/深色主题。系统主题当时为深色。前后截图目录为 `docs/validation/screenshots/wslc-05/`，均为原生截图，无模拟页面。

- `before-zh-system.jpg`：改动前详情页，没有文件入口。
- `after-zh-system-confirm.jpg`：独立测试容器的来源/目标与覆盖确认。
- `after-zh-system-upload.jpg`：通过应用实际上传并覆盖测试文件，显示完成状态；未显示虚假百分比。
- `after-zh-light.jpg`：中文浅色，长中文/空格/特殊字符路径。
- `after-en-light.jpg`、`after-en-dark.jpg`、`after-en-system.jpg`：英文浅色、深色、系统主题布局。
- `after-zh-dark.jpg`：中文深色布局；中英文和三种主题组合均实际打开检查。

原生界面确认了上传执行、覆盖确认和下载方向切换；下载字节、目录、链接、权限失败和取消由真实 CLI 集成测试验证。GUI 下载完整点击流程在隔离容器保留期结束后未继续提交，不将其计为 GUI 下载成功。只读布局检查可打开现有容器，但未对它们执行传输。

验收结束后恢复原来的简体中文、系统主题、5 秒刷新偏好并关闭应用；再次检查没有 `exwslc-05-*` 测试容器或临时目录残留。
