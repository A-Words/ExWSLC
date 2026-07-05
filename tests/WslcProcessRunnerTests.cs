using ExWSLC.Services;

namespace ExWSLC.Tests;

public class WslcProcessRunnerTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotExposeStandardInputInDisplayCommand()
    {
        var runner = new WslcProcessRunner();
        const string secret = "token-that-must-not-be-logged";

        var result = await runner.ExecuteAsync(
            "cmd.exe",
            ["/d", "/c", "set /p secret=& echo accepted"],
            secret,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("accepted", result.Output);
        Assert.DoesNotContain(secret, result.DisplayCommand);
        Assert.DoesNotContain(secret, result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationStopsProcessTree()
    {
        var runner = new WslcProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = await runner.ExecuteAsync("cmd.exe", ["/d", "/c", "ping 127.0.0.1 -n 20 > nul"], cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(-2, result.ExitCode);
    }

    [Fact]
    public void BuildDisplayCommand_QuotesWhitespaceAndEscapesQuotes()
    {
        var command = WslcProcessRunner.BuildDisplayCommand("wslc.exe", ["exec", "web app", "echo \"hi\""]);
        Assert.Equal("wslc.exe exec \"web app\" \"echo \\\"hi\\\"\"", command);
    }
}
