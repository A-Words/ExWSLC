# Repository Guidelines

## Project Structure & Module Organization

`ExWSLC.sln` contains the WPF application in `src/` and the xUnit test project in `tests/`. Application code is grouped by responsibility: `Views/`, `ViewModels/`, `Models/`, `Services/`, `Helpers/`, and reusable `Controls/`. Store images in `src/Assets/`, themes in `src/Themes/`, and user-facing text in both `src/Resources/Strings.en-US.xaml` and `Strings.zh-CN.xaml`. Architecture notes belong in `docs/`. Keep WSLC operations behind `IContainerRuntime` so preview-runtime changes do not leak into the UI. When changing `Microsoft.WSL.Containers` SDK integration, consult the [official C# API reference](https://wsl.dev/api-reference/csharp/). Continue treating actual `wslc.exe` output and version-specific behavior as the source of truth for CLI-backed inventory.

## Build, Test, and Development Commands

Use Windows 11 with an up-to-date WSL installation containing `wslc.exe`. `global.json` selects the .NET 10 SDK.

```powershell
dotnet restore ExWSLC.sln
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
dotnet run --project src/ExWSLC.csproj
```

They restore, build, test, and launch the app. For a release package, run `dotnet publish src/ExWSLC.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64`.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, file-scoped namespaces, nullable reference types, and implicit usings. Use PascalCase for types, methods, properties, and XAML resource keys; camelCase for parameters and locals; `_camelCase` for private fields; `I` prefixes for interfaces; and `Async` suffixes for asynchronous methods. Use CommunityToolkit.Mvvm attributes and match surrounding XAML formatting. Preserve the Windows 11 Fluent Design language implemented with WPF-UI. Use WinUI 3 controls and interaction patterns as references without adding WinUI dependencies. Never replace `ProcessStartInfo.ArgumentList` with shell-command concatenation.

## Testing Guidelines

Tests use xUnit v3, Moq, and coverlet. Name files `FeatureTests.cs` and methods by behavior, such as `TryParse_ReturnsFalseForUnsupportedPayload`. Add focused regression tests for parser, runtime, ViewModel, or XAML changes. No numeric coverage threshold is configured; protect changed behavior and failure paths. Live WSLC tests must be opt-in and remove every resource they create.

## Commit & Pull Request Guidelines

Follow the repository’s Conventional Commit history: `feat(containers): add mount details view`, `fix(ui): use one-way bindings`, or `test(i18n): validate resources`. Keep commits focused. Pull requests should explain the user-visible change, link relevant issues, and list validation performed. Include before/after screenshots for UI changes. For runtime compatibility fixes, include Windows/WSL details, `wslc version`, and reproduction steps. Before submission, run the build, tests, and `git diff --check`.

## Security & Configuration

Do not log registry credentials, standard input, or other secrets. Persist only application preferences in `%LocalAppData%\ExWSLC\settings.json`; runtime inventory remains owned by WSLC.
