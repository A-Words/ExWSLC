# ExWSLC：WSLC 升级任务提示词

恢复日期：2026-09-12。原来的 14 份文档从本任务的历史文件记录恢复；另补 A／B／C 三份整组启动 prompt。基础任务 01、02 已实现，03—12 的文字仍是待执行要求。

每个编号文件都是一份可以独立复制到新任务的完整 prompt，包含范围、前置条件、来源、测试与交付要求。整组启动文件可直接发给 A／B／C，由执行者按顺序读取该组的编号文件。此次只恢复并提交文档，没有启动三个工作树的实现。

## 现在直接使用

- [A：容器组启动 prompt](./A-container-worktree.md)：03 → 04 → 05 → 07。
- [B：镜像组启动 prompt](./B-image-worktree.md)：06。
- [C：设置与诊断组启动 prompt](./C-diagnostics-worktree.md)：08 → 09 → 10。

从主仓库当前 main 的同一提交创建三个工作树；该提交需要同时包含本目录和公共代码基线 `f85b111`。不要只从历史提交 `f85b111` 创建，因为它尚未包含本次恢复的 prompt 文档。复制对应整组启动文件全文到该工作树的任务即可。

已完成：01 为 `d9e321e`，02 为 `f85b111`。SDK 包为 2.9.9，CLI／服务版本分别记录；19 项能力检测与缓存契约见 [运行时能力契约](../runtime-capabilities.md)。基础验收为 217 / 217 测试通过，详情见 [01 验证](../validation/wslc-01.md)、[02 验证](../validation/wslc-02.md)。

## 推荐执行顺序

01、02 已完成，不再投放实现。从上述共同起点创建三个工作树，每组内串行，组与组并行：

| 阶段／工作树 | 执行顺序 | 原因 |
|---|---|---|
| 公共基线，已完成 | 01 → 02 | `d9e321e` → `f85b111` |
| A：容器 | 03 → 04 → 05 → 07 | 这些功能共同修改容器 ViewModel、创建表单和详情视图 |
| B：镜像 | 06 | 主要修改镜像工作区 |
| C：设置与诊断 | 08 → 09 → 10 | 复用诊断入口，最后接入刷新调度 |
| 后置任务 | 11、12 | 先确认公开发行接口已可用；12 依赖刷新调度 |
| 原会话审查 | 99 | 审查提交、完整差异、测试和跨任务交互 |

这里的组内顺序主要用来减少编辑冲突，不意味着所有业务功能天然依赖前一项。例如文件传输的功能前置是 01、02，但按建议在 04 的提交上继续做。提示词中的“按建议顺序”可以随实际分工调整，不能跳过明确需要的能力约定或共享诊断接口。

同一组可以在同一工作树内继续不同会话；换会话时，把该组最新提交作为起点。每份 prompt 要求的“不要自行合并其他分支”用于防止执行者猜测并修改其他工作树；公共基线和集成分支由你统一选择。

## 一条一条的提示词

- [01：适配新版 CLI JSON 输出](./01-runtime-json-compatibility.md)
- [02：建立版本与功能能力检测基线](./02-runtime-capabilities-sdk.md)
- [03：增加容器健康检查配置与状态详情](./03-container-health.md)
- [04：增加网络连接、断开与 IP 配置](./04-network-operations.md)
- [05：增加容器与本地文件的双向传输](./05-container-file-transfer.md)
- [06：完善镜像构建参数、输出与进度](./06-advanced-image-build.md)
- [07：完善创建、挂载和停止参数](./07-container-create-stop-options.md)
- [08：增加运行环境诊断与活动会话视图](./08-runtime-diagnostics-sessions.md)
- [09：增加容器访问 Windows 本地服务的诊断](./09-host-loopback-diagnostics.md)
- [10：增加空闲时的刷新调度与节能选项](./10-idle-aware-refresh.md)
- [11：后置：接入已公开发行的原生重启](./11-native-container-restart.md)
- [12：后置：用公开事件流触发资源刷新](./12-runtime-event-refresh.md)
- [99：原会话统一只读复查](./99-integration-review.md)

01、02 属于基础适配；03、04、05 是优先新增功能；06—10 是后续扩展。11、12 目前属于条件任务，公开接口没有就不投放实现，不能把主线已合入等同于发行版可用。

## 并行时需要保留的边界

工作树可以隔离文件，但不能消除同一文件的语义冲突。三组仍可能共同修改：

- src/Services/IContainerRuntime.cs
- src/Services/WslcContainerRuntime.cs
- src/ViewModels/Design/DesignContainerRuntime.cs
- src/Models/RuntimeCapabilities.cs 及能力检测的必要增量
- src/Resources/Strings.en-US.xaml、src/Resources/Strings.zh-CN.xaml
- 公共 Mock、运行时测试和文档

各任务只新增自己需要的方法、类型和资源键，遵守 02 的公共能力约定，不能复制出第二套检测逻辑或格式化整个文件。容器组内还会共同修改 ContainersViewModel、ContainerCreateSpec 和容器 XAML，因此不建议再拆成四个同时进行的工作树。

多个工作树共享本机 WSLC 运行时。真实测试要显式 opt-in，并使用带任务编号与唯一后缀的资源；不能使用已有用户资源，也不能通过全局清理命令互相干扰。

本次恢复文档单独提交，便于新工作树继承。若已创建的旧工作树缺少文档，可以向对应任务直接提供编号 prompt 全文；代码前置仍必须满足，执行者不得自行猜测并合并其他分支。各实现任务不能顺手暂存主工作树里与本任务无关的文件。

## 验证与交接约定

每项实现保留 docs/validation/wslc-编号.md，例如 docs/validation/wslc-03.md，写明版本、运行命令、结果、截图、共享接口、已知限制与未验证项。不同任务分别写文件，减少多人编辑同一验收文档的冲突。

任务完成后，让执行会话返回：

- 任务编号、完成状态。
- 工作树绝对路径、分支名。
- 起始基线 SHA、提交 SHA。
- build、test、diff 检查结果。
- 实机验证使用的 Windows／WSL／CLI／SDK 版本。
- 验证文档与截图路径、未验证项。

回到原会话时，附上这些信息并使用 99。可以先审查各分支；如果还没有包含全部结果的集成分支，无法据此断言跨分支整合后也通过测试。99 不会擅自合并，收到你指定的集成工作树后再检查整体行为。

## 首次调研记录与时效

下列结论保留首次调研时的背景，不能代替执行时的版本和公开接口核对：

- 首次调研时，本机 CLI 为 2.9.10.0，项目 Microsoft.WSL.Containers 包为 2.9.3；02 已将项目 SDK 包升级至 2.9.9。
- 最新 WSLC 预览版为 WSL 2.9.11；NuGet 版本索引当时列出 2.9.3、2.9.9，包版本与运行时版本不一致。[WSL 发布记录](https://github.com/microsoft/WSL/releases)；[包版本索引](https://api.nuget.org/v3-flatcontainer/microsoft.wsl.containers/index.json)。
- 新版列表／统计输出为逐行 JSON；原解析器的误判已由 01 修复。[上游变更](https://github.com/microsoft/WSL/pull/41280)。
- 2.9.11 的重启支持位于运行时层，CLI 重启命令在该版发布后才合入。[运行时 PR](https://github.com/microsoft/WSL/pull/41454)；[CLI PR](https://github.com/microsoft/WSL/pull/41435)。
- 事件流最初公开的变更位于底层 COM，不能假定 CLI／C# 投影可直接调用。[事件流 PR](https://github.com/microsoft/WSL/pull/40971)。

原生重启、事件订阅、宿主机回环与 VM 回收都需要区分“源码存在”“公开接口可用”“已发行”“本机已验证”。实现会话遇到某个实机环境缺失时，应继续完成独立可验证的工作并如实记录边界；11、12 的接口前置条件除外。
