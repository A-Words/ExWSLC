# Changelog

All notable changes to ExWSLC will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/2.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.1] - 2026-07-31

### Added

- Added a dedicated ExWSLC application icon for the executable and title bar.

### Fixed

- Fixed a crash when opening container mount details caused by an invalid
  `SymbolIcon` style resource reference.

## [0.1.0] - 2026-07-31

### Added

- Initial release of the native Windows desktop manager for WSL Container.
- Added container creation and lifecycle management, including logs, exec sessions,
  inspect details, resource statistics, and Windows Terminal integration.
- Added image pull, build, import, load, save, tag, push, inspect, removal, and
  pruning workflows.
- Added network and named-volume creation, inspection, removal, and pruning.
- Added a Fluent WPF interface with system, light, and dark themes, plus English
  and Simplified Chinese localization.
- Added persistent application preferences, cancellable runtime tasks, registry
  login through standard input, and confirmation for destructive operations.
- Added self-contained Windows x64 and ARM64 release packages with SHA-256
  checksums.

[Unreleased]: https://github.com/A-Words/ExWSLC/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/A-Words/ExWSLC/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/A-Words/ExWSLC/releases/tag/v0.1.0
