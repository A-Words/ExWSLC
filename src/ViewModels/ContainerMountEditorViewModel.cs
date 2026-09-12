using CommunityToolkit.Mvvm.ComponentModel;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ContainerMountEditorViewModel : ObservableObject
{
    [ObservableProperty] public partial int KindIndex { get; set; }
    [ObservableProperty] public partial string Source { get; set; } = string.Empty;
    [ObservableProperty] public partial string Target { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ReadOnly { get; set; }
    public bool HasSource => KindIndex != 2;
    partial void OnKindIndexChanged(int value) => OnPropertyChanged(nameof(HasSource));
    public ContainerMountSpec Build() => new(KindIndex switch { 0 => ContainerMountKind.Bind, 1 => ContainerMountKind.Volume, 2 => ContainerMountKind.Tmpfs, _ => ContainerMountKind.Unknown }, HasSource ? Source : string.Empty, Target, ReadOnly);
}
