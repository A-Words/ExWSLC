using System.IO;
using System.Text;
using ExWSLC.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ExWSLC.Services;

internal sealed class NativeSettingsStore(string path)
{
    internal const int MaxCharacters = 65536;
    internal const string Template = "# https://aka.ms/wslc-settings\n# Remove the # before a setting to override its default.\nsession:\n  # cpuCount: default\n  # memorySize: default\n  # maxStorageSize: default\n  # defaultBindingAddress: default\n# credentialStore: wincred\n";

    public async Task<NativeSettingsDocument> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, true);
            var buffer = new char[MaxCharacters + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), token);
            if (length > MaxCharacters) throw new InvalidDataException();
            return new(new string(buffer, 0, length), true);
        }
        catch (FileNotFoundException) { return new(Template, false); }
        catch (DirectoryNotFoundException) { return new(Template, false); }
    }

    internal static bool IsValid(string text)
    {
        if (text.Length > MaxCharacters) return false;
        try
        {
            var parser = new Parser(new StringReader(text));
            var depth = 0;
            var count = 0;
            while (parser.MoveNext())
            {
                if (++count > 8192 || parser.Current is AnchorAlias) return false;
                if (parser.Current is MappingStart or SequenceStart && ++depth > 32) return false;
                if (parser.Current is MappingEnd or SequenceEnd) depth--;
            }
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));
            if (yaml.Documents.Count == 0) return true;
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root) return false;
            return ValidateMapping(root);
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException) { return false; }
    }

    private static bool ValidateMapping(YamlMappingNode map) => map.Children.All(pair =>
        pair.Key is YamlScalarNode { Value: not "<<" } &&
        (pair.Value is not YamlMappingNode child || ValidateMapping(child)));

    public async Task<string> SaveAsync(NativeSettingsDocument original, string text, CancellationToken token)
    {
        if (!IsValid(text)) return "NativeConfigInvalid";
        var current = await ReadAsync(token);
        if (current != original) return "NativeConfigConflict";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), token);
            // Check again after preparing the replacement, preserving external edits.
            if (await ReadAsync(token) != original) return "NativeConfigConflict";
            token.ThrowIfCancellationRequested();
            if (original.Exists) File.Replace(temporary, path, path + ".exwslc.bak");
            else File.Move(temporary, path);
            return "NativeConfigSaved";
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
