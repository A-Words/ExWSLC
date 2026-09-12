using CommunityToolkit.Mvvm.ComponentModel;
using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainerStopOptionsViewModel(string containerName, RuntimeCapabilities capabilities) : ObservableObject
{
    public string ContainerName { get; } = containerName;
    public bool CanSetTimeout => capabilities[RuntimeFeature.StopTimeout].Support == CapabilitySupport.Supported;
    public bool CanSetSignal => capabilities[RuntimeFeature.StopSignal].Support == CapabilitySupport.Supported;
    [ObservableProperty] public partial string Timeout { get; set; } = string.Empty;
    [ObservableProperty] public partial string Signal { get; set; } = string.Empty;
    [ObservableProperty] public partial string Error { get; set; } = string.Empty;
    public bool IsValid => Error.Length == 0;
    public ContainerStopOptions Build() => new(ContainerLaunchOptions.ParseTimeout(Timeout), string.IsNullOrWhiteSpace(Signal) ? null : Signal.Trim());
    partial void OnTimeoutChanged(string value) => Validate();
    partial void OnSignalChanged(string value) => Validate();
    private void Validate()
    {
        try { ContainerLaunchOptions.ValidateStop(Build()); Error = string.Empty; }
        catch (ArgumentException exception) { Error = exception.Message; }
        OnPropertyChanged(nameof(IsValid));
    }
}
