using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

internal static partial class ContainerLaunchOptions
{
    public static int? ParseTimeout(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var timeout) || timeout < -1)
            Fail("StopOptionsInvalidTimeout", "Enter whole seconds from 0 to 2147483647, -1 for unlimited, or leave empty to inherit.");
        return timeout;
    }

    public static void ValidateStop(ContainerStopOptions options)
    {
        if (options.TimeoutSeconds < -1)
            Fail("StopOptionsInvalidTimeout", "Enter whole seconds from 0 to 2147483647, -1 for unlimited, or leave empty to inherit.");
        if (options.Signal is not null && !SignalPattern().IsMatch(options.Signal))
            Fail("StopOptionsInvalidSignal", "Enter a signal name such as SIGTERM, or a Linux signal number from 1 to 31.");
    }

    public static IReadOnlyList<string> BuildStopArguments(string id, ContainerStopOptions options)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Container is required.");
        ValidateStop(options);
        var arguments = new List<string> { "container", "stop" };
        if (options.TimeoutSeconds is { } timeout) arguments.AddRange(["--time", timeout.ToString(CultureInfo.InvariantCulture)]);
        if (options.Signal is { } signal) arguments.AddRange(["--signal", signal]);
        arguments.Add(id);
        return arguments;
    }

    public static void AddCreateArguments(ContainerCreateSpec spec, List<string> arguments)
    {
        ValidateStop(new(spec.StopTimeoutSeconds, spec.StopSignal));
        if (!Enum.IsDefined(spec.PullPolicy)) Fail("CreateOptionsInvalidPull", "Choose the runtime default, always, missing, or never.");
        if (spec.PullPolicy != ContainerPullPolicy.Inherit)
            arguments.AddRange(["--pull", spec.PullPolicy.ToString().ToLowerInvariant()]);
        if (spec.StopTimeoutSeconds is { } timeout) arguments.AddRange(["--stop-timeout", timeout.ToString(CultureInfo.InvariantCulture)]);
        if (spec.StopSignal is { } signal) arguments.AddRange(["--stop-signal", signal]);

        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var volume in spec.Volumes.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            // Match WSLC ParseDockerVolumeString: find the target from the right,
            // stripping only a trailing :ro/:rw. Never split Windows drive letters.
            var end = volume.EndsWith(":ro", StringComparison.Ordinal) || volume.EndsWith(":rw", StringComparison.Ordinal) ? volume.Length - 3 : volume.Length;
            var separator = volume.LastIndexOf(':', end - 1);
            if (separator <= 0) Fail("MountEditorInvalidSimple", "Use source:/target[:ro|rw], one mount per line.");
            AddTarget(volume[(separator + 1)..end], targets);
        }
        foreach (var mount in spec.Mounts)
        {
            if (!Enum.IsDefined(mount.Kind) || mount.Kind == ContainerMountKind.Unknown) Fail("MountEditorInvalidKind", "Choose bind, named volume, or tmpfs.");
            AddTarget(mount.Target, targets);
            if (mount.Kind == ContainerMountKind.Tmpfs)
            {
                if (mount.Source.Length != 0) Fail("MountEditorTmpfsSource", "Tmpfs has no source. Clear the source field.");
                // --tmpfs uses target[:options], not CSV; a colon in a target cannot be represented.
                if (mount.Target.Contains(':')) Fail("MountEditorTmpfsTarget", "A tmpfs target cannot contain a colon.");
                arguments.AddRange(["--tmpfs", mount.Target + (mount.ReadOnly ? ":ro" : ":rw")]);
                continue;
            }
            if (string.IsNullOrWhiteSpace(mount.Source)) Fail("MountEditorSourceRequired", "Enter a bind path or named volume source.");
            if (mount.Kind == ContainerMountKind.Volume && !VolumeNamePattern().IsMatch(mount.Source))
                Fail("MountEditorInvalidVolume", "Volume names require at least two letters/digits, dots, underscores or hyphens, starting with a letter or digit.");
            if (mount.Kind == ContainerMountKind.Bind && (!Path.IsPathFullyQualified(mount.Source) ||
                mount.Source.Any(character => char.IsControl(character) || "\"<>|*?".Contains(character)) || mount.Source.Skip(2).Contains(':')))
                Fail("MountEditorInvalidBind", "Enter an absolute Windows drive or UNC path without invalid filename characters.");
            // WSLC SplitCsvFields accepts quoted fields and doubled quotes (Docker CSV).
            // Quote the complete key=value field; backslashes are ordinary characters.
            var fields = new[] { "type=" + mount.Kind.ToString().ToLowerInvariant(), "source=" + mount.Source, "target=" + mount.Target, "readonly=" + (mount.ReadOnly ? "true" : "false") };
            arguments.AddRange(["--mount", string.Join(',', fields.Select(CsvField))]);
        }
    }

    public static IEnumerable<RuntimeFeature> RequiredFeatures(ContainerCreateSpec spec)
    {
        if (spec.PullPolicy != ContainerPullPolicy.Inherit) yield return RuntimeFeature.CreatePullPolicy;
        if (spec.StopSignal is not null) yield return RuntimeFeature.CreateStopSignal;
        if (spec.StopTimeoutSeconds is not null) yield return RuntimeFeature.CreateStopTimeout;
        if (spec.Mounts.Any(mount => mount.Kind != ContainerMountKind.Tmpfs)) yield return RuntimeFeature.CreateMount;
        if (spec.Mounts.Any(mount => mount.Kind == ContainerMountKind.Tmpfs)) yield return RuntimeFeature.CreateTmpfs;
    }

    public static void RequireSupport(RuntimeCapabilities? capabilities, IEnumerable<RuntimeFeature> features)
    {
        if (features.Any(feature => capabilities?[feature].Support != CapabilitySupport.Supported))
            Fail("CreateOptionsUnavailable", "One or more selected options are unavailable or unverified. Clear those overrides or recheck the runtime in Settings.");
    }

    private static void AddTarget(string target, HashSet<string> targets)
    {
        if (!target.StartsWith('/') || target.IndexOfAny(['\0', '\r', '\n']) >= 0)
            Fail("MountEditorInvalidTarget", "Enter an absolute Linux container path.");
        // Same lexical normalization as WSLC NormalizeDestination; Linux is case-sensitive.
        var parts = new List<string>();
        foreach (var part in target.Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        if (!targets.Add('/' + string.Join('/', parts))) Fail("MountEditorDuplicateTarget", "Two mounts resolve to the same container target.");
    }

    private static string CsvField(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0 && !value.EndsWith(' ') ? value : '"' + value.Replace("\"", "\"\"") + '"';
    private static void Fail(string key, string fallback) => throw new ArgumentException(LocalizationService.GetString(key, fallback));

    [GeneratedRegex(@"\A(?:[1-9]|[12][0-9]|3[01]|(?:SIG)?(?:HUP|INT|QUIT|ILL|TRAP|ABRT|IOT|BUS|FPE|KILL|USR1|SEGV|USR2|PIPE|ALRM|TERM|TKFLT|CHLD|CONT|STOP|TSTP|TTIN|TTOU|URG|XCPU|XFSZ|VTALRM|PROF|WINCH|IO|POLL|PWR|SYS))\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignalPattern();
    [GeneratedRegex(@"\A[a-zA-Z0-9][a-zA-Z0-9_.-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeNamePattern();
}
