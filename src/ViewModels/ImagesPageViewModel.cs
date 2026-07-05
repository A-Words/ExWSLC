using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ExWSLC.Models;

namespace ExWSLC.ViewModels;

public partial class ImagesPageViewModel : ObservableObject
{
    public ImagesPageViewModel(RuntimeWorkspace workspace)
    {
        Workspace = workspace;
        Workspace.Refreshed += (_, _) => ApplyImageFilter();
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public RuntimeWorkspace Workspace { get; }
    public ObservableCollection<ImageSummary> VisibleImages { get; } = [];

    [ObservableProperty] public partial string ImageSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ImageSummary? SelectedImage { get; set; }
    [ObservableProperty] public partial string ImageReference { get; set; } = "ubuntu:latest";
    [ObservableProperty] public partial string ImagePath { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImageTag { get; set; } = string.Empty;
    [ObservableProperty] public partial string DockerfilePath { get; set; } = string.Empty;

    public string DetailOutput { get => Workspace.DetailOutput; set => Workspace.DetailOutput = value; }
    public bool HasSelectedImage => SelectedImage is not null;
    public string SelectedImageDisplayName => SelectedImage?.DisplayName ??
        (Workspace.SettingsService.Current.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "Select an image" : "请选择镜像");
    public IAsyncRelayCommand RefreshAllCommand => Workspace.RefreshAllCommand;

    [RelayCommand]
    private async Task PullImageAsync()
    {
        if (string.IsNullOrWhiteSpace(ImageReference)) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync($"Pull {ImageReference}", (progress, token) => Workspace.Runtime.PullImageAsync(ImageReference, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task BuildImageAsync()
    {
        var folder = string.IsNullOrWhiteSpace(ImagePath) ? Workspace.Interaction.PickFolder("Choose build context") : ImagePath;
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(ImageTag)) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync($"Build {ImageTag}", (progress, token) => Workspace.Runtime.BuildImageAsync(folder, ImageTag, DockerfilePath, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ImagePath) ? Workspace.Interaction.PickOpenFile("Import image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ImagePath;
        if (path is null || string.IsNullOrWhiteSpace(ImageTag)) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync("Import image", (progress, token) => Workspace.Runtime.ImportImageAsync(path, ImageTag, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task LoadImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ImagePath) ? Workspace.Interaction.PickOpenFile("Load image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ImagePath;
        if (path is null) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync("Load image", (progress, token) => Workspace.Runtime.LoadImageAsync(path, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task SaveImageAsync()
    {
        var image = SelectedImage?.DisplayName ?? ImageReference;
        if (string.IsNullOrWhiteSpace(image)) return;
        var path = Workspace.Interaction.PickSaveFile("Save image", "Tar archive (*.tar)|*.tar", image.Replace('/', '_').Replace(':', '_') + ".tar");
        if (path is null) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync("Save image", (progress, token) => Workspace.Runtime.SaveImageAsync(image, path, progress, token)));
    }

    [RelayCommand]
    private async Task TagImageAsync()
    {
        if (SelectedImage is null || string.IsNullOrWhiteSpace(ImageTag)) return;
        Workspace.ShowResult(await Workspace.Runtime.TagImageAsync(SelectedImage.DisplayName, ImageTag, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task PushImageAsync()
    {
        var image = SelectedImage?.DisplayName ?? ImageReference;
        if (string.IsNullOrWhiteSpace(image)) return;
        Workspace.ShowResult(await Workspace.RunTrackedAsync("Push image", (progress, token) => Workspace.Runtime.PushImageAsync(image, progress, token)));
    }

    [RelayCommand]
    private async Task RemoveImageAsync()
    {
        if (SelectedImage is null || !Workspace.Interaction.Confirm("Remove image", $"Permanently remove {SelectedImage.DisplayName}?")) return;
        Workspace.ShowResult(await Workspace.Runtime.RemoveImageAsync(SelectedImage.DisplayName, true, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectImageAsync()
    {
        if (SelectedImage is null) return;
        Workspace.ShowResult(await Workspace.Runtime.InspectImageAsync(SelectedImage.DisplayName, Workspace.Lifetime.Token));
    }

    [RelayCommand]
    private async Task PruneAsync(string? resource)
    {
        if (resource is not "image") return;
        if (!Workspace.Interaction.Confirm("Prune resources", "Remove every unused image resource?")) return;
        Workspace.ShowResult(await Workspace.Runtime.PruneAsync(resource, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    partial void OnImageSearchTextChanged(string value) => ApplyImageFilter();

    partial void OnSelectedImageChanged(ImageSummary? value)
    {
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(SelectedImageDisplayName));
    }

    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(SelectedImageDisplayName));

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(RuntimeWorkspace.DetailOutput))
        {
            OnPropertyChanged(nameof(DetailOutput));
        }
    }

    private void ApplyImageFilter()
    {
        var query = ImageSearchText.Trim();
        RuntimeWorkspace.Replace(VisibleImages, Workspace.Images.Where(image => string.IsNullOrEmpty(query) ||
            image.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            image.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
}
