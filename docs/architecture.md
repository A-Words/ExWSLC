# Architecture

## Application structure

ExWSLC targets `net10.0-windows10.0.19041.0` and uses WPF, WPF-UI 4.3, CommunityToolkit.Mvvm, and Microsoft.Extensions.DependencyInjection.

```text
WPF pages → Page ViewModels → RuntimeWorkspace → IContainerRuntime → wslc.exe
                                      ├── ITaskService
                                      ├── ISettingsService
                                      └── IUserInteractionService

Startup → IRuntimeCapabilityService → Microsoft.WSL.Containers.WslcService
```

`App` registers the runtime and application services as singletons. `MainViewModel` owns one ViewModel for each navigation page, while `AppPageProvider` constructs the corresponding page with that ViewModel. Containers, images, networks, volumes, capabilities, task state, and application-wide status are shared through the singleton `RuntimeWorkspace`. Resource-specific ViewModels keep selection, filtering, commands, and detail-loading behavior close to their pages.

## Runtime boundary

`IContainerRuntime` is the boundary between the UI and WSL Container operations. Its current implementation, `WslcContainerRuntime`, translates typed application requests into `wslc.exe` argument lists and parses CLI output into application models.

`wslc.exe --format json` is the source of truth for container, image, network, volume, and statistics inventory. The preview `Microsoft.WSL.Containers` SDK is limited to prerequisite detection, version reporting, and user-initiated installation of missing components. Keeping both integrations behind service interfaces allows version-specific behavior to change without leaking into the views and leaves room for a fuller native provider after the API stabilizes.

`RuntimeWorkspace` performs the initial capability check, refreshes all inventory in parallel, and runs periodic refreshes using the configured interval. Long-running mutations are passed through `ITaskService`, which records state and progress and exposes cancellation through the workspace. Page ViewModels use the workspace refresh event and shared collections while maintaining their own selection and on-demand detail caches.

## UI composition

The single Fluent window navigates directly to Containers, Images, Networks, Volumes, and Settings; Containers is the startup page. Overview and Tasks are not separate navigation destinations.

- Containers use a master-detail layout. The detail view exposes logs, live resource usage, network data, mounts, configuration, and raw inspect output. Network and mount details are loaded on demand and cached per selected container.
- Images, networks, and volumes use dedicated resource pages with shared Fluent toolbar, table, empty-state, and dialog patterns. Network inspection is shown on the network page; the volume page intentionally keeps a narrower create, remove, and prune workflow.
- Settings presents runtime capabilities, native WSLC maintenance, appearance preferences, refresh interval, and registry login.
- Interactive TTY sessions open in Windows Terminal rather than embedding a terminal emulator.

Dynamic resource dictionaries provide `en-US` and `zh-CN` text. `LocalizationService` applies language and system, light, or dark themes at runtime. WPF-UI supplies the Fluent shell, Mica backdrop, dialogs, cards, data grids, and other controls; WinUI 3 is a design and interaction reference rather than an application dependency.

## Safety and compatibility

- Every process argument is added through `ProcessStartInfo.ArgumentList`; user input is never concatenated into a shell command.
- Registry passwords are sent through standard input and excluded from command displays, task details, and settings.
- List parsers accept direct or wrapped arrays, match known fields case-insensitively, and ignore unknown preview fields.
- Destructive operations require explicit UI confirmation. Cancellation terminates the complete child process tree.
- Only application preferences are persisted under `%LocalAppData%\ExWSLC\settings.json`; runtime inventory remains owned by WSLC.

## Testing

The xUnit test project covers CLI argument mapping and injection boundaries, preview JSON compatibility, cancellation and task state, secret redaction, settings persistence, ViewModel refresh and selection behavior, and container inspect/network/mount detail loading. STA and source-level XAML tests protect reusable controls, bindings, localization resources, accessibility metadata, and the current navigation structure.

Live WSLC tests must be opt-in, use disposable `exwslc-*` resources, and remove every resource they create.
