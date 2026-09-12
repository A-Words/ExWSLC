# 11：后置：接入已公开发行的原生重启

> 当前起点：包含 f85b111 及本次恢复文档的同一提交；01、02 已完成。

项目是 ExWSLC，主仓库位于 E:/src/personal/ExWSLC。请在当前会话选定的工作树内完成本任务。先检查 git status、分支、HEAD 和 AGENTS.md，确认前置任务已包含在当前基线；若缺少，只报告缺少的任务／提交，不自行合并其他分支。
遵守仓库现有 WPF、MVVM、WPF-UI 和 xUnit v3 约定。运行时操作继续封装在 IContainerRuntime 后，通过 ProcessStartInfo.ArgumentList 传参；更新相关设计时实现、Mock 和双语资源。只持久化应用偏好，凭据和运行时清单不写入应用设置。
开始实现前，核对本机 wslc version、相关命令的 --help 和官方资料。基础任务 01、02 已完成，公共代码基线为 f85b111（含 d9e321e）。该基线使用 SDK 包 2.9.9，本机验收为 CLI 2.9.10.0／服务 2.9.10；执行时仍须重新核实。先读 docs/runtime-capabilities.md，复用 RuntimeFeature、CapabilitySupport 与现有缓存／重检约定，不重做 01、02。公开发行版本、上游主线、CLI 和 C# SDK 的能力要分别确认。
保持改动聚焦，复用已经合入的能力检测、任务、取消、错误提示与刷新机制。只做本任务需要的接口增量，不重构无关工作区，不批量格式化共享文件。SDK 集成以官方 C# 参考和实际 NuGet 包／版本源码共同核对。

前置条件：01、02、07 及当前已整合功能；先确认发行版 CLI 的原生重启入口已可用。

执行分组：暂缓投放。入口公开发行并验证可用后，作为独立任务执行。

## 执行前置检查

2026-09-12 的调研确认：2.9.11 只有运行时层重启支持，该版本 ContainerCommand.cpp 尚未注册 restart；CLI PR #41435 在该版本发布后才合入，C# 投影也没有对应方法。
请重新检查当前已发行版本、本机 --help 和实际 SDK。若公开可用入口仍不存在，本任务只记录版本与缺失证据，列出可重试条件，不调用内部 COM、不自行构建或安装未发行 WSL；最终状态应为“前置条件未满足”，不能标为实现完成。

## 入口可用后的实现要求

1. 在 IContainerRuntime 现有重启操作后接入原生 restart，按 02 的能力约定选择路径；核对该命令实际使用的 timeout／signal 参数，不假定与 stop 的参数名称一致。
2. 正确处理原生运行时提供的重启原子性和自动删除容器行为。对旧版保留经过验证的兼容路径；若 stop → start 会让 --rm 容器消失，必须识别并明确拒绝该不安全回退。
3. 新命令执行失败不能一律再调用 stop → start，避免重复执行和掩盖真实错误。只有已确认不支持的接口才走兼容路径。
4. 保留现有重启按钮，更新任务、取消与完成后状态刷新。此任务不增加 always／on-failure 等自动重启策略。

## 本任务重点验证

能力选择、原生命令参数、普通／停止／自动删除容器、容器在操作中被删除、取消、运行时失败及旧版回退。真实操作仅使用可销毁测试容器。

## 验证与交付

- 增加针对本任务行为和失败路径的回归测试，并运行 dotnet build ExWSLC.sln、dotnet test ExWSLC.sln 和 git diff --check。
- UI 改动检查中英文、系统／浅色／深色主题，以及现有窗口尺寸下的布局，提供前后截图；运行或截图确实受环境限制时，明确列出未验证项，不能把源码／XAML 检查当作运行验收。
- 本任务允许在测试入口显式启用 opt-in 后创建隔离的 exwslc-* 测试资源，名称包含任务编号与唯一后缀；只操作这些测试资源，并在成功、失败和取消后清理。不得修改已有用户资源，不用全局 prune 或 shutdown 清理，也不自动修改原生 WSLC 设置或升级本机 WSL。
- 将本任务结果记录在 docs/validation/wslc-11.md，包含 Windows／WSL／CLI／SDK 版本、实际命令、结果、截图位置、未验证项和涉及的共享接口；不要写入凭据或敏感运行数据。
- 仅暂存本任务相关文件，检查 git diff --cached --check，并整理为聚焦的英文 Conventional Commit。不要推送、合并其他分支或改写已有提交。
- 最终给出：工作树绝对路径、分支名、起始基线 SHA、提交 SHA、功能变化、验证结果、剩余问题，方便原会话统一审查。

## 参考资料

- [重启运行时支持](https://github.com/microsoft/WSL/pull/41454)
- [重启 CLI](https://github.com/microsoft/WSL/pull/41435)
- [2.9.11 命令注册源码](https://github.com/microsoft/WSL/blob/2.9.11/src/windows/wslc/commands/ContainerCommand.cpp)
