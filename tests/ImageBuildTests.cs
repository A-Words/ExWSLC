using System.IO;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class ImageBuildTests
{
    [Fact]
    public void DefaultBuild_OnlyAddsTagAndContext()
    {
        Assert.Equal(["image", "build", "--tag", "app:test", @"C:\项目 文件"],
            ImageBuildOptions.BuildArguments(new() { ContextPath = @"C:\项目 文件", Tag = "app:test" }));
    }

    [Fact]
    public void AdvancedBuild_SeparatesArgumentsAndOmitsTagForExport()
    {
        var request = new ImageBuildRequest
        {
            ContextPath = @"C:\项目 文件", Tag = "ignored", Dockerfile = @"C:\项目 文件\Dockerfile.dev",
            BuildArguments = ["VERSION=one two", "EMPTY="], Target = "export", NoCache = true, Pull = true,
            Output = ImageBuildOutput.Tar, OutputPath = @"C:\输出 文件\out.tar", Progress = ImageBuildProgress.Plain,
            Secrets = [new("file", BuildSecretSource.File, @"C:\机密 文件.txt"), new("token", BuildSecretSource.Environment, "BUILD_TOKEN")]
        };
        Assert.Equal(["image", "build", "--file", @"C:\项目 文件\Dockerfile.dev", "--build-arg", "VERSION=one two", "--build-arg", "EMPTY=",
            "--target", "export", "--no-cache", "--pull", "--output", @"type=tar,dest=C:\输出 文件\out.tar", "--progress", "plain",
            "--secret", @"id=file,type=file,src=C:\机密 文件.txt", "--secret", "id=token,type=env,env=BUILD_TOKEN", @"C:\项目 文件"],
            ImageBuildOptions.BuildArguments(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("out.tar,type=registry")]
    [InlineData("out\".tar")]
    public void Export_RejectsMissingOrUnsafeDestination(string path) =>
        Assert.Equal("BuildOutputPathRequired", ImageBuildOptions.Validate(new() { ContextPath = "context", Output = ImageBuildOutput.Tar, OutputPath = path }));

    [Fact]
    public void Validation_RejectsInvalidVariablesAndDuplicateSecretIds()
    {
        var valid = new ImageBuildRequest { ContextPath = "context", Tag = "tag" };
        Assert.Equal("BuildArgumentsInvalid", ImageBuildOptions.Validate(valid with { BuildArguments = ["TOKEN"] }));
        Assert.Equal("BuildSecretsInvalid", ImageBuildOptions.Validate(valid with
        { Secrets = ImageBuildOptions.ParseSecrets("id=C:\\secret", "id=TOKEN") }));
        Assert.Equal("BuildSecretsInvalid", ImageBuildOptions.Validate(valid with
        { Secrets = ImageBuildOptions.ParseSecrets("missing separator", "") }));
        Assert.Equal("BuildSecretsInvalid", ImageBuildOptions.Validate(valid with
        { Secrets = ImageBuildOptions.ParseSecrets("", "id=not a variable") }));
    }

    [Theory]
    [InlineData(ImageBuildProgress.Auto, "auto")]
    [InlineData(ImageBuildProgress.Plain, "plain")]
    [InlineData(ImageBuildProgress.Quiet, "quiet")]
    public void Progress_UsesSupportedTextModes(ImageBuildProgress mode, string expected) =>
        Assert.Contains(expected, ImageBuildOptions.BuildArguments(new() { ContextPath = "context", Tag = "tag", Progress = mode }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SecretBuild_RedactsProgressResultAndCommandBeforeReturning(bool success)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "top-secret-value\nsecond-secret-line", TestContext.Current.CancellationToken);
            var runner = new Mock<IProcessRunner>();
            runner.Setup(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null,
                    It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                .Returns((string file, IReadOnlyList<string> args, string? stdin, IProgress<string> progress, CancellationToken token) =>
                {
                    progress.Report("\u001b[32m#1 CACHED\u001b[0m top-secret-value");
                    return Task.FromResult(new OperationResult(success, success ? 0 : 1, "top-secret-value", "second-secret-line", "top-secret-value"));
                });
            var lines = new List<string>();
            var result = await new WslcContainerRuntime(runner.Object).BuildImageAsync(new()
            { ContextPath = "context", Tag = "tag", Secrets = [new("token", BuildSecretSource.File, path)] }, new CaptureProgress(lines), TestContext.Current.CancellationToken);
            Assert.Equal(success, result.Success);
            Assert.Equal("[REDACTED]", result.Output);
            Assert.Equal("[REDACTED]", result.Error);
            Assert.Equal("wslc image build", result.DisplayCommand);
            Assert.Equal(["#1 CACHED [REDACTED]"], lines);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingSecret_FailsBeforeStartingCliWithoutEchoingSource()
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new WslcContainerRuntime(runner.Object).BuildImageAsync(new()
        { ContextPath = "context", Tag = "tag", Secrets = [new("id", BuildSecretSource.File, "missing-secret-" + Guid.NewGuid())] }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.DoesNotContain("missing-secret-", exception.Message);
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelledBuild_ThrowsForTaskCancellationState()
    {
        var runner = new Mock<IProcessRunner>();
        runner.Setup(value => value.ExecuteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null,
                It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, -2, "", "cancelled", ""));
        var tasks = new TaskService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tasks.RunAsync("Build", (progress, token) =>
            new WslcContainerRuntime(runner.Object).BuildImageAsync(new() { ContextPath = "context", Tag = "tag" }, progress, token), TestContext.Current.CancellationToken));
        Assert.Equal(RuntimeTaskState.Cancelled, Assert.Single(tasks.Tasks).State);
    }

    [Fact]
    public async Task EnvironmentSecret_IsResolvedOnlyInMemoryAndRedacted()
    {
        var name = "EXWSLC_06_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "synthetic-sensitive-value");
        try
        {
            var redactor = await BuildOutputRedactor.CreateAsync([new("token", BuildSecretSource.Environment, name)], TestContext.Current.CancellationToken);
            Assert.Equal("#2 [REDACTED]", redactor.Clean("#2 synthetic-sensitive-value"));
            var arguments = ImageBuildOptions.BuildArguments(new() { ContextPath = "context", Tag = "tag", Secrets = [new("token", BuildSecretSource.Environment, name)] });
            Assert.Contains($"id=token,type=env,env={name}", arguments);
            Assert.DoesNotContain("synthetic-sensitive-value", string.Join(' ', arguments));
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    private sealed class CaptureProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }
}
