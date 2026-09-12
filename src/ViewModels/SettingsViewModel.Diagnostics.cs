using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial RuntimeDiagnostics? Diagnostics { get; set; }
    [ObservableProperty] public partial string DiagnosticsActionStatus { get; set; } = string.Empty;
    private string _diagnosticsActionKey = string.Empty;

    public IReadOnlyList<DiagnosticField> DiagnosticsFields => Diagnostics is { } snapshot ? RuntimeDiagnosticsFormatter.Fields(snapshot) : [];
    public IReadOnlyList<DiagnosticField> DiagnosticsSessions => Diagnostics?.SystemInfo?.Sessions?.Select(session => new DiagnosticField(
        RuntimeDiagnosticsFormatter.Display(session.Name),
        $"ID: {RuntimeDiagnosticsFormatter.Display(session.Id?.ToString())}  ·  " +
        $"{LocalizationService.GetString("DiagnosticsCreatorPid", "Creator PID")}: {RuntimeDiagnosticsFormatter.Display(session.CreatorPid?.ToString())}")).ToArray() ?? [];
    public string DiagnosticsStatus => Diagnostics is { } snapshot
        ? RuntimeDiagnosticsFormatter.Status(snapshot)
        : LocalizationService.GetString("DiagnosticsNotCollected", "Refresh to collect diagnostics.");
    public string DiagnosticsSessionsStatus => Diagnostics is { } snapshot ? RuntimeDiagnosticsFormatter.SessionsStatus(snapshot) : string.Empty;
    public bool CanRefreshDiagnostics => !Workspace.IsBusy;
    public bool CanShareDiagnostics => Diagnostics is not null && !RefreshDiagnosticsCommand.IsRunning;

    partial void OnDiagnosticsChanged(RuntimeDiagnostics? value) => RaiseDiagnosticsChanged();

    private void RaiseDiagnosticsChanged()
    {
        OnPropertyChanged(nameof(DiagnosticsFields));
        OnPropertyChanged(nameof(DiagnosticsSessions));
        OnPropertyChanged(nameof(DiagnosticsStatus));
        OnPropertyChanged(nameof(DiagnosticsSessionsStatus));
        OnPropertyChanged(nameof(CanShareDiagnostics));
        CopyDiagnosticsCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
        DiagnosticsActionStatus = string.IsNullOrEmpty(_diagnosticsActionKey) ? string.Empty : LocalizationService.GetString(_diagnosticsActionKey, _diagnosticsActionKey);
    }

    private void SetDiagnosticsActionStatus(string key)
    {
        _diagnosticsActionKey = key;
        DiagnosticsActionStatus = LocalizationService.GetString(key, key);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshDiagnostics))]
    private async Task RefreshDiagnosticsAsync()
    {
        if (Workspace.IsBusy) return;
        SetDiagnosticsActionStatus(string.Empty);
        RaiseDiagnosticsChanged();
        try
        {
            await Workspace.RunTrackedAsync(LocalizationService.GetString("DiagnosticsRefresh", "Refresh diagnostics"), async (_, token) =>
            {
                // Reuse the capability service's cached version/help probes. A failed probe
                // must not prevent the independently accessible diagnostic query.
                var capabilities = Workspace.Capabilities;
                try { capabilities = await Workspace.GetCapabilitiesAsync(token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
                RuntimeDiagnostics snapshot;
                try { snapshot = await Workspace.Runtime.GetSystemInfoAsync(capabilities, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    snapshot = new RuntimeDiagnostics
                    {
                        StatusKey = "DiagnosticsQueryFailed",
                        BasicCliVersion = RuntimeSystemInfoParser.SafeVersion(capabilities.CliVersion),
                        BasicServiceVersion = RuntimeSystemInfoParser.SafeVersion(capabilities.ServiceVersion),
                        SdkPackageVersion = RuntimeSystemInfoParser.SafeVersion(capabilities.SdkPackageVersion)
                    };
                }
                token.ThrowIfCancellationRequested();
                Diagnostics = snapshot;
                var success = snapshot.StatusKey == "DiagnosticsCollected";
                return new OperationResult(success, snapshot.ExitCode ?? (success ? 0 : -1), string.Empty,
                    success ? string.Empty : RuntimeDiagnosticsFormatter.Status(snapshot), "wslc system info --format json");
            });
        }
        catch (OperationCanceledException)
        {
            // Keep the previous timestamp and values rather than publishing a cancelled sample.
            SetDiagnosticsActionStatus("DiagnosticsCancelled");
        }
    }

    [RelayCommand(CanExecute = nameof(CanShareDiagnostics))]
    private void CopyDiagnostics()
    {
        if (Diagnostics is not { } snapshot) return;
        try
        {
            Workspace.Interaction.SetClipboardText(RuntimeDiagnosticsFormatter.Summary(snapshot));
            SetDiagnosticsActionStatus("DiagnosticsCopied");
        }
        catch (Exception) { SetDiagnosticsActionStatus("DiagnosticsCopyFailed"); }
    }

    [RelayCommand(CanExecute = nameof(CanShareDiagnostics))]
    private async Task ExportDiagnosticsAsync()
    {
        if (Diagnostics is not { } snapshot) return;
        try
        {
            var path = Workspace.Interaction.PickSaveFile(
                LocalizationService.GetString("DiagnosticsExport", "Export diagnostics"),
                LocalizationService.GetString("DiagnosticsTextFilter", "Text files (*.txt)|*.txt"), "exwslc-diagnostics.txt");
            if (path is null) return;
            await File.WriteAllTextAsync(path, RuntimeDiagnosticsFormatter.Summary(snapshot), Workspace.Lifetime.Token);
            SetDiagnosticsActionStatus("DiagnosticsExported");
        }
        catch (OperationCanceledException) { SetDiagnosticsActionStatus("DiagnosticsCancelled"); }
        catch (Exception) { SetDiagnosticsActionStatus("DiagnosticsExportFailed"); }
    }
}
