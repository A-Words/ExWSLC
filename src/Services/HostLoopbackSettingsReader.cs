using System.IO;
using ExWSLC.Helpers;
using ExWSLC.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ExWSLC.Services;

public sealed class HostLoopbackSettingsReader : IHostLoopbackSettingsReader
{
    private const int MaxCharacters = 65536;

    public async Task<HostLoopbackConfiguration> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only this user's documented native path. Never call `wslc settings`, which
        // can create a file, or inspect another user's profile to discover settings.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wslc", "settings.yaml");
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[MaxCharacters + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return length > MaxCharacters ? HostLoopbackConfiguration.Unknown : Parse(new string(buffer, 0, length));
        }
        catch (FileNotFoundException) { return HostLoopbackConfiguration.Default; }
        catch (DirectoryNotFoundException) { return HostLoopbackConfiguration.Default; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HostLoopbackConfiguration.Unknown;
        }
    }

    internal static HostLoopbackConfiguration Parse(string yaml)
    {
        if (yaml.Length > MaxCharacters) return HostLoopbackConfiguration.Unknown;
        try
        {
            var parser = new Parser(new StringReader(yaml));
            var depth = 0;
            var events = 0;
            while (parser.MoveNext())
            {
                // Bound representation-model recursion and avoid cyclic aliases.
                if (++events > 8192 || parser.Current is AnchorAlias) return HostLoopbackConfiguration.Unknown;
                if (parser.Current is MappingStart or SequenceStart && ++depth > 32) return HostLoopbackConfiguration.Unknown;
                if (parser.Current is MappingEnd or SequenceEnd) depth--;
            }
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0) return HostLoopbackConfiguration.Default;
            if (stream.Documents.Count != 1) return HostLoopbackConfiguration.Unknown;
            var root = stream.Documents[0].RootNode;
            if (IsNull(root)) return HostLoopbackConfiguration.Default;
            if (root is not YamlMappingNode map) return HostLoopbackConfiguration.Unknown;
            if (map.Children.ContainsKey(new YamlScalarNode("<<"))) return HostLoopbackConfiguration.Unknown;
            if (!map.Children.TryGetValue(new YamlScalarNode("session"), out var session) || IsNull(session))
                return HostLoopbackConfiguration.Default;
            if (session is not YamlMappingNode settings) return HostLoopbackConfiguration.Unknown;
            // yaml-cpp does not apply YAML merge keys here. Reject ambiguity instead
            // of interpreting a configuration differently from the native loader.
            if (settings.Children.ContainsKey(new YamlScalarNode("<<")))
                return HostLoopbackConfiguration.Unknown;
            if (!settings.Children.TryGetValue(new YamlScalarNode("hostLoopback"), out var value) || IsNull(value))
                return HostLoopbackConfiguration.Default;
            if (value is not YamlScalarNode scalar) return HostLoopbackConfiguration.Unknown;
            return scalar.Value switch
            {
                "default" => HostLoopbackConfiguration.Default,
                "none" => new(CapabilitySupport.Unsupported, string.Empty, "HostConfigDisabled"),
                { } host when HostLoopbackTargetValidator.IsHostName(host) => new(CapabilitySupport.Supported, host, "HostConfigCustom"),
                _ => HostLoopbackConfiguration.Unknown
            };
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException)
        {
            return HostLoopbackConfiguration.Unknown;
        }
    }

    private static bool IsNull(YamlNode node) => node is YamlScalarNode { Style: ScalarStyle.Plain } scalar &&
        scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";
}
