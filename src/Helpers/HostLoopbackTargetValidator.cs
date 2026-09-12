using System.Net;
using System.Text.RegularExpressions;

namespace ExWSLC.Helpers;

public static partial class HostLoopbackTargetValidator
{
    public static bool IsContainerId(string value) => ContainerIdPattern().IsMatch(value);
    public static bool IsPort(int value) => value is >= 1 and <= 65535;
    public static bool IsHostName(string value) => value.Length is > 0 and <= 253 &&
        !IPAddress.TryParse(value, out _) && (value.EndsWith('.') ? value[..^1] : value).Split('.').All(label => LabelPattern().IsMatch(label));

    [GeneratedRegex(@"\A[0-9a-fA-F]{12,64}\z")]
    private static partial Regex ContainerIdPattern();

    [GeneratedRegex(@"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\z")]
    private static partial Regex LabelPattern();
}
