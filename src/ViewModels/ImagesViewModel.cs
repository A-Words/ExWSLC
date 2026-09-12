using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels.Messages;

namespace ExWSLC.ViewModels;

public partial class ImagesViewModel : WorkspaceViewModel
{
    private const string DefaultImageReference = "ubuntu:latest";
    private string? _inspectedImageKey;

    public ImagesViewModel(RuntimeWorkspace workspace) : base(workspace)
    {
        Workspace.Refreshed += (_, _) => RefreshVisibleImages();
        WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (recipient, message) => ((ImagesViewModel)recipient).RaiseLanguageChanged());
    }
    public ObservableCollection<ImageSummary> VisibleImages { get; } = [];

    [ObservableProperty] public partial string ImageSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial ImageSummary? SelectedImage { get; set; }
    [ObservableProperty] public partial string ImageReference { get; set; } = DefaultImageReference;
    [ObservableProperty] public partial string BuildContextPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildImageTag { get; set; } = string.Empty;
    [ObservableProperty] public partial string ArchivePath { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImportImageName { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImageTag { get; set; } = string.Empty;
    [ObservableProperty] public partial string DockerfilePath { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildArgumentsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildTarget { get; set; } = string.Empty;
    [ObservableProperty] public partial bool BuildNoCache { get; set; }
    [ObservableProperty] public partial bool BuildPull { get; set; }
    [ObservableProperty] public partial int BuildOutputMode { get; set; }
    [ObservableProperty] public partial string BuildOutputPath { get; set; } = string.Empty;
    [ObservableProperty] public partial int BuildProgressMode { get; set; } = 1;
    [ObservableProperty] public partial string BuildSecretFiles { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildSecretEnvironment { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildLog { get; set; } = string.Empty;
    [ObservableProperty] public partial string BuildMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string OperationOutput { get; set; } = string.Empty;
    [ObservableProperty] public partial string ImageInspectOutput { get; set; } = string.Empty;

    public bool HasSelectedImage => SelectedImage is not null;
    public bool HasImageInspectOutput =>
        SelectedImage is not null &&
        _inspectedImageKey == GetImageKey(SelectedImage) &&
        !string.IsNullOrWhiteSpace(ImageInspectOutput);
    public string SelectedImageDisplayName => SelectedImage?.DisplayName ??
        (Workspace.SettingsService.Current.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "Select an image" : "请选择镜像");

    [RelayCommand]
    private async Task PullImageAsync()
    {
        if (string.IsNullOrWhiteSpace(ImageReference)) return;
        await ShowOperationResultAsync(await Workspace.RunTrackedAsync($"Pull {ImageReference}", (progress, token) => Workspace.Runtime.PullImageAsync(ImageReference, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    public bool IsBuildExport => BuildOutputMode == (int)ImageBuildOutput.Tar;
    public bool IsBuildLocalImage => !IsBuildExport;
    public bool CanUseBuildSecrets => Allows(RuntimeFeature.BuildSecret);
    public bool CanUseBuildOutput => Allows(RuntimeFeature.BuildOutput);
    public bool CanUseBuildProgress => Allows(RuntimeFeature.BuildProgress);
    public bool CanUseBuildPull => Allows(RuntimeFeature.BuildPull);
    // Rechecking capabilities must still let users clear previously selected options.
    public bool CanEditBuildSecrets => CanUseBuildSecrets || BuildSecretFiles.Length > 0 || BuildSecretEnvironment.Length > 0;
    public bool CanToggleBuildPull => CanUseBuildPull || BuildPull;
    private bool Allows(RuntimeFeature feature) => Workspace.Capabilities[feature].Support != CapabilitySupport.Unsupported;
    private bool CanBuildImage() => !Workspace.IsBusy;

    partial void OnBuildOutputModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsBuildExport));
        OnPropertyChanged(nameof(IsBuildLocalImage));
    }

    partial void OnBuildSecretFilesChanged(string value) => OnPropertyChanged(nameof(CanEditBuildSecrets));
    partial void OnBuildSecretEnvironmentChanged(string value) => OnPropertyChanged(nameof(CanEditBuildSecrets));
    partial void OnBuildPullChanged(bool value) => OnPropertyChanged(nameof(CanToggleBuildPull));

    protected override void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        base.OnWorkspacePropertyChanged(sender, eventArgs);
        if (eventArgs.PropertyName == nameof(RuntimeWorkspace.IsBusy)) BuildImageCommand.NotifyCanExecuteChanged();
        if (eventArgs.PropertyName == nameof(RuntimeWorkspace.Capabilities))
        {
            OnPropertyChanged(nameof(CanUseBuildSecrets));
            OnPropertyChanged(nameof(CanUseBuildOutput));
            OnPropertyChanged(nameof(CanUseBuildProgress));
            OnPropertyChanged(nameof(CanUseBuildPull));
            OnPropertyChanged(nameof(CanEditBuildSecrets));
            OnPropertyChanged(nameof(CanToggleBuildPull));
        }
    }

    [RelayCommand(CanExecute = nameof(CanBuildImage))]
    private async Task BuildImageAsync()
    {
        if (Workspace.IsBusy) return;
        var folder = string.IsNullOrWhiteSpace(BuildContextPath)
            ? Workspace.Interaction.PickFolder(LocalizationService.GetString("BuildContext", "Build context")) : BuildContextPath;
        if (string.IsNullOrWhiteSpace(folder)) return;
        BuildContextPath = folder;
        var request = new ImageBuildRequest
        {
            ContextPath = folder, Tag = BuildImageTag, Dockerfile = DockerfilePath,
            BuildArguments = ImageBuildOptions.Lines(BuildArgumentsText), Target = BuildTarget,
            NoCache = BuildNoCache, Pull = BuildPull, Output = (ImageBuildOutput)BuildOutputMode,
            OutputPath = BuildOutputPath,
            Progress = CanUseBuildProgress ? (ImageBuildProgress)BuildProgressMode : null,
            Secrets = ImageBuildOptions.ParseSecrets(BuildSecretFiles, BuildSecretEnvironment)
        };
        var error = ImageBuildOptions.Validate(request);
        if (request.Pull && !CanUseBuildPull || request.Secrets.Count > 0 && !CanUseBuildSecrets ||
            request.Output != ImageBuildOutput.LocalImage && !CanUseBuildOutput) error = "BuildFeatureUnavailable";
        BuildMessage = error is null ? string.Empty : LocalizationService.GetString(error, error);
        if (error is not null) return;
        BuildLog = string.Empty;
        var uiContext = SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext
            ? SynchronizationContext.Current : null;
        try
        {
            var result = await Workspace.RunTrackedAsync(LocalizationService.GetString("Build", "Build"), (progress, token) =>
                Workspace.Runtime.BuildImageAsync(request, new BuildLogProgress(this, progress, uiContext), token));
            BuildLog = result.CombinedOutput;
            await ShowOperationResultAsync(result);
            if (result.Success)
            {
                BuildMessage = request.Output == ImageBuildOutput.LocalImage
                    ? LocalizationService.GetString("BuildImageCompleted", "Local image build completed.")
                    : LocalizationService.GetString("BuildExportCompleted", "Tar export completed; no local image was created.");
                await Workspace.RefreshAllAsync();
            }
        }
        catch (OperationCanceledException)
        {
            BuildMessage = LocalizationService.GetString("BuildCancelled", "Build cancelled. An export may be incomplete.");
        }
    }

    private sealed class BuildLogProgress(ImagesViewModel owner, IProgress<string> tracked, SynchronizationContext? uiContext) : IProgress<string>
    {
        public void Report(string value)
        {
            tracked.Report(value);
            void Append()
            {
                var log = owner.BuildLog + value + Environment.NewLine;
                owner.BuildLog = log.Length > 100_000 ? log[^100_000..] : log;
            }
            if (uiContext is not null) uiContext.Send(_ => Append(), null);
            else Append();
        }
    }

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ArchivePath) ? Workspace.Interaction.PickOpenFile("Import image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ArchivePath;
        if (path is null || string.IsNullOrWhiteSpace(ImportImageName)) return;
        ArchivePath = path;
        await ShowOperationResultAsync(await Workspace.RunTrackedAsync("Import image", (progress, token) => Workspace.Runtime.ImportImageAsync(path, ImportImageName, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task LoadImageAsync()
    {
        var path = string.IsNullOrWhiteSpace(ArchivePath) ? Workspace.Interaction.PickOpenFile("Load image", "Tar archive (*.tar)|*.tar|All files (*.*)|*.*") : ArchivePath;
        if (path is null) return;
        ArchivePath = path;
        await ShowOperationResultAsync(await Workspace.RunTrackedAsync("Load image", (progress, token) => Workspace.Runtime.LoadImageAsync(path, progress, token)));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task SaveImageAsync()
    {
        if (SelectedImage is null) return;
        var image = SelectedImage.DisplayName;
        var path = Workspace.Interaction.PickSaveFile("Save image", "Tar archive (*.tar)|*.tar", image.Replace('/', '_').Replace(':', '_') + ".tar");
        if (path is null) return;
        ArchivePath = path;
        await ShowOperationResultAsync(await Workspace.RunTrackedAsync("Save image", (progress, token) => Workspace.Runtime.SaveImageAsync(image, path, progress, token)));
    }

    [RelayCommand]
    private async Task TagImageAsync()
    {
        if (SelectedImage is null || string.IsNullOrWhiteSpace(ImageTag)) return;
        await ShowOperationResultAsync(await Workspace.Runtime.TagImageAsync(SelectedImage.DisplayName, ImageTag, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task PushImageAsync()
    {
        if (SelectedImage is null) return;
        var image = SelectedImage.DisplayName;
        await ShowOperationResultAsync(await Workspace.RunTrackedAsync("Push image", (progress, token) => Workspace.Runtime.PushImageAsync(image, progress, token)));
    }

    [RelayCommand]
    private async Task RemoveImageAsync()
    {
        if (SelectedImage is null || !await Workspace.Interaction.ConfirmAsync("Remove image", $"Permanently remove {SelectedImage.DisplayName}?")) return;
        await ShowOperationResultAsync(await Workspace.Runtime.RemoveImageAsync(SelectedImage.DisplayName, true, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    [RelayCommand]
    private async Task InspectImageAsync()
    {
        if (SelectedImage is null) return;
        var result = await Workspace.Runtime.InspectImageAsync(SelectedImage.DisplayName, Workspace.Lifetime.Token);
        _inspectedImageKey = GetImageKey(SelectedImage);
        ImageInspectOutput = result.Success ? JsonOutputFormatter.Format(result.Output) : result.CombinedOutput;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
        }
    }

    [RelayCommand]
    private async Task PruneAsync(string? resource)
    {
        if (resource is not "image") return;
        var title = LocalizationService.GetString("PruneDanglingImagesTitle", "Prune dangling images");
        var message = LocalizationService.GetString("PruneDanglingImagesConfirmation", "Remove every dangling image?");
        if (!await Workspace.Interaction.ConfirmAsync(title, message)) return;
        await ShowOperationResultAsync(await Workspace.Runtime.PruneAsync(resource, Workspace.Lifetime.Token));
        await Workspace.RefreshAllAsync();
    }

    private async Task ShowOperationResultAsync(OperationResult result)
    {
        OperationOutput = result.CombinedOutput;
        if (!result.Success && !string.IsNullOrWhiteSpace(result.Error))
        {
            await Workspace.Interaction.ShowErrorAsync("WSLC operation failed", result.Error);
        }
    }

    partial void OnImageSearchTextChanged(string value) => ApplyImageFilter();

    partial void OnSelectedImageChanged(ImageSummary? value)
    {
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(HasImageInspectOutput));
        OnPropertyChanged(nameof(SelectedImageDisplayName));
    }

    partial void OnImageInspectOutputChanged(string value) => OnPropertyChanged(nameof(HasImageInspectOutput));

    public void RaiseLanguageChanged() => OnPropertyChanged(nameof(SelectedImageDisplayName));

    private void ApplyImageFilter()
    {
        var query = ImageSearchText.Trim();
        VisibleImages.ReplaceAll(Workspace.Images.Where(image => string.IsNullOrEmpty(query) ||
            image.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            image.Id.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void RefreshVisibleImages()
    {
        var selectedKey = SelectedImage is null ? null : GetImageKey(SelectedImage);
        ApplyImageFilter();
        if (selectedKey is not null)
        {
            SelectedImage = VisibleImages.FirstOrDefault(image => GetImageKey(image) == selectedKey);
        }
    }

    private static string GetImageKey(ImageSummary image) =>
        string.IsNullOrWhiteSpace(image.Id) ? image.DisplayName : image.Id;
}
