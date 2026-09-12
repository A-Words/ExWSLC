using System.IO;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class ContainerHealthLiveTests
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("EXWSLC_HEALTH_LIVE") == "1";

    [Fact(Skip = "Set EXWSLC_HEALTH_LIVE=1 and EXWSLC_HEALTH_IMAGE to an existing local Linux image containing /bin/sh.", SkipUnless = nameof(IsEnabled))]
    public async Task IsolatedContainers_InheritDisableAndOverrideHealth()
    {
        var image = Environment.GetEnvironmentVariable("EXWSLC_HEALTH_IMAGE");
        Assert.False(string.IsNullOrWhiteSpace(image));
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tag = $"exwslc-03-health-{suffix}:test";
        var directory = Path.Combine(Path.GetTempPath(), $"exwslc-03-{suffix}");
        var names = new List<string>();
        var runner = new WslcProcessRunner();
        var capabilities = new RuntimeCapabilityService(runner, new WslcSdkService());
        var runtime = new WslcContainerRuntime(runner, capabilities);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));
        var token = timeout.Token;
        var snapshot = await capabilities.DetectAsync(token);
        TestContext.Current.TestOutputHelper!.WriteLine($"CLI {snapshot.CliVersion}; service {snapshot.ServiceVersion}; SDK {snapshot.SdkPackageVersion}; OS {Environment.OSVersion}");
        Assert.Equal(CapabilitySupport.Supported, snapshot[RuntimeFeature.HealthChecks].Support);
        Directory.CreateDirectory(directory);
        var imageBuildAttempted = false;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Dockerfile"),
                $"FROM {image}\nHEALTHCHECK --interval=1s --timeout=1s --retries=1 CMD echo inherited-ready\nCMD [\"sleep\",\"600\"]\n", token);
            imageBuildAttempted = true;
            var build = await runtime.BuildImageAsync(directory, tag, "", cancellationToken: token);
            Assert.True(build.Success, build.Error);
            foreach (var mode in new[] { HealthCheckMode.Inherit, HealthCheckMode.Disabled, HealthCheckMode.Custom })
            {
                var name = $"exwslc-03-{mode.ToString().ToLowerInvariant()}-{suffix}";
                names.Add(name); // Also clean up if creation is cancelled after the service creates it.
                var spec = new ContainerCreateSpec { Image = tag, Name = name, HealthMode = mode, Command = "sleep 600" };
                if (mode == HealthCheckMode.Custom)
                {
                    spec.HealthCommand = "echo intentional-health-failure; exit 1";
                    spec.HealthInterval = "1s";
                    spec.HealthTimeout = "1s";
                    spec.HealthStartPeriod = "0s";
                    spec.HealthRetries = "1";
                }
                var run = await runtime.RunContainerAsync(spec, cancellationToken: token);
                Assert.True(run.Success, run.Error);
                var expected = mode switch { HealthCheckMode.Inherit => ContainerHealthStatus.Healthy, HealthCheckMode.Disabled => ContainerHealthStatus.NotConfigured, _ => ContainerHealthStatus.Unhealthy };
                ContainerHealthDetails? health = null;
                for (var attempt = 0; attempt < 45; attempt++)
                {
                    var inspect = await runtime.InspectContainerAsync(name, token);
                    Assert.True(inspect.Success, inspect.Error);
                    Assert.True(ContainerInspectDetailsParser.TryParse(inspect.Output, out var details));
                    health = details.Health;
                    if (health.Status == expected) break;
                    await Task.Delay(1000, token);
                }
                Assert.Equal(expected, health!.Status);
                if (mode != HealthCheckMode.Disabled)
                {
                    Assert.NotEmpty(health.Logs);
                    Assert.Equal(mode == HealthCheckMode.Custom ? 1 : 0, health.Logs[0].ExitCode);
                    Assert.Contains(mode == HealthCheckMode.Custom ? "intentional-health-failure" : "inherited-ready", health.Logs[0].Output);
                }
                var listed = Assert.Single(await runtime.GetContainersAsync(token), container => container.Name == name);
                Assert.Equal(expected, listed.HealthStatus);
                TestContext.Current.TestOutputHelper.WriteLine($"{name}: inspect/list={expected}; failures={health.FailingStreak}; records={health.Logs.Count}");
            }
            var stopped = await runtime.StopContainerAsync(names[0], token);
            Assert.True(stopped.Success, stopped.Error);
            var stoppedRow = Assert.Single(await runtime.GetContainersAsync(token), container => container.Name == names[0]);
            Assert.Equal(ContainerHealthStatus.Unknown, stoppedRow.HealthStatus);
            TestContext.Current.TestOutputHelper.WriteLine($"{names[0]}: stopped list health is Unknown, not NotConfigured");
            // Optional bounded window for manual UI verification; cleanup still runs on cancellation/failure.
            if (int.TryParse(Environment.GetEnvironmentVariable("EXWSLC_HEALTH_UI_HOLD_SECONDS"), out var seconds))
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 600)), token);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var failures = new List<string>();
            foreach (var name in names)
            {
                try
                {
                    var removed = await runtime.RemoveContainerAsync(name, true, cleanup.Token);
                    if (!removed.Success) failures.Add($"container {name}: {removed.Error}");
                }
                catch (Exception error) { failures.Add($"container {name}: {error.Message}"); }
            }
            if (imageBuildAttempted)
            {
                try
                {
                    var removed = await runtime.RemoveImageAsync(tag, false, cleanup.Token);
                    if (!removed.Success) failures.Add($"image {tag}: {removed.Error}");
                }
                catch (Exception error) { failures.Add($"image {tag}: {error.Message}"); }
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Assert.Empty(failures);
        }
    }
}
