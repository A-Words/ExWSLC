# 01：WSLC 列表 JSON 兼容验证

验证日期：2026-09-12。任务开始基线：`eb5555e`。

## 变更范围

`WslcContainerRuntime` 中容器、镜像、网络、卷、stats 五个读取入口统一使用严格列表解析。支持：

- 旧版完整 JSON 数组，包括多行缩进数组。
- 资源名包装数组（`containers`、`images`、`networks`、`volumes`、`stats`）及 `items`、`data` 包装；键名不区分大小写。
- 单个 JSON 对象和新版每行一个对象的输出，允许行间空白。
- 命令退出成功时的空字符串、纯空白、空数组和空包装数组。

包含资源标识的对象优先作为单条记录解析，避免把 `Ports` 或未来新增的数组字段误当成清单。不是任意 JSON 对象都代表有效空列表：缺少非空资源标识、非对象记录、映射字段类型错误、损坏的 JSON 行和非零退出码均会报告异常。不会返回坏行之前的部分记录。取消结果及已取消的令牌以 `OperationCanceledException` 传播，不会清空库存。

容器保留旧版数字状态、结构化 Ports 与新版字符串字段；镜像保留数字大小和时间；stats 保留数字 PIDs。字段别名按明确顺序读取，例如 `Created` → `CreatedAt` → `CreatedSince`，空值允许回退。网络的新列表没有子网、网关时保持空值，卷的新列表没有挂载点、大小时保持空值。没有增加逐资源 Inspect 调用，也没有修改独立的 Inspect 解析器或公共 JSON helper。

`RuntimeWorkspace.RefreshAllAsync` 已在所有读取成功后统一替换集合，因此无需调整刷新架构。新增集成回归覆盖五类读取分别发生命令失败、坏 JSON 和取消时，容器、活动容器、镜像、网络、卷、stats 都保留上一份有效数据，展示诊断错误，并能在之后成功读取空清单时清空数据及错误。

## 上游依据

- [WSL PR #41280](https://github.com/microsoft/WSL/pull/41280)：容器、镜像、网络、卷列表与 stats 的 JSON 输出变为逐行对象，空结果不输出内容。
- [WSL PR #41375](https://github.com/microsoft/WSL/pull/41375)：容器列表采用 Docker 字段命名及预渲染字符串。
- [WSL 2.9.3 ContainerModel.h](https://github.com/microsoft/WSL/blob/2.9.3/src/windows/wslc/services/ContainerModel.h) 和 [ImageModel.h](https://github.com/microsoft/WSL/blob/2.9.3/src/windows/wslc/services/ImageModel.h)：旧容器 `State`、`CreatedAt`、`Ports` 及镜像数字 `Created`、`Size` 的序列化来源。

## 验证环境

| 项目 | 实测值 |
| --- | --- |
| Windows | Windows 11 25H2，10.0.26200.9445 |
| `wslc.exe version` | `wslc 2.9.10.0` |
| `wsl.exe --version` | WSL 2.9.10.0，内核 6.18.40.1-1 |
| `dotnet --version` | 10.0.401，按 `global.json` 的 10.0.300 / latestFeature 选择 |

## 自动化验证

```powershell
dotnet build ExWSLC.sln --no-restore
dotnet test ExWSLC.sln --no-build
git diff --check
```

结果：构建通过，0 警告、0 错误；完整测试 161 / 161 通过，0 跳过；diff 空白检查通过。

主要新增测试在 `tests/WslcInventoryTests.cs`，通过实际 `WslcContainerRuntime` 搭配模拟 `IProcessRunner` 验证列表格式、命令及参数、错误、取消、映射与工作区保留。原 `WslcContainerRuntimeTests.cs` 中“错误输出返回空列表”的测试由严格失败语义的回归替换。现有 Inspect、挂载、网络详情和 XAML 测试均包含在完整测试运行中。

## 本机只读 CLI 核对

以下命令全部退出码为 0，仅记录结果形状与计数，不保存库存原文、资源名称或用户路径：

| 命令 | 非空对象行数 | 关键格式 |
| --- | ---: | --- |
| `wslc.exe container list --all --no-trunc --format json` | 3 | `ID` / `Names` / `State` / `Ports` / `CreatedAt` 均为字符串；`Platform` 为对象 |
| `wslc.exe image list --no-trunc --format json` | 1 | 单对象；`ID` / `Repository` / `Tag` / `Size` / `CreatedAt` 为字符串 |
| `wslc.exe network list --format json` | 3 | `ID` / `Name` / `Driver` / `Scope` 为字符串；不包含子网和网关 |
| `wslc.exe volume list --format json` | 0 | 成功且没有 stdout |
| `wslc.exe stats --all --no-trunc --format json` | 3 | `ID` / `Name` / `CPUPerc` / `MemUsage` / `NetIO` / `BlockIO` 为字符串，`PIDs` 为数字 |

主代理随后使用引用本次构建产物的临时 C# 验证程序，实例化 `new WslcContainerRuntime(new WslcProcessRunner())`，依次调用五个真实读取入口，并与各自 CLI 输出的非空对象行数再次比较。容器 **3 = 3**、镜像 **1 = 1**、网络 **3 = 3**、卷 **0 = 0**、stats **3 = 3**，均一致，程序退出码为 0。这覆盖真实进程启动、输出读取及新解析器的完整读取路径；比较仅输出计数，不持久化库存原文。

## 验证边界

本机检查的是 2.9.10.0；2.9.3 的兼容性依靠版本源码和合成回归样例，尚未在 2.9.3 或 2.9.11 安装实例上执行。原始 CLI 和实际 Runtime 路径均完成只读核对；没有执行 WPF 人工交互验收。真实卷列表为空，非空卷字段由合成样例覆盖。没有创建、停止、删除资源，也没有更新 WSL 或 SDK。
