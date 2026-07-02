# Contributing

1. Use Windows 11, .NET 8, and a current WSL release containing `wslc.exe`.
2. Create a focused branch and keep runtime operations behind `IContainerRuntime`.
3. Do not log registry credentials, raw standard input, or other secrets.
4. Run `dotnet build ExWSLC.sln`, `dotnet test ExWSLC.sln`, and `git diff --check` before submitting a change.
5. Mark tests that mutate the live WSLC runtime as opt-in integration tests and clean every created container, image, network, and volume.

WSL Container is a preview feature. Prefer tolerant parsing and capability checks over assumptions tied to one CLI version. User-facing strings belong in both resource dictionaries.
