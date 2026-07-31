# Contributing

Thank you for contributing to ExWSLC. Keep changes focused, explain the user-visible result, and validate the behavior you touch.

## Development environment

Use Windows 11 with an up-to-date WSL installation that includes `wslc.exe`. The repository's `global.json` selects the required .NET 10 SDK. Windows Terminal is only needed when testing interactive container sessions.

From the repository root, restore, build, and test the solution:

```powershell
dotnet restore ExWSLC.sln
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
```

Run the application with:

```powershell
dotnet run --project src/ExWSLC.csproj
```

If you are changing runtime compatibility, record the Windows and WSL versions and include the output of `wslc version` in your test notes.

## Making changes

- Create a focused branch from `main` and keep each change limited to one purpose.
- Follow the existing C# conventions: four-space indentation, file-scoped namespaces, nullable reference types, and implicit usings. Use `Async` suffixes for asynchronous methods.
- Keep WSLC operations behind `IContainerRuntime`. Treat actual `wslc.exe` output as the source of truth for CLI inventory and version-specific behavior. For SDK integration, consult the [official C# API reference](https://wsl.dev/api-reference/csharp/).
- Add process arguments through `ProcessStartInfo.ArgumentList`; never build shell command strings from user input.
- Preserve the Fluent Design language implemented with WPF-UI. WinUI 3 is an interaction and control reference, not an application dependency.
- Add or update user-facing text in both `src/Resources/Strings.en-US.xaml` and `src/Resources/Strings.zh-CN.xaml`.
- Do not log registry credentials, standard input, or other secrets. Persist only application preferences in `%LocalAppData%\ExWSLC\settings.json`.

WSL Container remains a preview feature. Prefer capability checks, tolerant parsing, and focused compatibility fallbacks over assumptions tied to one CLI or SDK version.

## Testing

Tests use xUnit v3 and Moq. Add focused regression coverage for parser, runtime, ViewModel, reusable-control, localization, or XAML behavior affected by your change. Name tests by observable behavior, for example `TryParse_ReturnsFalseForUnsupportedPayload`.

Live WSLC tests must be opt-in. Use disposable `exwslc-*` resources and remove every container, image, network, and volume created during the run, including after failures.

Before submitting, run:

```powershell
dotnet build ExWSLC.sln
dotnet test ExWSLC.sln
git diff --check
```

If a running ExWSLC process locks the normal build output, close it or validate with an isolated output/artifacts directory. Do not treat stale binaries as evidence for the current source.

## Commits and pull requests

Use focused [Conventional Commits](https://www.conventionalcommits.org/), such as:

```text
feat(containers): add mount details view
fix(ui): use one-way bindings
test(i18n): validate resources
```

Add notable user-visible changes to the `Unreleased` section of `CHANGELOG.md`. Pull requests should:

- Explain the user-visible change and link relevant issues.
- List the commands and results used for validation.
- Include before/after screenshots for UI changes.
- Include reproduction steps, Windows/WSL details, and `wslc version` output for runtime compatibility fixes.
- Avoid mixing unrelated formatting, refactoring, or generated files into the change.
