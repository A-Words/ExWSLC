using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class ContainerExportOperationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("cancel-result")]
    [InlineData("stop-failure")]
    [InlineData("stop-cancel")]
    public async Task TemporaryStop_AlwaysAttemptsRecoveryWithIndependentToken(string scenario)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = new List<string>();
        var runtime = new Mock<IContainerRuntime>(MockBehavior.Strict);
        runtime.Setup(x => x.StopContainerAsync("id", It.IsAny<CancellationToken>())).Returns(() =>
        {
            calls.Add("stop");
            if (scenario == "stop-cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return Task.FromResult(Result(scenario != "stop-failure"));
        });
        runtime.Setup(x => x.ExportContainerAsync("id", "out.tar", null, It.IsAny<CancellationToken>())).Returns(() =>
        {
            calls.Add("export");
            if (scenario == "exception") throw new InvalidOperationException("export failed");
            if (scenario == "cancel") { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            return Task.FromResult(scenario == "cancel-result" ? new OperationResult(false, -2, "", "cancelled", "") : Result(scenario != "failure"));
        });
        runtime.Setup(x => x.StartContainerAsync("id", It.IsAny<CancellationToken>())).Returns<string, CancellationToken>((_, token) =>
        {
            calls.Add("restore");
            Assert.False(token.IsCancellationRequested);
            Assert.NotEqual(cancel.Token, token);
            return Task.FromResult(Result(true));
        });
        var operation = () => ContainerExportOperation.RunAsync(runtime.Object, "id", "out.tar", true, null, cancel.Token);
        if (scenario.Contains("cancel")) await Assert.ThrowsAsync<OperationCanceledException>(operation);
        else if (scenario == "exception") await Assert.ThrowsAsync<InvalidOperationException>(operation);
        else Assert.Equal(scenario == "success", (await operation()).Success);
        Assert.Equal(scenario.StartsWith("stop-") ? ["stop", "restore"] : ["stop", "export", "restore"], calls);
    }

    [Fact]
    public async Task RestoreFailure_ReportsExportAndRecoveryFailureWithoutRecreatingAutoRemovedContainer()
    {
        var runtime = new Mock<IContainerRuntime>(MockBehavior.Strict);
        runtime.Setup(x => x.StopContainerAsync("id", It.IsAny<CancellationToken>())).ReturnsAsync(Result(true));
        runtime.Setup(x => x.ExportContainerAsync("id", "out.tar", null, It.IsAny<CancellationToken>())).ReturnsAsync(Result(false, "export missing container"));
        runtime.Setup(x => x.StartContainerAsync("id", It.IsAny<CancellationToken>())).ReturnsAsync(Result(false, "start missing container"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ContainerExportOperation.RunAsync(runtime.Object, "id", "out.tar", true, null, TestContext.Current.CancellationToken));
        Assert.Contains("export missing container", exception.Message);
        Assert.Contains("start missing container", exception.Message);
    }

    [Fact]
    public async Task AlreadyStoppedContainer_OnlyExports()
    {
        var runtime = new Mock<IContainerRuntime>(MockBehavior.Strict);
        runtime.Setup(x => x.ExportContainerAsync("id", "out.tar", null, It.IsAny<CancellationToken>())).ReturnsAsync(Result(true));
        Assert.True((await ContainerExportOperation.RunAsync(runtime.Object, "id", "out.tar", false, null, TestContext.Current.CancellationToken)).Success);
        Assert.Single(runtime.Invocations);
    }

    private static OperationResult Result(bool success, string error = "failed") => new(success, success ? 0 : 1, "", success ? "" : error, "");
}
