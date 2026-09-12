using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial HostLoopbackConfiguration? HostLoopbackConfiguration { get; set; }
    [ObservableProperty] public partial ContainerSummary? HostProbeContainer { get; set; }
    [ObservableProperty] public partial string HostProbePort { get; set; } = string.Empty;
    [ObservableProperty] public partial HostLoopbackProbeResult? HostProbeResult { get; set; }
    private string _hostProbeCopyStatusKey = string.Empty;

    public string HostLoopbackHost => RuntimeDiagnosticsFormatter.Display(HostLoopbackConfiguration?.HostName);
    public string HostLoopbackConfigStatus => HostLoopbackDiagnosticsFormatter.Text(HostLoopbackConfiguration?.ReasonKey ?? "HostConfigNotRead");
    public string HostProbeValidation => TryGetHostProbePort(out _) ? string.Empty : HostLoopbackDiagnosticsFormatter.Text("HostProbePortValidation");
    public string HostProbeSummary => HostProbeResult is { } result ? HostLoopbackDiagnosticsFormatter.Summary(result) : string.Empty;
    public string HostProbeCopyStatus => string.IsNullOrEmpty(_hostProbeCopyStatusKey) ? string.Empty : HostLoopbackDiagnosticsFormatter.Text(_hostProbeCopyStatusKey);
    public bool CanReadHostLoopback => !Workspace.IsBusy && !ProbeHostLoopbackCommand.IsRunning;
    public bool CanProbeHostLoopback => CanReadHostLoopback && !ReadHostLoopbackCommand.IsRunning && HostProbeContainer?.IsRunning == true &&
        HostLoopbackConfiguration is { HostName.Length: > 0 } && TryGetHostProbePort(out _);
    public bool CanCopyHostProbe => HostProbeResult is not null && !ProbeHostLoopbackCommand.IsRunning;

    private bool TryGetHostProbePort(out int port) => int.TryParse(HostProbePort, NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
        HostLoopbackTargetValidator.IsPort(port);

    partial void OnHostLoopbackConfigurationChanged(HostLoopbackConfiguration? value) => RaiseHostLoopbackChanged();
    partial void OnHostProbeContainerChanged(ContainerSummary? value) => RaiseHostLoopbackChanged();
    partial void OnHostProbePortChanged(string value) => RaiseHostLoopbackChanged();
    partial void OnHostProbeResultChanged(HostLoopbackProbeResult? value) => RaiseHostLoopbackChanged();

    private void RaiseHostLoopbackChanged()
    {
        OnPropertyChanged(nameof(HostLoopbackHost));
        OnPropertyChanged(nameof(HostLoopbackConfigStatus));
        OnPropertyChanged(nameof(HostProbeValidation));
        OnPropertyChanged(nameof(HostProbeSummary));
        OnPropertyChanged(nameof(HostProbeCopyStatus));
        OnPropertyChanged(nameof(CanProbeHostLoopback));
        OnPropertyChanged(nameof(CanCopyHostProbe));
        OnPropertyChanged(nameof(CanReadHostLoopback));
        ProbeHostLoopbackCommand.NotifyCanExecuteChanged();
        CopyHostProbeCommand.NotifyCanExecuteChanged();
        ReadHostLoopbackCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanReadHostLoopback))]
    private async Task ReadHostLoopbackAsync()
    {
        try { HostLoopbackConfiguration = await Workspace.Runtime.GetHostLoopbackConfigurationAsync(Workspace.Lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception) { HostLoopbackConfiguration = Models.HostLoopbackConfiguration.Unknown; }
    }

    [RelayCommand(CanExecute = nameof(CanProbeHostLoopback))]
    private async Task ProbeHostLoopbackAsync()
    {
        // Capture before any await. Editing the form cannot retarget an in-flight probe.
        if (Workspace.IsBusy || HostProbeContainer is not { IsRunning: true } container ||
            HostLoopbackConfiguration is not { HostName.Length: > 0 } configuration || !TryGetHostProbePort(out var port)) return;
        var target = new HostLoopbackProbeRequest(container.Id, configuration.HostName, port);
        var started = DateTimeOffset.UtcNow;
        var capabilities = Workspace.Capabilities;
        _hostProbeCopyStatusKey = string.Empty;
        HostProbeResult = null;
        try
        {
            await Workspace.RunTrackedAsync(HostLoopbackDiagnosticsFormatter.Text("HostProbeRun"), async (_, token) =>
            {
                try { HostProbeResult = await Workspace.Runtime.ProbeHostLoopbackAsync(target, capabilities, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    HostProbeResult = new(target, started, HostLoopbackOutcome.RuntimeFailed, false, capabilities.CliVersion);
                }
                if (HostProbeResult.Outcome == HostLoopbackOutcome.Cancelled) throw new OperationCanceledException(token);
                var success = HostProbeResult.Outcome == HostLoopbackOutcome.Connected;
                // Only fixed status messages reach task history, never raw command output.
                return new OperationResult(success, success ? 0 : -1, string.Empty,
                    success ? string.Empty : HostLoopbackDiagnosticsFormatter.Outcome(HostProbeResult), "host-loopback probe");
            });
        }
        catch (OperationCanceledException)
        {
            HostProbeResult = new(target, started, HostLoopbackOutcome.Cancelled, HostProbeResult?.DnsSucceeded ?? false, capabilities.CliVersion);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyHostProbe))]
    private void CopyHostProbe()
    {
        try
        {
            Workspace.Interaction.SetClipboardText(HostProbeSummary);
            _hostProbeCopyStatusKey = "DiagnosticsCopied";
        }
        catch (Exception) { _hostProbeCopyStatusKey = "DiagnosticsCopyFailed"; }
        OnPropertyChanged(nameof(HostProbeCopyStatus));
    }
}
