using System.Diagnostics;
using System.IO;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class ContainerLaunchLiveTests
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("EXWSLC_LAUNCH_LIVE") == "1";

    [Fact(Skip = "Set EXWSLC_LAUNCH_LIVE=1 and EXWSLC_LAUNCH_IMAGE to an existing local Linux image with /bin/sh.", SkipUnless = nameof(IsEnabled))]
    public async Task IsolatedResources_ExercisePullMountsStopInheritanceAndCancellation()
    {
        var image = Environment.GetEnvironmentVariable("EXWSLC_LAUNCH_IMAGE");
        Assert.False(string.IsNullOrWhiteSpace(image));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(25));
        var token = deadline.Token;
        var runner = new WslcProcessRunner();
        var capability = new RuntimeCapabilityService(runner, new WslcSdkService());
        var runtime = new WslcContainerRuntime(runner, capability);
        var snapshot = await capability.DetectAsync(token);
        var prefix = "exwslc-07-" + Guid.NewGuid().ToString("N")[..10];
        var tag = "127.0.0.1:1/" + prefix + ":latest";
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), prefix, "中文, space & literal")).FullName;
        var volume = prefix + "-volume";
        var names = new List<string>();
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"CLI {snapshot.CliVersion}; service {snapshot.ServiceVersion}; SDK {snapshot.SdkPackageVersion}; prefix {prefix}");
        async Task Check(Task<OperationResult> operation)
        {
            var result = await operation;
            output.WriteLine(result.DisplayCommand);
            Assert.True(result.Success, result.CombinedOutput);
        }
        async Task Ready(string name)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if ((await runtime.ExecAsync(name, "test -f /tmp/ready", cancellationToken: token)).Success) return;
                await Task.Delay(100, token);
            }
            Assert.Fail("Container did not become ready.");
        }
        async Task Launch(string suffix, int? timeout, string? signal, ContainerPullPolicy policy = ContainerPullPolicy.Never, bool remove = false)
        {
            var name = prefix + suffix;
            names.Add(name);
            await Check(runtime.RunContainerAsync(new()
            {
                Image = tag, Name = name, PullPolicy = policy, StopTimeoutSeconds = timeout, StopSignal = signal, RemoveWhenStopped = remove,
                Command = "trap 'exit 0' INT; trap '' TERM; touch /tmp/ready; while :; do sleep 1; done"
            }, cancellationToken: token));
            await Ready(name);
        }
        try
        {
            await Check(runtime.TagImageAsync(image!, tag, token));
            await Check(runtime.CreateVolumeAsync(new() { Name = volume }, token));
            await File.WriteAllTextAsync(Path.Combine(root, "sentinel"), "owned", token);
            var mounted = prefix + "-mounts";
            names.Add(mounted);
            var spec = new ContainerCreateSpec
            {
                Image = tag, Name = mounted, PullPolicy = ContainerPullPolicy.Missing, StopTimeoutSeconds = 0,
                Command = "trap '' TERM; touch /tmp/ready; while :; do sleep 1; done"
            };
            spec.Mounts.Add(new(ContainerMountKind.Bind, root, "/bound,\"quoted\"", true));
            spec.Mounts.Add(new(ContainerMountKind.Volume, volume, "/cache-rw"));
            spec.Mounts.Add(new(ContainerMountKind.Volume, volume, "/cache-ro", true));
            spec.Mounts.Add(new(ContainerMountKind.Tmpfs, "", "/memory-rw"));
            spec.Mounts.Add(new(ContainerMountKind.Tmpfs, "", "/memory-ro", true));
            await Check(runtime.RunContainerAsync(spec, cancellationToken: token));
            await Ready(mounted);
            await Check(runtime.ExecAsync(mounted,
                "test -f '/bound,\"quoted\"/sentinel' && ! touch '/bound,\"quoted\"/denied' && touch /cache-rw/allowed && test -f /cache-ro/allowed && ! touch /cache-ro/denied && touch /memory-rw/allowed && ! touch /memory-ro/denied", cancellationToken: token));
            var elapsed = Stopwatch.StartNew();
            await Check(runtime.StopContainerAsync(mounted, token));
            output.WriteLine($"Inherited zero timeout: {elapsed.Elapsed.TotalSeconds:F2}s");
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));

            await Check(runtime.StartContainerAsync(mounted, token));
            await Check(ContainerExportOperation.RunAsync(runtime, mounted, Path.Combine(root, "export.tar"), true, null, token));
            Assert.Contains(await runtime.GetContainersAsync(token), item => item.Name == mounted && item.IsRunning);
            var failedExport = await ContainerExportOperation.RunAsync(runtime, mounted, root, true, null, token);
            Assert.False(failedExport.Success);
            Assert.Contains(await runtime.GetContainersAsync(token), item => item.Name == mounted && item.IsRunning);
            output.WriteLine("Export success and invalid output destination both restored the originally running container.");

            await Launch("-signal", -1, "SIGINT");
            await Check(runtime.StopContainerAsync(prefix + "-signal", token));
            await Launch("-positive", 1, "SIGTERM");
            elapsed.Restart();
            await Check(runtime.StopContainerAsync(prefix + "-positive", token));
            output.WriteLine($"Inherited one second timeout: {elapsed.Elapsed.TotalSeconds:F2}s");
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(800));

            await Launch("-cancel", null, null);
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cancellation.CancelAfter(700);
                await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.StopContainerAsync(prefix + "-cancel", new(-1, "SIGTERM"), cancellation.Token));
            }
            await Check(runtime.StopContainerAsync(prefix + "-cancel", new(0, "SIGKILL"), token));

            // An owned tag exists locally, but --pull=always must still contact the registry.
            // Port 1 on loopback deliberately has no registry; no remote image or user tag is changed.
            names.Add(prefix + "-always");
            var always = await runtime.RunContainerAsync(new() { Image = tag, Name = prefix + "-always", PullPolicy = ContainerPullPolicy.Always }, cancellationToken: token);
            Assert.False(always.Success);
            output.WriteLine($"Always policy ignored cached owned tag and failed to pull from loopback: exit {always.ExitCode}.");

            await Launch("-remove", 0, "SIGTERM", remove: true);
            await Check(runtime.StopContainerAsync(prefix + "-remove", token));
            Assert.DoesNotContain(await runtime.GetContainersAsync(token), item => item.Name == prefix + "-remove");
            output.WriteLine("Verified missing/never cache use, always pull failure, CSV bind path, ro/rw volumes and tmpfs, inherited signal and timeouts, -1 cancellation, and automatic removal.");

            if (int.TryParse(Environment.GetEnvironmentVariable("EXWSLC_LAUNCH_UI_HOLD_SECONDS"), out var seconds) && seconds > 0)
            {
                await Check(runtime.StartContainerAsync(mounted, token));
                output.WriteLine($"UI container: {mounted}; hold {seconds}s");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 1200)), token);
            }
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var errors = new List<string>();
            foreach (var name in names)
            {
                try { await runtime.RemoveContainerAsync(name, true, cleanup.Token); }
                catch (Exception exception) { errors.Add(exception.Message); }
            }
            try
            {
                await runtime.RemoveVolumeAsync(volume, true, cleanup.Token);
                await runtime.RemoveImageAsync(tag, false, cleanup.Token);
                Assert.DoesNotContain(await runtime.GetContainersAsync(cleanup.Token), item => names.Contains(item.Name));
                Assert.DoesNotContain(await runtime.GetVolumesAsync(cleanup.Token), item => item.Name == volume);
                Assert.DoesNotContain(await runtime.GetImagesAsync(cleanup.Token), item => item.Repository + ":" + item.Tag == tag);
            }
            finally
            {
                var ownedRoot = Path.GetDirectoryName(root)!;
                Assert.Equal(prefix, Path.GetFileName(ownedRoot));
                Directory.Delete(ownedRoot, true);
            }
            Assert.Empty(errors);
            output.WriteLine($"Cleaned every container, volume, tag and temporary directory belonging to {prefix}.");
        }
    }
}
