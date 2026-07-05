using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class SettingsAndTaskServiceTests
{
    [Fact]
    public async Task SettingsService_RoundTripsUserPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ExWSLC.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var writer = new SettingsService(path);
            writer.Current.Language = "en-US";
            writer.Current.Theme = "Dark";
            writer.Current.RefreshIntervalSeconds = 15;
            await writer.SaveAsync(TestContext.Current.CancellationToken);

            var reader = new SettingsService(path);
            await reader.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal("en-US", reader.Current.Language);
            Assert.Equal("Dark", reader.Current.Theme);
            Assert.Equal(15, reader.Current.RefreshIntervalSeconds);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TaskService_RecordsFailureWithoutThrowing()
    {
        var service = new TaskService();
        var result = await service.RunAsync("failing task", (_, _) =>
            Task.FromResult(new OperationResult(false, 7, string.Empty, "boom", "test")), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        var task = Assert.Single(service.Tasks);
        Assert.Equal(RuntimeTaskState.Failed, task.State);
        Assert.Contains("boom", task.Detail);
        Assert.NotNull(task.FinishedAt);
    }
}
