using System.Formats.Tar;
using System.IO;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class ImageBuildLiveTests
{
    [Fact]
    public async Task IsolatedBuild_LocalImageCacheTarFailureAndCleanup()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("EXWSLC_LIVE_06") == "1", "Set EXWSLC_LIVE_06=1 to create isolated task 06 build resources.");
        var id = "exwslc-06-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), id);
        var context = Path.Combine(root, "中文 context");
        var tag = id + ":test";
        var secretEnvironment = id.Replace('-', '_').ToUpperInvariant();
        var runner = new WslcProcessRunner();
        var runtime = new WslcContainerRuntime(runner);
        Directory.CreateDirectory(context);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        try
        {
            TestContext.Current.TestOutputHelper!.WriteLine("Service: " + new WslcSdkService().GetServiceVersion() + "; SDK package: " + RuntimeCapabilities.BundledSdkPackageVersion);
            await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), "FROM scratch AS export\nARG FILE=proof.txt\nCOPY ${FILE} /proof.txt\n", token);
            await File.WriteAllTextAsync(Path.Combine(context, "proof.txt"), "exwslc task 06\n", token);
            var secretFile = Path.Combine(root, "secret 文件.txt");
            await File.WriteAllTextAsync(secretFile, "synthetic-file-secret", token);
            Environment.SetEnvironmentVariable(secretEnvironment, "synthetic-env-secret");
            var unsupported = await runner.ExecuteAsync("wslc.exe", ["build", "--output", $"type=local,dest={root}/directory", context], cancellationToken: token);
            TestContext.Current.TestOutputHelper!.WriteLine("local exporter: " + unsupported.CombinedOutput);
            Assert.False(unsupported.Success);

            var request = new ImageBuildRequest { ContextPath = context, Tag = tag, Progress = ImageBuildProgress.Plain };
            var image = await runtime.BuildImageAsync(request, cancellationToken: token);
            TestContext.Current.TestOutputHelper.WriteLine("image: " + image.CombinedOutput);
            Assert.True(image.Success, image.CombinedOutput);
            Assert.Contains(await runtime.GetImagesAsync(token), value => value.DisplayName == tag);
            var cached = await runtime.BuildImageAsync(request, cancellationToken: token);
            Assert.True(cached.Success, cached.CombinedOutput);
            Assert.Contains("CACHED", cached.CombinedOutput);

            var tar = Path.Combine(root, "中文 output.tar");
            var exported = await runtime.BuildImageAsync(request with
            {
                Tag = "", Target = "export", BuildArguments = ["FILE=proof.txt"], NoCache = true, Pull = true,
                Output = ImageBuildOutput.Tar, OutputPath = tar,
                Secrets = [new("file", BuildSecretSource.File, secretFile), new("token", BuildSecretSource.Environment, secretEnvironment)]
            }, cancellationToken: token);
            TestContext.Current.TestOutputHelper.WriteLine("tar: " + exported.CombinedOutput);
            Assert.True(exported.Success, exported.CombinedOutput);
            Assert.True(File.Exists(tar));
            using (var stream = File.OpenRead(tar))
            using (var reader = new TarReader(stream))
            {
                var found = false;
                while (reader.GetNextEntry() is { } entry)
                    if (entry.Name.TrimStart('.', '/') == "proof.txt") found = true;
                Assert.True(found, "Expected root filesystem artifact in tar.");
            }
            await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), "INVALID_INSTRUCTION\n", token);
            var failed = await runtime.BuildImageAsync(request, cancellationToken: token);
            Assert.False(failed.Success);
            Assert.NotEmpty(failed.CombinedOutput);
            TestContext.Current.TestOutputHelper.WriteLine("failure: " + failed.CombinedOutput);
            await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), "FROM scratch\nCOPY proof.txt /proof.txt\n", token);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var cancelProgress = new CancelOnOutput(cancellation);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.BuildImageAsync(request, cancelProgress, cancellation.Token));
            Assert.True(cancelProgress.ReceivedOutput, "Cancellation should follow actual CLI output.");
            TestContext.Current.TestOutputHelper.WriteLine("cancel: requested after first CLI output; task cancelled");
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretEnvironment, null);
            // Only the uniquely named tag and directory owned by this test may be removed.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var removed = await runtime.RemoveImageAsync(tag, false, cleanup.Token);
            TestContext.Current.TestOutputHelper!.WriteLine("cleanup image: " + removed.Success);
            var resolved = Path.GetFullPath(root);
            Assert.Equal(Path.Combine(Path.GetFullPath(Path.GetTempPath()), id), resolved);
            Directory.Delete(resolved, recursive: true);
            Assert.False(Directory.Exists(resolved));
            Assert.DoesNotContain(await runtime.GetImagesAsync(cleanup.Token), value => value.DisplayName == tag);
        }
    }

    private sealed class CancelOnOutput(CancellationTokenSource cancellation) : IProgress<string>
    {
        private int _received;
        public bool ReceivedOutput => _received > 0;
        public void Report(string value)
        {
            if (Interlocked.Exchange(ref _received, 1) == 0) cancellation.Cancel();
        }
    }
}
