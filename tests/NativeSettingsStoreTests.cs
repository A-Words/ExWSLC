using System.IO;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public sealed class NativeSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExWSLC-settings-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.yaml");
    private NativeSettingsStore Store => new(SettingsPath);

    [Fact]
    public async Task MissingFileIsNotCreatedUntilSave()
    {
        var original = await Store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.False(original.Exists);
        Assert.False(Directory.Exists(_directory));
        Assert.Equal("NativeConfigSaved", await Store.SaveAsync(original, "session:\n  cpuCount: 4\n", TestContext.Current.CancellationToken));
        Assert.True((await Store.ReadAsync(TestContext.Current.CancellationToken)).Exists);
    }

    [Fact]
    public async Task SavePreservesCommentsUnknownFieldsAndBacksUpOriginal()
    {
        Directory.CreateDirectory(_directory);
        const string before = "# custom\nsession:\n  cpuCount: 2\nfutureOption: value\n";
        await File.WriteAllTextAsync(SettingsPath, before, TestContext.Current.CancellationToken);
        var original = await Store.ReadAsync(TestContext.Current.CancellationToken);
        var after = before.Replace("cpuCount: 2", "cpuCount: 4");
        Assert.Equal("NativeConfigSaved", await Store.SaveAsync(original, after, TestContext.Current.CancellationToken));
        Assert.Equal(after, await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllTextAsync(SettingsPath + ".exwslc.bak", TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Theory]
    [InlineData("session: [")]
    [InlineData("- item")]
    [InlineData("a: 1\n---\nb: 2")]
    [InlineData("a: &a {b: *a}")]
    [InlineData("session:\n  <<: value")]
    [InlineData("a: 1\na: 2")]
    public async Task InvalidYamlDoesNotWrite(string text)
    {
        Assert.Equal("NativeConfigInvalid", await Store.SaveAsync(await Store.ReadAsync(TestContext.Current.CancellationToken), text, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task ExternalCreationModificationAndDeletionAreConflicts()
    {
        var missing = await Store.ReadAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "session: {}\n", TestContext.Current.CancellationToken);
        Assert.Equal("NativeConfigConflict", await Store.SaveAsync(missing, "session: {cpuCount: 4}", TestContext.Current.CancellationToken));
        var original = await Store.ReadAsync(TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(SettingsPath, "session: {cpuCount: 8}", TestContext.Current.CancellationToken);
        Assert.Equal("NativeConfigConflict", await Store.SaveAsync(original, "session: {cpuCount: 4}", TestContext.Current.CancellationToken));
        Assert.Contains("cpuCount: 8", await File.ReadAllTextAsync(SettingsPath, TestContext.Current.CancellationToken));
        File.Delete(SettingsPath);
        Assert.Equal("NativeConfigConflict", await Store.SaveAsync(original, "session: {}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SizeLimitAndCancellationDoNotCreateFile()
    {
        Assert.False(NativeSettingsStore.IsValid(new string(' ', NativeSettingsStore.MaxCharacters + 1)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.SaveAsync(new("", false), "a: b", cancellation.Token));
        Assert.False(File.Exists(SettingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
