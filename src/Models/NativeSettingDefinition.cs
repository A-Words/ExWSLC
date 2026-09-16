using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ExWSLC.Models;

public sealed record NativeSettingDefinition(string Path, string Key, string Kind, string[] Choices)
{
    public static IReadOnlyList<NativeSettingDefinition> All { get; } =
    [
        new("session.cpuCount", "Cpu", "Number", []),
        new("session.memorySize", "Memory", "Text", []),
        new("session.maxStorageSize", "Disk", "Text", []),
        new("session.defaultBindingAddress", "Address", "Text", []),
        new("session.hostLoopback", "Host", "Text", []),
        new("session.idleTimeout", "Idle", "Number", []),
        new("session.storagePath", "Storage", "Text", []),
        new("credentialStore", "Credentials", "Choice", ["wincred", "file"]),
        new("session.networkingMode", "Network", "Choice", ["consomme", "nat", "none"]),
        new("session.hostFileShareMode", "Sharing", "Choice", ["virtiofs", "plan9"]),
        new("session.dnsTunneling", "Dns", "Toggle", []),
        new("experimental.portRelay", "Relay", "Choice", ["virtionet", "wslrelay"])
    ];

    public bool IsValid(string value)
    {
        if (value == "default") return true;
        if (Kind == "Number") return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;
        if (Kind == "Choice") return Choices.Contains(value);
        if (Kind == "Toggle") return value is "true" or "false";
        if (Key is "Memory" or "Disk")
        {
            var match = Regex.Match(value, @"\A([0-9]+)(MB|GB|TB)\z", RegexOptions.IgnoreCase);
            if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out var size)) return false;
            var unit = match.Groups[2].Value.ToUpperInvariant();
            var multiplier = unit == "TB" ? 1048576UL : unit == "GB" ? 1024UL : 1UL;
            return size > 0 && size <= uint.MaxValue / multiplier;
        }
        if (Key == "Address") return value.Split('.') is { Length: 4 } parts && parts.All(part =>
            part.Length is > 0 and <= 3 && part.All(char.IsAsciiDigit) && byte.TryParse(part, out _));
        if (Key == "Host") return value == "none" || Helpers.HostLoopbackTargetValidator.IsHostName(value);
        if (Key == "Storage") return !string.IsNullOrWhiteSpace(value) && System.IO.Path.IsPathFullyQualified(value) && value.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0;
        return false;
    }
}
