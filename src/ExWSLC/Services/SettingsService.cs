using System.Text.Json;
using System.IO;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ExWSLC",
            "settings.json");
    }

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return;
        try
        {
            await using var stream = File.OpenRead(_path);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken) ?? new AppSettings();
        }
        catch (JsonException)
        {
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken);
    }
}
