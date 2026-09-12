using System.Reflection;
using ExWSLC.Models;
using ExWSLC.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ExWSLC.Tests;

public class RuntimeIntegrationTests
{
    [Fact]
    public async Task ApplicationRegistration_ResolvesContainerBuildAndDiagnosticsDependenciesTogether()
    {
        var token = TestContext.Current.CancellationToken;
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var commands = new List<IReadOnlyList<string>>();
        runner.Setup(value => value.ExecuteAsync("wslc.exe", It.IsAny<IReadOnlyList<string>>(), null,
                It.IsAny<IProgress<string>?>(), token))
            .Returns((string _, IReadOnlyList<string> arguments, string? input, IProgress<string>? progress, CancellationToken cancellation) =>
            {
                commands.Add(arguments);
                return Task.FromResult(new OperationResult(true, 0, string.Empty, string.Empty, string.Empty));
            });
        var capabilities = new Mock<IRuntimeCapabilityService>(MockBehavior.Strict);
        capabilities.Setup(value => value.DetectAsync(token)).ReturnsAsync(new RuntimeCapabilities
        {
            Features = new Dictionary<RuntimeFeature, RuntimeFeatureCapability>
            {
                [RuntimeFeature.HealthChecks] = new(CapabilitySupport.Supported, "test", "test")
            }
        });
        var settings = new Mock<IHostLoopbackSettingsReader>(MockBehavior.Strict);
        settings.Setup(value => value.ReadAsync(token)).ReturnsAsync(HostLoopbackConfiguration.Default);
        var services = new ServiceCollection();
        typeof(App).GetMethod("ConfigureServices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [services]);
        services.AddSingleton(runner.Object);
        services.AddSingleton(capabilities.Object);
        services.AddSingleton(settings.Object);
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IContainerRuntime>();

        Assert.True((await runtime.RunContainerAsync(new ContainerCreateSpec
        { Image = "test:local", HealthMode = HealthCheckMode.Disabled }, cancellationToken: token)).Success);
        Assert.True((await runtime.BuildImageAsync(new ImageBuildRequest
        { ContextPath = "context", Tag = "test:local" }, cancellationToken: token)).Success);
        Assert.Equal(HostLoopbackConfiguration.Default, await runtime.GetHostLoopbackConfigurationAsync(token));

        Assert.Contains("--no-healthcheck", commands[0]);
        Assert.Equal(new[] { "image", "build", "--tag", "test:local", "context" }, commands[1]);
        capabilities.VerifyAll();
        settings.VerifyAll();
    }
}
