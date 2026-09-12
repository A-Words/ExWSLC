# A：容器工作树整组实现

请在本任务已经选定的独立工作树内实现 ExWSLC 的 A 组：健康检查、动态网络管理、双向文件传输、创建／挂载／停止参数。执行顺序为 03 → 04 → 05 → 07。完成本组全部任务后再交付；每完成一项先验证并独立提交，再继续下一项。

## 起点与必读资料

1. 先检查当前绝对路径、git status、分支、HEAD 与 AGENTS.md，记录起始基线。保留用户选择的工作树，不切换到主工作树实现。
2. 公共代码基线是 f85b111，已包含 01 的 d9e321e。用 git merge-base --is-ancestor f85b111 HEAD 验证前置；当前工作树也应包含本次恢复的 docs/implementation-prompts。缺少前置时准确报告缺少内容，不自行合并其他分支。
3. 01、02 已完成，不要重新实现。先读 docs/runtime-capabilities.md；现有 SDK 包为 2.9.9，CliVersion、ServiceVersion、SdkPackageVersion 已分开。RuntimeFeature / CapabilitySupport / ReasonKey / Source 是共享能力契约，DetectAsync 有缓存，RefreshAsync 用于显式重检。
4. 按顺序完整阅读并执行以下 prompt，它们的实现、验收和交付要求都属于本组任务：

- [ 03-container-health.md ](./03-container-health.md)
- [ 04-network-operations.md ](./04-network-operations.md)
- [ 05-container-file-transfer.md ](./05-container-file-transfer.md)
- [ 07-container-create-stop-options.md ](./07-container-create-stop-options.md)

## 本组边界

A 组共享 ContainersViewModel、ContainerCreateSpec、容器详情与创建表单，必须组内串行。07 必须保留 03 的健康配置、04 的网络参数及 05 的文件传输。停止接口调整要检查已有重启与导出恢复的所有调用方。

A／B／C 可以在不同工作树同时工作，组内按上述顺序继续。不要实现其他组或后置 11、12，也不要擅自合并它们的提交。共享的 IContainerRuntime、WslcContainerRuntime、DesignContainerRuntime、双语资源和测试仅做本组必要增量，不批量重排、格式化或复制另一套能力检测。新增能力确有必要时，沿用现有三态契约并记录接口增量，不能仅凭版本号猜测支持。

工作树隔离代码，但共享本机 WSLC。运行时写入验收遵守编号 prompt 的 opt-in 约定，仅创建并操作包含任务编号与唯一后缀的 exwslc-* 测试资源，成功／失败／取消后均清理本任务创建的资源。不操作已有用户资源，不以全局 prune、shutdown 或修改原生设置清理环境。

## 验证和交接

- 遵循编号 prompt 的回归、build、test、git diff --check 与 UI 截图要求。每项分别写 docs/validation/wslc-编号.md，记录已验证和未验证边界。
- 每项仅暂存相关文件，执行 git diff --cached --check，使用独立英文 Conventional Commit；不 push、合并其他分支或改写已有提交。
- 不因完成本组第一项就结束整个任务；后续所需的本组前置提交应已经进入当前 HEAD。
- 最终返回：组别 A、工作树绝对路径、分支、起始基线 SHA、各任务提交 SHA、当前 HEAD、验证文档／截图、共享接口增量与尚未验证项。
- 完成后由原任务按 docs/implementation-prompts/99-integration-review.md 统一只读复查。分组测试通过不代表跨分支整合已经通过。
