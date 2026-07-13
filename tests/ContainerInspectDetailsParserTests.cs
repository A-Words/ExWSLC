using ExWSLC.Helpers;

namespace ExWSLC.Tests;

public class ContainerInspectDetailsParserTests
{
    private const string InspectPayload = """
        {
          "Id": "container-id",
          "Name": "web",
          "Created": "2026-07-11T04:30:00Z",
          "Image": "nginx:latest",
          "State": {
            "Status": "running",
            "Running": true,
            "ExitCode": 0,
            "StartedAt": "2026-07-11T04:31:00Z",
            "FinishedAt": "0001-01-01T00:00:00Z",
            "Health": { "Status": "healthy", "FailingStreak": 0 }
          },
          "HostConfig": {
            "NetworkMode": "bridge",
            "Memory": 536870912,
            "NanoCpus": "1500000000",
            "Ulimits": [{ "Name": "nofile", "Soft": 1024, "Hard": 2048 }]
          },
          "Config": {
            "Env": ["MODE=production", "EMPTY=", "FLAG"],
            "Cmd": ["nginx", "-g", "daemon off;"],
            "Entrypoint": ["/docker-entrypoint.sh"],
            "User": "1000:1000",
            "WorkingDir": "/srv/app",
            "StopTimeout": 10,
            "Healthcheck": {
              "Test": ["CMD", "curl", "-f", "http://localhost/health"],
              "Interval": "30s",
              "Timeout": "3s",
              "StartPeriod": "5s",
              "Retries": 3
            }
          },
          "Labels": { "com.example.owner": "platform", "revision": 7 }
        }
        """;

    [Fact]
    public void TryParse_MapsDocumentedInspectConfiguration()
    {
        var parsed = ContainerInspectDetailsParser.TryParse(InspectPayload, out var details);

        Assert.True(parsed);
        Assert.Equal("container-id", details.Id);
        Assert.Equal("nginx -g \"daemon off;\"", details.Config.DisplayCommand);

        Assert.Collection(details.EnvironmentVariables,
            item => Assert.Equal(("MODE", "production"), (item.Key, item.Value)),
            item => Assert.Equal(("EMPTY", string.Empty), (item.Key, item.Value)),
            item => Assert.Equal(("FLAG", string.Empty), (item.Key, item.Value)));
        Assert.Contains(Environment.NewLine, details.RawJson);
    }

    [Fact]
    public void TryParse_AcceptsSingleItemArrayAndCaseInsensitiveProperties()
    {
        const string payload = """[{ "id": "wrapped", "config": { "command": ["echo", "hello world"] } }]""";

        Assert.True(ContainerInspectDetailsParser.TryParse(payload, out var details));
        Assert.Equal("wrapped", details.Id);
        Assert.Equal("echo \"hello world\"", details.Config.DisplayCommand);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void TryParse_RejectsMissingOrUnsupportedPayload(string payload)
    {
        Assert.False(ContainerInspectDetailsParser.TryParse(payload, out _));
    }
}
