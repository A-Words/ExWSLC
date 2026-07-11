using ExWSLC.Helpers;
using ExWSLC.Models;

namespace ExWSLC.Tests;

public class ContainerMountDetailsParserTests
{
    [Fact]
    public void TryParse_ReadsKnownAndForwardCompatibleMountTypesFromArrayPayload()
    {
        // WSLC 2.9.3 does not expose named volumes in Mounts; keep a volume item here to verify
        // forward compatibility if a later Inspect version reports them.
        const string inspect = """
            [
              {
                "Mounts": [
                  {
                    "Type": "bind",
                    "Source": "C:\\workspace\\data",
                    "Destination": "/workspace/data",
                    "ReadWrite": true,
                    "FutureField": 42
                  },
                  {
                    "Type": "volume",
                    "Source": "cache-data",
                    "Destination": "/var/cache/app",
                    "ReadWrite": false
                  },
                  {
                    "Type": "tmpfs",
                    "Source": "",
                    "Destination": "/run/app",
                    "ReadWrite": "true"
                  },
                  {
                    "Type": "future-mount",
                    "Source": "opaque-source",
                    "Destination": "/future",
                    "ReadWrite": "unknown"
                  }
                ]
              }
            ]
            """;

        var parsed = ContainerMountDetailsParser.TryParse(inspect, out var details);

        Assert.True(parsed);
        Assert.True(details.HasMounts);
        Assert.Equal(4, details.Mounts.Count);

        var bind = details.Mounts[0];
        Assert.Equal(ContainerMountKind.Bind, bind.Kind);
        Assert.Equal(ContainerMountAccess.ReadWrite, bind.Access);
        Assert.Equal(@"C:\workspace\data", bind.DisplaySource);
        Assert.Equal("/workspace/data", bind.DisplayDestination);
        Assert.True(bind.HasSource);
        Assert.True(bind.HasDestination);

        var volume = details.Mounts[1];
        Assert.Equal(ContainerMountKind.Volume, volume.Kind);
        Assert.Equal(ContainerMountAccess.ReadOnly, volume.Access);

        var tmpfs = details.Mounts[2];
        Assert.Equal(ContainerMountKind.Tmpfs, tmpfs.Kind);
        Assert.Equal(ContainerMountAccess.ReadWrite, tmpfs.Access);
        Assert.False(tmpfs.HasSource);
        Assert.Equal("-", tmpfs.DisplaySource);

        var unknown = details.Mounts[3];
        Assert.Equal("future-mount", unknown.Type);
        Assert.Equal(ContainerMountKind.Unknown, unknown.Kind);
        Assert.Equal(ContainerMountAccess.Unknown, unknown.Access);
    }

    [Fact]
    public void TryParse_IsCaseInsensitiveAndAcceptsStringReadWrite()
    {
        const string inspect = """
            {
              "mOuNtS": [
                {
                  "tYpE": "BIND",
                  "sOuRcE": "C:\\src",
                  "dEsTiNaTiOn": "/src",
                  "rEaDwRiTe": "false"
                }
              ]
            }
            """;

        var parsed = ContainerMountDetailsParser.TryParse(inspect, out var details);

        Assert.True(parsed);
        var mount = Assert.Single(details.Mounts);
        Assert.Equal(ContainerMountKind.Bind, mount.Kind);
        Assert.Equal(ContainerMountAccess.ReadOnly, mount.Access);
    }

    [Fact]
    public void TryParse_AllowsEmptyMountArray()
    {
        var parsed = ContainerMountDetailsParser.TryParse("""{ "Mounts": [] }""", out var details);

        Assert.True(parsed);
        Assert.False(details.HasMounts);
        Assert.Empty(details.Mounts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{ \"Mounts\": null }")]
    [InlineData("{ \"Mounts\": {} }")]
    public void TryParse_ReturnsFalseForUnsupportedPayload(string inspect)
    {
        Assert.False(ContainerMountDetailsParser.TryParse(inspect, out _));
    }

    [Fact]
    public void ContainerMount_ProvidesFallbackDisplayValues()
    {
        var mount = new ContainerMount("", "", "", null);

        Assert.Equal(ContainerMountKind.Unknown, mount.Kind);
        Assert.Equal(ContainerMountAccess.Unknown, mount.Access);
        Assert.False(mount.HasSource);
        Assert.False(mount.HasDestination);
        Assert.Equal("-", mount.DisplaySource);
        Assert.Equal("-", mount.DisplayDestination);
    }
}
