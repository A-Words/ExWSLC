using System.IO;
using System.Text.Json;
using ExWSLC.Services;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ExWSLC.Helpers;

internal static class NativeSettingsYaml
{
    private static YamlMappingNode? Parse(string text)
    {
        if (!NativeSettingsStore.IsValid(text)) throw new InvalidDataException();
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count == 0 ? null : (YamlMappingNode)stream.Documents[0].RootNode;
    }

    public static string Read(string text, string path)
    {
        YamlNode? node = Parse(text);
        foreach (var segment in path.Split('.'))
        {
            if (node is null || IsNull(node)) return "default";
            if (node is not YamlMappingNode map) throw new InvalidDataException();
            map.Children.TryGetValue(new YamlScalarNode(segment), out node);
        }
        if (node is null || IsNull(node)) return "default";
        return node is YamlScalarNode scalar ? scalar.Value ?? "default" : throw new InvalidDataException();
    }

    private static bool IsNull(YamlNode node) => node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" };

    public static string Update(string text, string path, string value)
    {
        var root = Parse(text);
        var segments = path.Split('.');
        // JSON quoted strings are valid YAML scalars, including paths and Unicode.
        var encoded = JsonSerializer.Serialize(value);
        string Nested(int index) => index == segments.Length ? encoded : "{" + segments[index] + ": " + Nested(index + 1) + "}";
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        if (root is null) return text + newline + segments[0] + ": " + Nested(1) + newline;
        YamlNode current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            if (IsNull(current)) return Replace(text, current, Nested(index));
            if (current is not YamlMappingNode map) throw new InvalidDataException();
            if (!map.Children.TryGetValue(new YamlScalarNode(segments[index]), out var child))
            {
                var entry = segments[index] + ": " + Nested(index + 1);
                var start = checked((int)map.Start.Index);
                if (map.Style == MappingStyle.Flow)
                    return text.Insert(start + 1, entry + (map.Children.Count > 0 ? ", " : ""));
                return text.Insert(start, entry + newline + new string(' ', checked((int)map.Start.Column) - 1));
            }
            if (index == segments.Length - 1) return Replace(text, child, encoded);
            current = child;
        }
        throw new InvalidDataException();
    }

    private static string Replace(string text, YamlNode node, string value)
    {
        var start = checked((int)node.Start.Index);
        var length = checked((int)(node.End.Index - node.Start.Index));
        // Block scalar spans include their final line break(s). Keep that source
        // whitespace so the following key or comment stays on its own line.
        if (node is YamlScalarNode { Style: ScalarStyle.Literal or ScalarStyle.Folded })
        {
            while (length > 0 && char.IsWhiteSpace(text[start + length - 1])) length--;
        }
        if (length == 0 && start > 0 && text[start - 1] == ':') value = " " + value;
        return text.Remove(start, length).Insert(start, value);
    }
}
