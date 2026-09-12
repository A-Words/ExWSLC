using System.IO;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;
using Moq;

namespace ExWSLC.Tests;

public class ContainerCopyTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "exwslc-05-unit-" + Guid.NewGuid().ToString("N"))).FullName;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Upload_PreservesFileAndDirectoryNamesAsLiteralArguments(bool directory)
    {
        var path = Path.Combine(_root, "中文 & $(literal); 空格");
        if (directory) Directory.CreateDirectory(path); else File.WriteAllText(path, "source");
        var request = new ContainerCopyRequest(ContainerCopyDirection.Upload, "container-id", path, "/data/中文 : ; $(literal)");
        Assert.Equal(["container", "cp", path, "container-id:/data/中文 : ; $(literal)"], ContainerCopyOptions.BuildArguments(request));
    }

    [Fact]
    public void Download_AlwaysUsesDirectoryExtractionIncludingTrailingSeparator()
    {
        Assert.Equal(["container", "cp", "container-id:/data/file", _root + "\\"],
            ContainerCopyOptions.BuildArguments(new(ContainerCopyDirection.Download, "container-id", _root, "/data/file")));
        var file = Path.Combine(_root, "existing");
        File.WriteAllText(file, "keep");
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.BuildArguments(new(ContainerCopyDirection.Download, "container-id", file, "/data/link")));
        Assert.Equal("keep", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("C:relative")] [InlineData("relative")] [InlineData("-")] [InlineData("C:\\a:stream")]
    [InlineData("C:\\a\\.")] [InlineData("C:\\a\\..\\b")] [InlineData("\\\\server\\share")]
    [InlineData("C:\\bad\nfile")] [InlineData("C:\\bad\"file")]
    [InlineData("C:\\")] [InlineData("C:/")]
    public void AmbiguousLocalPaths_AreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.Validate(new(ContainerCopyDirection.Upload, "container-id", path, "/data")));

    [Theory]
    [InlineData("relative")] [InlineData("/data/.")] [InlineData("/data/../file")] [InlineData("/data\nfile")]
    public void AmbiguousRemotePaths_AreRejected(string path) =>
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.Validate(new(ContainerCopyDirection.Download, "container-id", _root, path)));

    [Theory]
    [InlineData("a")] [InlineData("--session")] [InlineData("bad:name")] [InlineData("bad name")]
    public void AmbiguousContainerIdentifiers_AreRejected(string id) =>
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.Validate(new(ContainerCopyDirection.Download, id, _root, "/data")));

    [Fact]
    public void Upload_MissingSourceAndTarOptionBasenamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.Validate(new(ContainerCopyDirection.Upload, "container-id", Path.Combine(_root, "missing"), "/data")));
        var path = Path.Combine(_root, "--help");
        File.WriteAllText(path, "literal");
        Assert.Throws<ArgumentException>(() => ContainerCopyOptions.Validate(new(ContainerCopyDirection.Upload, "container-id", path, "/data")));
    }

    [Theory]
    [InlineData(CapabilitySupport.Unknown)] [InlineData(CapabilitySupport.Unsupported)]
    public async Task UnverifiedCapability_DoesNotExecute(CapabilitySupport support)
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var runtime = new WslcContainerRuntime(runner.Object, Capability(support));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.CopyContainerPathAsync(new(ContainerCopyDirection.Download, "container-id", _root, "/data"), cancellationToken: TestContext.Current.CancellationToken));
        runner.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(-2)]
    public async Task Runtime_ForwardsArgumentsProgressAndCancellationAndPreservesFailures(int code)
    {
        var request = new ContainerCopyRequest(ContainerCopyDirection.Download, "container-id", _root, "/data/中文 : file");
        var runner = new Mock<IProcessRunner>();
        var progress = new Progress<string>();
        using var cancellation = new CancellationTokenSource();
        var result = new OperationResult(code == 0, code, "output", "permission denied", "command");
        runner.Setup(x => x.ExecuteAsync("wslc.exe", It.Is<IReadOnlyList<string>>(args => args.SequenceEqual(ContainerCopyOptions.BuildArguments(request))),
            null, progress, cancellation.Token)).ReturnsAsync(result);
        var runtime = new WslcContainerRuntime(runner.Object, Capability(CapabilitySupport.Supported));
        if (code == -2)
        {
            var tasks = new TaskService();
            await Assert.ThrowsAsync<OperationCanceledException>(() => tasks.RunAsync("copy", (_, token) => runtime.CopyContainerPathAsync(request, progress, token), cancellation.Token));
            Assert.Equal(RuntimeTaskState.Cancelled, Assert.Single(tasks.Tasks).State);
        }
        else Assert.Same(result, await runtime.CopyContainerPathAsync(request, progress, cancellation.Token));
        runner.VerifyAll();
    }

    internal static IRuntimeCapabilityService Capability(CapabilitySupport support)
    {
        var mock = new Mock<IRuntimeCapabilityService>();
        mock.Setup(x => x.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeCapabilities
        {
            Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability> { [RuntimeFeature.ContainerCopy] = new(support, "CapabilityAdvertised", "test") }
        });
        return mock.Object;
    }

    public void Dispose() => Directory.Delete(_root, true);
}
