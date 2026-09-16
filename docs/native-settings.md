# WSLC 图形化设置

设置页的「WSLC 设置」提供表单控件，不需要编辑 YAML。每项可以独立勾选「使用 WSLC 默认值」，修改后点击「保存 WSLC 配置」。

| 设置 | 控件 / 输入格式 |
| --- | --- |
| 虚拟 CPU 数量 | 正整数步进输入 |
| 内存上限、磁盘容量上限 | 整数容量，例如 4096MB、4GB、1TB |
| 发布端口的默认绑定地址 | IPv4 地址 |
| 宿主机回环名称 | 域名，或 none 禁用 |
| 空闲会话超时 | 秒数步进输入 |
| 会话存储目录 | Windows 绝对路径 |
| 凭据存储后端 | wincred / file 下拉框 |
| 网络模式（高级） | consomme / nat / none 下拉框 |
| 宿主机文件共享（高级） | virtiofs / plan9 下拉框 |
| DNS 隧道（高级） | 开关 |
| 端口转发方式（实验性） | virtionet / wslrelay 下拉框 |

配置项依据 [Microsoft WSLC 配置实现](https://github.com/microsoft/WSL/blob/master/src/windows/common/WSLCUserSettings.cpp)。高级选项和新增选项需要所安装的 WSLC 版本支持；表单不会将保存成功宣称为运行时已应用。会话设置供新会话使用，不会自动停止现有会话。改变存储路径不会迁移已有镜像、容器或磁盘，会在新位置创建空会话。

底层通过 `IContainerRuntime` 读取和保存当前用户的 `%LOCALAPPDATA%\wslc\settings.yaml`。应用的 `settings.json` 不保存这些配置。文件不存在时表单显示默认配置，仅在修改并保存后创建文件。

保存只更新修改过的字段，保留原有注释、未修改值及未知字段。已有值超出表单格式时显示提示，不修改该项就会原样保留。输入校验防止保存非法容量、数值、IPv4 地址和路径。YAML 限制为单个映射文档、64K 字符、32 层嵌套，拒绝重复键、别名和合并键。文件结构无法解析时不会覆盖。

保存前检查外部编辑冲突，使用同目录临时文件及替换操作，原文件保留为 `settings.yaml.exwslc.bak`。检查与替换之间仍有很短的竞争窗口，不支持多个程序同时写同一文件。重新读取有未保存修改时会确认丢弃，保存失败保留表单。保存或重置成功后清除旧宿主机回环诊断。

测试使用独立临时目录，不修改真实 WSLC 配置。`NativeSettingsFormTests` 覆盖控件模型、校验及保留原文的字段更新；`SettingsNativeConfigurationViewModelTests` 覆盖保存、冲突及重读；`NativeSettingsUiTests` 单独设置 `EXWSLC_UI_SETTINGS_OUTPUT` 后验证实际 WPF 控件绑定，输出中英文及浅深主题离屏预览，不能替代桌面验收。
