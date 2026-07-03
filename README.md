# ExWSLC

ExWSLC is a native Windows desktop manager for [WSL Container](https://learn.microsoft.com/windows/wsl/wsl-container), the container runtime built into current WSL releases. It manages Linux containers, images, networks, volumes, registries, logs, exec sessions, and resource statistics without requiring Docker Desktop.

> WSL Container and its SDK are currently in preview. ExWSLC checks the installed CLI and SDK at startup and isolates preview-specific behavior behind a runtime interface.

## Features

- Fluent WPF desktop UI with Mica, system/light/dark themes, English and Simplified Chinese.
- Container creation with CPU, memory, GPU, ports, environment, network, user, workdir, and volume options.
- Start, graceful stop, force stop, restart, remove, export, inspect, logs, live log following, one-shot exec, and Windows Terminal access.
- Pull, build, import, load, save, tag, push, inspect, remove, and prune images.
- Create, inspect, remove, and prune networks and named volumes.
- Live `wslc stats`, task history and cancellation, registry login through `--password-stdin`, and native WSLC settings access.

The navigation hierarchy and Fluent master-detail composition are inspired by
[ExHyperV](https://github.com/Justsenger/ExHyperV). ExWSLC's views, resources,
and product assets are implemented from scratch for the WSL Container workflow.

## Requirements

- Windows 11 with WSL installed and updated to a release that includes `wslc.exe` (tested with WSL 2.9.3).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source.
- Windows Terminal is optional and only required for interactive container terminals.

Run `wsl --update` and `wslc version` if the application reports missing components.

## Build and run

```powershell
dotnet restore ExWSLC.sln
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
dotnet run --project src/ExWSLC.csproj
```

Publish a self-contained package:

```powershell
dotnet publish src/ExWSLC.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

Settings are stored at `%LocalAppData%\ExWSLC\settings.json`. Passwords and tokens are never stored by ExWSLC; they are sent to `wslc registry login` through standard input.

## Architecture and contributing

See [docs/architecture.md](docs/architecture.md) for runtime boundaries and preview compatibility decisions. Contributions are welcome under [CONTRIBUTING.md](CONTRIBUTING.md).

## 中文说明

ExWSLC 是面向微软原生 WSL Container 的 Windows 桌面管理器，不依赖 Docker Desktop。它提供容器、镜像、网络、卷、日志、资源统计、注册表和任务管理，并支持中英双语与深浅色主题。

当前 WSL Container 仍处于预览阶段。请先通过 `wsl --update` 更新 WSL，并用 `wslc version` 确认功能可用。构建、测试和发布命令与上文一致。

## License

Copyright © 2026 ExWSLC contributors. Licensed under the [GNU General Public License v3.0](LICENSE).
