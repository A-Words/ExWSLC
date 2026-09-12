using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class HostLoopbackLiveTests
{
    public static bool Enabled => Environment.GetEnvironmentVariable("EXWSLC_LIVE_HOST_LOOPBACK") == "1";

    [Fact(Skip = "Set EXWSLC_LIVE_HOST_LOOPBACK=1 to create isolated task-09 test containers and a loopback-only TCP listener.", SkipUnless = nameof(Enabled))]
    public async Task SelectedTestContainer_ConnectsToOnlyTheChosenLocalServiceWithoutSendingPayload()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(4));
        var runner = new WslcProcessRunner();
        var runtime = new WslcContainerRuntime(runner);
        var capabilities = await new RuntimeCapabilityService(runner, new WslcSdkService()).DetectAsync(lifetime.Token);
        var configuration = await runtime.GetHostLoopbackConfigurationAsync(lifetime.Token);
        Assert.Equal(HostLoopbackConfiguration.DefaultHostName, configuration.HostName);
        var image = Environment.GetEnvironmentVariable("EXWSLC_HOST_LOOPBACK_TEST_IMAGE") ?? "python:3.13-alpine";
        Assert.True(image is "python:3.13-alpine" or "docker.1ms.run/python:3.13-alpine" or "docker.1ms.run/nginx:latest",
            "Only the task's known diagnostic test images are accepted.");
        var name = "exwslc-09-" + Guid.NewGuid().ToString("N");
        var missingToolsName = name + "-tools";
        var imagesBefore = await runtime.GetImagesAsync(lifetime.Token);
        var imageExisted = imagesBefore.Any(item => item.DisplayName == image ||
            (image == "python:3.13-alpine" && item.DisplayName == "docker.io/library/python:3.13-alpine"));
        var containersToClean = new List<string>();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        var imagePulled = false;
        try
        {
            if (!imageExisted)
            {
                var pull = await runner.ExecuteAsync("wslc.exe", ["image", "pull", image], cancellationToken: lifetime.Token);
                imagePulled = pull.Success;
                Assert.True(pull.Success, $"Isolated test image pull failed (exit {pull.ExitCode}); raw output withheld.");
            }
            containersToClean.Add(name);
            var create = await runner.ExecuteAsync("wslc.exe", ["container", "run", "--detach", "--name", name, image, "/bin/sleep", "180"], cancellationToken: lifetime.Token);
            Assert.True(create.Success, $"Isolated test container creation failed (exit {create.ExitCode}).");
            var container = Assert.Single(await runtime.GetContainersAsync(lifetime.Token), item => item.Name == name);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync(lifetime.Token).AsTask();
            var target = new HostLoopbackProbeRequest(container.Id, configuration.HostName, port);
            var connected = await runtime.ProbeHostLoopbackAsync(target, capabilities, lifetime.Token);
            Assert.Equal(HostLoopbackOutcome.Connected, connected.Outcome);
            Assert.True(connected.DnsSucceeded);
            using (var client = await accept.WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token))
            {
                var buffer = new byte[1];
                Assert.Equal(0, await client.GetStream().ReadAsync(buffer, lifetime.Token));
            }
            var artifacts = Path.Combine(TestPaths.SourceDirectory, "..", "artifacts");
            Directory.CreateDirectory(artifacts);
            await File.WriteAllTextAsync(Path.Combine(artifacts, "wslc-09-live-connected.json"), JsonSerializer.Serialize(connected), lifetime.Token);
            listener.Stop();
            var refused = await runtime.ProbeHostLoopbackAsync(target, capabilities, lifetime.Token);
            Assert.Equal(HostLoopbackOutcome.TcpFailed, refused.Outcome);
            Assert.True(refused.DnsSucceeded);

            containersToClean.Add(missingToolsName);
            create = await runner.ExecuteAsync("wslc.exe", ["container", "run", "--detach", "--name", missingToolsName,
                "--env", "PATH=/exwslc-no-tools", "--entrypoint", "/bin/sleep", image, "180"], cancellationToken: lifetime.Token);
            Assert.True(create.Success);
            var noTools = Assert.Single(await runtime.GetContainersAsync(lifetime.Token), item => item.Name == missingToolsName);
            var missing = await runtime.ProbeHostLoopbackAsync(target with { ContainerId = noTools.Id }, capabilities, lifetime.Token);
            Assert.Equal(HostLoopbackOutcome.ToolsMissing, missing.Outcome);

            var stop = await runtime.StopContainerAsync(container.Id, lifetime.Token);
            Assert.True(stop.Success);
            Assert.Equal(HostLoopbackOutcome.ContainerNotRunning, (await runtime.ProbeHostLoopbackAsync(target, capabilities, lifetime.Token)).Outcome);
            var remove = await runtime.RemoveContainerAsync(container.Id, true, lifetime.Token);
            Assert.True(remove.Success);
            containersToClean.Remove(name);
            Assert.Equal(HostLoopbackOutcome.ContainerUnavailable, (await runtime.ProbeHostLoopbackAsync(target, capabilities, lifetime.Token)).Outcome);
        }
        finally
        {
            listener.Stop();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var failures = new List<string>();
            foreach (var createdName in containersToClean)
            {
                var remove = await runner.ExecuteAsync("wslc.exe", ["container", "remove", "--force", createdName], cancellationToken: cleanup.Token);
                if (!remove.Success)
                {
                    var remaining = await runtime.GetContainersAsync(cleanup.Token);
                    if (remaining.Any(item => item.Name == createdName)) failures.Add("test container cleanup failed");
                }
            }
            if (imagePulled)
            {
                var remove = await runner.ExecuteAsync("wslc.exe", ["image", "remove", image], cancellationToken: cleanup.Token);
                if (!remove.Success) failures.Add("test image cleanup failed");
            }
            Assert.Empty(failures);
        }
    }
}
