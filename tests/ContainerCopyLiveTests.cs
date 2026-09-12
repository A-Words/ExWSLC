using System.IO;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class ContainerCopyLiveTests
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("EXWSLC_COPY_LIVE") == "1";

    [Fact(Skip = "Set EXWSLC_COPY_LIVE=1 and EXWSLC_COPY_IMAGE to an existing local Linux image with /bin/sh.", SkipUnless = nameof(IsEnabled))]
    public async Task IsolatedContainer_RoundTripsOverwriteLinksFailuresAndCancellation()
    {
        var image = Environment.GetEnvironmentVariable("EXWSLC_COPY_IMAGE");
        Assert.False(string.IsNullOrWhiteSpace(image));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(16));
        var token = timeout.Token;
        var runner = new WslcProcessRunner();
        var capability = new RuntimeCapabilityService(runner, new WslcSdkService());
        var runtime = new WslcContainerRuntime(runner, capability);
        var snapshot = await capability.DetectAsync(token);
        var name = "exwslc-05-live-" + Guid.NewGuid().ToString("N")[..10];
        var root = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(root);
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"CLI {snapshot.CliVersion}; service {snapshot.ServiceVersion}; SDK {snapshot.SdkPackageVersion}; container {name}; local {root}");
        async Task<OperationResult> Copy(ContainerCopyDirection direction, string local, string remote)
        {
            var result = await runtime.CopyContainerPathAsync(new(direction, name, local, remote), cancellationToken: token);
            output.WriteLine($"{result.DisplayCommand}: exit={result.ExitCode}; {result.CombinedOutput}");
            return result;
        }
        try
        {
            var created = await runtime.RunContainerAsync(new() { Name = name, Image = image!, Command = "sleep 1200" }, cancellationToken: token);
            Assert.True(created.Success, created.Error);
            var prepared = await runtime.ExecAsync(name, "mkdir -p '/tmp/exwslc-05/目标 : ; $(literal)' /tmp/exwslc-05/links; printf original > /tmp/exwslc-05/links/original; ln -s original /tmp/exwslc-05/links/link", cancellationToken: token);
            Assert.True(prepared.Success, prepared.Error);
            const string destination = "/tmp/exwslc-05/目标 : ; $(literal)";
            var file = Path.Combine(root, "中文 & $(literal); 空格.txt");
            var bytes = System.Text.Encoding.UTF8.GetBytes("roundtrip 中文\n\0binary");
            await File.WriteAllBytesAsync(file, bytes, token);
            Assert.True((await Copy(ContainerCopyDirection.Upload, file, destination)).Success);
            var downloads = Directory.CreateDirectory(Path.Combine(root, "下载 & 空格")).FullName;
            var remoteFile = destination + "/" + Path.GetFileName(file);
            Assert.True((await Copy(ContainerCopyDirection.Download, downloads, remoteFile)).Success);
            var downloaded = Path.Combine(downloads, Path.GetFileName(file));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(downloaded, token));
            await File.WriteAllTextAsync(file, "overwritten 中文", token);
            Assert.True((await Copy(ContainerCopyDirection.Upload, file, destination)).Success);
            Assert.True((await Copy(ContainerCopyDirection.Download, downloads, remoteFile)).Success);
            Assert.Equal("overwritten 中文", await File.ReadAllTextAsync(downloaded, token));

            var directory = Directory.CreateDirectory(Path.Combine(root, "目录 空格")).FullName;
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllBytesAsync(Path.Combine(directory, "nested", "file.bin"), bytes, token);
            Assert.True((await Copy(ContainerCopyDirection.Upload, directory, destination)).Success);
            Assert.True((await Copy(ContainerCopyDirection.Download, downloads, destination + "/目录 空格")).Success);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(downloads, "目录 空格", "nested", "file.bin"), token));
            await File.WriteAllTextAsync(Path.Combine(downloads, "目录 空格", "keep.txt"), "keep", token);
            Assert.True((await Copy(ContainerCopyDirection.Download, downloads, destination + "/目录 空格")).Success);
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(downloads, "目录 空格", "keep.txt"), token));

            Assert.True((await Copy(ContainerCopyDirection.Download, downloads, "/tmp/exwslc-05/links")).Success);
            var localLink = Path.Combine(downloads, "links", "link");
            Assert.Equal("original", new FileInfo(localLink).LinkTarget);
            Assert.Equal("original", await File.ReadAllTextAsync(localLink, token));
            Assert.True((await Copy(ContainerCopyDirection.Upload, localLink, destination)).Success);
            var readLink = await runtime.ExecAsync(name, "readlink '/tmp/exwslc-05/目标 : ; $(literal)/link'", cancellationToken: token);
            Assert.True(readLink.Success, readLink.Error);
            Assert.Equal("original", readLink.Output.Trim());

            Assert.False((await Copy(ContainerCopyDirection.Download, downloads, "/tmp/exwslc-05/missing")).Success);
            Assert.False((await Copy(ContainerCopyDirection.Upload, file, "/tmp/exwslc-05/missing")).Success);
            Assert.False((await Copy(ContainerCopyDirection.Upload, file, remoteFile)).Success);
            // The engine ignores container process uid for archive access. Exercise an actual local write denial instead.
            using (var locked = new FileStream(downloaded, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.False((await Copy(ContainerCopyDirection.Download, downloads, remoteFile)).Success);
            Assert.Equal("overwritten 中文", await File.ReadAllTextAsync(downloaded, token));

            var large = Path.Combine(root, "cancel.bin");
            using (var stream = File.Create(large)) stream.SetLength(256 * 1024 * 1024);
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var transfer = runtime.CopyContainerPathAsync(new(ContainerCopyDirection.Upload, name, large, destination), cancellationToken: cancellation.Token);
                // Capability cache is warm; ExecuteAsync has started this copy process before its first asynchronous wait.
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => transfer);
            }
            Assert.True(File.Exists(large));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(downloads, "目录 空格", "keep.txt"), token));
            output.WriteLine("Verified byte equality, overwrite, directory merge, links, missing source/target, non-directory target, write denial and cancellation. Existing destination sentinel retained.");
            if (int.TryParse(Environment.GetEnvironmentVariable("EXWSLC_COPY_UI_HOLD_SECONDS"), out var seconds))
            {
                output.WriteLine($"UI hold {seconds}s: {name}; {root}");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 900)), token);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            OperationResult removed;
            try
            {
                removed = await runtime.RemoveContainerAsync(name, true, cleanup.Token);
            }
            finally
            {
                // Local cleanup still runs if container cleanup times out or throws.
                // This test created the unique root and every child; production copy code never deletes destinations.
                Directory.Delete(root, true);
            }
            Assert.False(Directory.Exists(root));
            Assert.True(removed.Success, removed.Error);
            Assert.DoesNotContain(await runtime.GetContainersAsync(cleanup.Token), container => container.Name == name);
            output.WriteLine($"Cleaned container {name} and local directory {root}");
        }
    }
}
