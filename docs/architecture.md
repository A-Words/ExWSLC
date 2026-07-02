# Architecture

## Runtime boundary

The application targets `net8.0-windows10.0.19041.0` and follows a WPF/MVVM structure:

```text
WPF views → MainViewModel → IContainerRuntime → wslc.exe
                         ↘ ITaskService
                         ↘ ISettingsService
Startup → IRuntimeCapabilityService → Microsoft.WSL.Containers.WslcService
```

`wslc.exe --format json` is the source of truth for inventory because the preview C# projection can create containers but cannot enumerate and reopen every existing CLI container. `Microsoft.WSL.Containers` is used for prerequisite detection, version reporting, and user-initiated dependency installation. `IContainerRuntime` allows a native API provider to replace the CLI provider after the API reaches general availability.

## Safety and compatibility

- Every process argument is added through `ProcessStartInfo.ArgumentList`; user input is never concatenated into a shell command.
- Registry passwords use standard input and are excluded from command displays, task logs, and settings.
- List parsers accept direct arrays or wrapped arrays, match known fields case-insensitively, and ignore unknown preview fields.
- Destructive operations require an explicit UI confirmation. Long operations can cancel the complete child process tree.
- App preferences are the only persistent application data. Runtime inventory always comes from WSLC.

## UI composition

The single Fluent window contains overview, containers, images, networks/volumes, tasks, and settings workspaces. Dynamic resource dictionaries provide `zh-CN` and `en-US`; WPF-UI applies system, light, or dark themes. The integrated output panel handles inspect, logs, and one-shot exec. Interactive TTY sessions intentionally open in Windows Terminal rather than embedding a terminal emulator.

## Testing

Unit tests cover argument mapping, injection boundaries, preview JSON compatibility, cancellation, secret redaction, settings persistence, task state, and ViewModel recovery. Live acceptance uses disposable `exwslc-*` resources and must remove them after the run.
