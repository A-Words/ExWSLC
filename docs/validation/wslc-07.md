# 任务 07：创建、挂载与停止参数

## 基线和范围

- 工作树：`C:\Users\A_Words\.codex\worktrees\1f9e\ExWSLC`。
- 起始基线：`3c4ab022752b97fa7aaf315d7a2c8620518fe8a3`（任务 05）。已检查包含 01、02、03、04、05；开始时工作树干净。
- 分支：`feat/container-create-stop-options`。仅提交本任务；没有推送、合并或改写旧提交。交付 SHA 见本文件所属提交和交付消息。
- 验证日期：2026-09-12 至 2026-09-13。
- 新增创建拉取策略、停止默认配置、结构化挂载、手动停止选项；继续使用 stop → start 重启。未引入原生 restart、自动重启策略或原地修改配置。

## 环境与依据

| 层次 | 核实结果 |
| --- | --- |
| Windows | Windows 11 专业版，10.0.26200.9445，x64 |
| WSL / 内核 | 2.9.10.0 / 6.18.40.1-1 |
| CLI / 服务 | Client.Version=2.9.10.0；SDK 版本查询得到服务 2.9.10 |
| C# SDK | 项目和实际 NuGet 包 `Microsoft.WSL.Containers` 2.9.9；没有修改 SDK 集成 |
| .NET | SDK 10.0.401；运行时 10.0.12 |
| 公开发行 | 本次查询稳定版 2.7.14、预览版 2.9.11；均不作为本机运行版本 |
| 上游主线 | 本次查询 master=`eaa69e766cf375d96053207a4ba8858f54ea1536`；行为以本机 2.9.10 和对应源码为准 |

执行了 `wslc version --format json`、`wslc container create --help`、`wslc container stop --help`、`wslc container inspect --help`、`wsl --version`、`dotnet --version`。

核对 [停止超时 PR 40919](https://github.com/microsoft/WSL/pull/40919)、[拉取策略 PR 41304](https://github.com/microsoft/WSL/pull/41304)、[挂载 PR 41337](https://github.com/microsoft/WSL/pull/41337)，以及安装版本对应的源码：

- [ContainerTasks.cpp](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/tasks/ContainerTasks.cpp) 和 [ContainerModel.h](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/services/ContainerModel.h)：未提供 `--time` 时使用默认继承哨兵；未提供 `--signal` 时使用容器 STOPSIGNAL。帮助中的默认秒数说明不能替代实际参数省略语义。
- [MountSpecParsing.cpp](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/common/MountSpecParsing.cpp)、[stringshared.h](https://github.com/microsoft/WSL/blob/2.9.10/src/shared/inc/stringshared.h)、[上游挂载测试](https://github.com/microsoft/WSL/blob/2.9.10/test/windows/wslc/WSLCCLIMountParserUnitTests.cpp)：CSV 字段引用、双引号加倍、Windows 盘符、简单挂载右侧冒号拆分、目标路径归一化。
- [SpecParsing.cpp](https://github.com/microsoft/WSL/blob/2.9.10/src/windows/wslc/arguments/SpecParsing.cpp)：当前 CLI 接受信号编号 1–31 和名称／省略 SIG 的名称，忽略大小写；当前映射含 `SIGTKFLT`，不擅自当作完整 Docker 信号集或接受实时信号。
- [官方 C# API](https://wsl.dev/api-reference/csharp/) 与 [2.9.9 SDK IDL](https://github.com/microsoft/WSL/blob/2.9.9/src/windows/WslcSDK/winrt/wslcsdk.idl)：SDK `Stop(Signal, TimeSpan)` 及创建设置不等同于本任务新增 CLI 选项；本任务继续通过 `IProcessRunner` 和 `ArgumentList` 调用 CLI。

## 行为和共享接口

| 输入 | 发送到 CLI |
| --- | --- |
| 创建选项留空／运行时默认 | 不发送覆盖参数；基础创建仍为 `run --detach IMAGE` |
| always / missing / never | `--pull POLICY` |
| 创建停止信号／超时 | `--stop-signal SIGNAL` / `--stop-timeout SECONDS` |
| 手动停止留空 | `container stop ID`，继承容器配置 |
| 手动停止覆盖 | `container stop --time SECONDS --signal SIGNAL ID`，仅发送填写项 |
| 超时 | 空值、0、正整数、-1；拒绝小于 -1、小数、单位和 int 溢出 |
| bind / named volume | `--mount type=…,source=…,target=…,readonly=true/false` |
| tmpfs | `--tmpfs /target:ro` 或 `:rw`，不传来源；目标中的冒号无法由此语法表达，因此拒绝 |
| 原简单挂载 | 每行一个 `source:/target[:ro 或 :rw]`，继续传 `--volume` |

简单输入不再按逗号分割，以免拆坏 Windows 路径。表单明确说明：原来的逗号分隔多项需要改成分行。结构化 CSV 对整个 `key=value` 字段引用，并把内部双引号加倍；反斜杠和 shell 字符按普通字符保留。没有用 shell 拼接执行命令。重复目标按 Linux 大小写敏感规则处理重复斜杠、`.`、`..`，同时检查简单输入和结构化项。

`ContainerCreateSpec` 新增 `PullPolicy`、`StopSignal`、`StopTimeoutSeconds`、`Mounts`。`ContainerMountSpec` 复用既有 `ContainerMountKind`；保留原健康检查、环境、端口和自动删除选项。`IContainerRuntime` 增加接收不可变 `ContainerStopOptions` 的重载；保留旧重载，默认语义从固定 10 秒改为继承。运行时支持门禁在发出命令前再次检查。

能力检测复用四个既有创建能力，只新增 `CreateTmpfs` 和停止命令的 `StopTimeout` / `StopSignal`，沿用缓存和设置页重新检测。旧版／Unknown 不能发送不支持的覆盖；默认创建和停止不依赖新能力。

`IUserInteractionService` 增加停止选项对话框；设计时实现同步更新。列表与详情的停止均捕获目标后打开同一对话框，不受期间选中项变化影响。停止、现有重启和导出使用现有任务服务和工作区取消；容器页新增任务状态与取消入口，取消消息不会被库存刷新立即覆盖。

`ContainerExportOperation` 把临时停止、导出、恢复纳入同一任务。对原本运行的容器，即使停止／导出失败或取消，也会在 `finally` 尝试恢复；恢复使用独立的 30 秒令牌。恢复失败会报告主操作和恢复错误，不把导出成功等同于完整成功。原本停止的容器不会被启动。

自动删除的配置不被改写，已删除的容器不被重新创建。本机常规 inspect 的 `HostConfig` 没有明确 `AutoRemove` 字段；本任务不推断内部元数据标签的位定义。临时导出确认框明确说明自动删除风险，并要求确认未开启自动删除后再继续。手动停止仍执行正常的自动删除语义。

## 验证

自动测试覆盖参数默认值／全部策略／超时边界／信号、CSV 和 Windows 特殊路径、三种挂载与读写、重复目标、能力缺失、列表和详情的目标捕获、取消、重启 stop → start、导出成功／失败／异常／取消和恢复失败。XAML 实例化与双语资源检查纳入测试。

最终结果：`dotnet build ExWSLC.sln` 成功，0 警告、0 错误；`dotnet test ExWSLC.sln` 共 426 项，422 通过、4 项 opt-in live 测试跳过；单独启用本任务真实测试后 1/1 通过，最终一次约 14 秒。`git diff --check` 通过。界面持有测试也正常完成并清理，没有中止测试进程来跳过清理。

```powershell
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check

$env:EXWSLC_LAUNCH_LIVE = '1'
$env:EXWSLC_LAUNCH_IMAGE = 'docker.1ms.run/nginx:latest' # 已有本地镜像，仅作为隔离标签的来源
dotnet test --project tests/ExWSLC.Tests.csproj --no-build --filter-class '*ContainerLaunchLiveTests' --output Detailed
```

真实测试显式 opt-in，资源使用 `exwslc-07-唯一后缀`。给已有镜像添加唯一的本地测试标签，不修改原标签；创建专属卷、容器、临时目录，所有路径都在 `finally` 清理并检查不存在。没有使用全局 prune、shutdown、原生设置修改或升级。

已完成的实际验证：

- 缓存命中时 missing / never 创建成功；always 即使有本地专属标签也尝试向 `127.0.0.1:1` 拉取并失败。没有运行镜像仓库，因此**未验证 always 的远端拉取成功路径**。
- `C:\…\中文, space & literal` 绑定到含逗号及双引号的容器目标，读取标记成功、只读写入失败；同一命名卷的 rw/ro 挂载，以及 tmpfs 的 rw/ro 都按预期工作。
- 创建配置为 0 秒后，省略 `--time` 的实际停止约 0.24 秒；配置为 1 秒后约 1.23 秒。配置 `SIGINT` 和 -1 后，默认停止使用该信号退出。
- 手动 `--time -1 --signal SIGTERM` 可取消客户端等待，再以测试专属容器的 0 秒/KILL 完成停止；`--rm` 容器停止后从清单消失。
- 原生 UI 实测发现并修复缺少取消按钮的问题；最终按钮可以取消无限等待，页面恢复可操作并显示取消边界。
- 最终真实测试新增导出至专属 `export.tar` 的成功场景，以及导出到目录路径的失败场景；两者均经临时停止后恢复原运行容器。导出抛异常／取消／恢复失败另有隔离的 Mock 回归测试。

## 原生界面证据

使用当前工作树编译的 WPF 应用，窗口 1100×800，原生桌面自动化操作。构建中间验收版输出在忽略目录 `artifacts/wslc-07-ui`，以便隔离的测试容器仍由测试进程持有并在期限到达后自动清理。截图为实际窗口，不是 XAML 渲染替代。

- [修改前创建表单，中文／系统](screenshots/wslc-07/before-create-zh-system.jpg)
- [修改后创建表单，中文／系统](screenshots/wslc-07/after-create-zh-system.jpg)
- [创建、挂载、保留健康检查，英文／浅色](screenshots/wslc-07/after-create-en-light.jpg)
- [停止选项，英文／浅色](screenshots/wslc-07/stop-options-en-light.jpg)
- [非法超时，英文／浅色](screenshots/wslc-07/stop-invalid-en-light.jpg)
- [无限等待及取消按钮](screenshots/wslc-07/stop-running-en-light.jpg)
- [取消后恢复操作](screenshots/wslc-07/stop-cancelled-en-light.jpg)
- [详情停止选项，中文／深色](screenshots/wslc-07/stop-options-zh-dark.jpg)
- [继承配置实际停止成功，中文／深色](screenshots/wslc-07/stop-completed-zh-dark.jpg)
- [既有重启路径完成，中文／深色](screenshots/wslc-07/restart-completed-zh-dark.jpg)

已检查中文／系统、英文／浅色、中文／深色这三组组合；没有把它们描述为六种语言与主题组合的全排列验收。列表取消和详情继承停止都在专属容器上实际执行，未操作已有用户容器。

结束后恢复原偏好 `zh-CN / System / 5 秒`，关闭应用。三轮真实验证的唯一前缀为 `exwslc-07-1bb6740cd4`、`exwslc-07-90d5622bbf`、`exwslc-07-de1f0cf56f`，均已清理容器、卷、专属镜像标签和临时目录。

## 边界

- 能力支持表示入口存在；绑定路径是否存在、共享目录权限、镜像与运行时配置仍由 WSLC 最终判断。UNC 参数已做回归测试，未连接真实远程 SMB 共享。
- tmpfs 的大小／mode、传播策略、卷驱动高级选项不在本次编辑器范围。
- 取消停止终止客户端等待，不能撤销已被服务接受的停止请求。导出恢复是有时间上限的尝试；服务不可用、自动删除、关闭／终止应用，或取消时仍在服务侧执行的停止，都不能保证恢复最终状态。
- 原生界面不要求用户使用 CLI；仅验收文档记录命令。没有持久化运行时清单、创建规格或凭据。
