using System.Net;
using System.Text.Json;
using ExWSLC.Helpers;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Tests;

public class NetworkOperationsLiveTests
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("EXWSLC_NETWORK_LIVE") == "1";

    [Fact(Skip = "Set EXWSLC_NETWORK_LIVE=1 and EXWSLC_NETWORK_IMAGE to an existing local Linux image with /bin/sh.", SkipUnless = nameof(IsEnabled))]
    public async Task IsolatedNetworks_CreateConnectConflictAndDisconnect()
    {
        var image = Environment.GetEnvironmentVariable("EXWSLC_NETWORK_IMAGE");
        Assert.False(string.IsNullOrWhiteSpace(image));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));
        var token = timeout.Token;
        var runner = new WslcProcessRunner();
        var capability = new RuntimeCapabilityService(runner, new WslcSdkService());
        var runtime = new WslcContainerRuntime(runner, capability);
        var snapshot = await capability.DetectAsync(token);
        TestContext.Current.TestOutputHelper!.WriteLine($"CLI {snapshot.CliVersion}; service {snapshot.ServiceVersion}; SDK {snapshot.SdkPackageVersion}");
        var occupied = new List<IPNetwork>();
        foreach (var network in await runtime.GetNetworksAsync(token))
        {
            var inspect = await runtime.InspectResourceAsync("network", network.Name, token);
            Assert.True(inspect.Success, inspect.Error);
            using var data = JsonDocument.Parse(inspect.Output);
            var configs = data.RootElement[0].GetProperty("IPAM").GetProperty("Config");
            if (configs.ValueKind != JsonValueKind.Array) continue;
            foreach (var config in configs.EnumerateArray())
                if (config.TryGetProperty("Subnet", out var subnet) && IPNetwork.TryParse(subnet.GetString(), out var block)) occupied.Add(block);
        }
        var octet = Enumerable.Range(0, 256).OrderBy(_ => Guid.NewGuid()).First(value =>
            !occupied.Any(network => network.Contains(IPAddress.Parse($"10.203.{value}.0")) || IPNetwork.Parse($"10.203.{value}.0/24").Contains(network.BaseAddress)));
        var prefix = $"10.203.{octet}";
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var origin = $"exwslc-04-origin-{suffix}";
        var target = $"exwslc-04-target-{suffix}";
        var containers = new List<string>();
        var networks = new List<string>();
        try
        {
            networks.Add(target);
            var spec = new NetworkCreateSpec { Name = target, Subnet = prefix + ".0/24", Gateway = prefix + ".1", IpRange = prefix + ".128/25" };
            spec.DriverOptions.Add("com.docker.network.driver.mtu=1400");
            spec.Labels.Add("exwslc.task=04");
            var created = await runtime.CreateNetworkAsync(spec, token);
            Assert.True(created.Success, created.Error);
            networks.Add(origin);
            Assert.True((await runtime.CreateNetworkAsync(new() { Name = origin }, token)).Success);
            var networkInspect = await runtime.InspectResourceAsync("network", target, token);
            using (var data = JsonDocument.Parse(networkInspect.Output))
            {
                var root = data.RootElement[0];
                var config = root.GetProperty("IPAM").GetProperty("Config")[0];
                Assert.Equal(spec.Subnet, config.GetProperty("Subnet").GetString());
                Assert.Equal(spec.Gateway, config.GetProperty("Gateway").GetString());
                Assert.Equal(spec.IpRange, config.GetProperty("IPRange").GetString());
                Assert.Equal("04", root.GetProperty("Labels").GetProperty("exwslc.task").GetString());
                Assert.Equal("1400", root.GetProperty("Options").GetProperty("com.docker.network.driver.mtu").GetString());
            }
            foreach (var role in new[] { "client", "conflict" })
            {
                var name = $"exwslc-04-{role}-{suffix}";
                containers.Add(name);
                var run = await runtime.RunContainerAsync(new() { Name = name, Image = image!, Network = origin, Command = "sleep 600" }, cancellationToken: token);
                Assert.True(run.Success, run.Error);
            }
            var connect = new NetworkConnectionSpec(containers[0], target, prefix + ".140", ["exwslc-api", "exwslc-secondary"],
                ["com.docker.network.endpoint.sysctls=net.ipv4.conf.IFNAME.log_martians=1"]);
            var connected = await runtime.ConnectNetworkAsync(connect, token);
            Assert.True(connected.Success, connected.Error);
            var inspected = await runtime.InspectContainerAsync(containers[0], token);
            Assert.True(ContainerNetworkDetailsParser.TryParse(inspected.Output, out var details));
            var attachment = Assert.Single(details.Networks, network => network.Name == target);
            Assert.Equal(connect.Ipv4Address, attachment.IpAddress);
            Assert.Contains("exwslc-api", attachment.Aliases);
            Assert.Contains("exwslc-secondary", attachment.Aliases);
            var conflict = await runtime.ConnectNetworkAsync(connect with { ContainerId = containers[1] }, token);
            Assert.False(conflict.Success);
            TestContext.Current.TestOutputHelper.WriteLine($"{target}: subnet={spec.Subnet}; gateway={spec.Gateway}; range={spec.IpRange}; {containers[0]}: IP={attachment.IpAddress}; aliases={attachment.DisplayAliases}; endpoint option accepted; conflict={conflict.Error}");
            if (int.TryParse(Environment.GetEnvironmentVariable("EXWSLC_NETWORK_UI_HOLD_SECONDS"), out var seconds))
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 600)), token);
            var disconnected = await runtime.DisconnectNetworkAsync(new(containers[0], target), token);
            Assert.True(disconnected.Success, disconnected.Error);
            inspected = await runtime.InspectContainerAsync(containers[0], token);
            Assert.True(ContainerNetworkDetailsParser.TryParse(inspected.Output, out details));
            Assert.DoesNotContain(details.Networks, network => network.Name == target);
            Assert.Contains(details.Networks, network => network.Name == origin);
            TestContext.Current.TestOutputHelper.WriteLine($"{containers[0]}: target detached; origin retained");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var failures = new List<string>();
            foreach (var name in containers)
            {
                try { var result = await runtime.RemoveContainerAsync(name, true, cleanup.Token); if (!result.Success) failures.Add(result.Error); }
                catch (Exception error) { failures.Add(error.Message); }
            }
            foreach (var name in networks.AsEnumerable().Reverse())
            {
                try { var result = await runtime.RemoveNetworkAsync(name, cleanup.Token); if (!result.Success) failures.Add(result.Error); }
                catch (Exception error) { failures.Add(error.Message); }
            }
            Assert.Empty(failures);
        }
    }
}
