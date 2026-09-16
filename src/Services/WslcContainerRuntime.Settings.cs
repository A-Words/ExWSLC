using System.IO;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed partial class WslcContainerRuntime
{
    private readonly NativeSettingsStore _nativeSettings = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "settings.yaml"));

    public Task<NativeSettingsDocument> ReadNativeSettingsAsync(CancellationToken cancellationToken = default) =>
        _nativeSettings.ReadAsync(cancellationToken);

    public Task<string> SaveNativeSettingsAsync(NativeSettingsDocument original, string text, CancellationToken cancellationToken = default) =>
        _nativeSettings.SaveAsync(original, text, cancellationToken);
}
