using System.Globalization;
using System.Text.RegularExpressions;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

internal static partial class HealthCheckOptions
{
    public static void Validate(ContainerCreateSpec spec)
    {
        if (!Enum.IsDefined(spec.HealthMode)) Fail("HealthInvalidMode", "Choose inherit, disable, or custom health checks.");
        var values = new[] { spec.HealthCommand, spec.HealthInterval, spec.HealthTimeout, spec.HealthStartPeriod, spec.HealthRetries };
        if (spec.HealthMode != HealthCheckMode.Custom)
        {
            if (values.Any(value => value is not null))
                Fail("HealthConflict", "Clear custom health fields or select Custom. Inherit and Disable cannot include overrides.");
            return;
        }

        if (spec.HealthCommand is not null && (string.IsNullOrWhiteSpace(spec.HealthCommand) || spec.HealthCommand.Contains('\0')))
            Fail("HealthInvalidCommand", "Enter a health command, or leave it empty to inherit the image command.");
        if (values.All(value => value is null))
            Fail("HealthCustomEmpty", "Enter a command or at least one override, or select Inherit.");
        foreach (var (name, value) in new[] { ("--health-interval", spec.HealthInterval), ("--health-timeout", spec.HealthTimeout), ("--health-start-period", spec.HealthStartPeriod) })
        {
            if (value is not null && !TryParseDuration(value, out _))
                throw new ArgumentException(name + ": " + LocalizationService.GetString("HealthInvalidDuration",
                    "Use a nonnegative duration such as 30s, 1m30s or 500ms, within 9223372036854775807 ns."));
        }
        if (spec.HealthRetries is not null && (!int.TryParse(spec.HealthRetries, NumberStyles.None, CultureInfo.InvariantCulture, out var retries) || retries < 0))
            Fail("HealthInvalidRetries", "Retries must be an integer from 0 to 2147483647. Leave empty to inherit.");
    }

    public static void RequireSupport(CapabilitySupport support)
    {
        if (support != CapabilitySupport.Supported)
            Fail("HealthUnsupported", "Health overrides are unavailable or not detected. Select Inherit, or re-detect the runtime in Settings.");
    }

    public static bool TryParseDuration(string text, out long nanoseconds)
    {
        nanoseconds = 0;
        if (text is "0" or "+0" or "-0") return true;
        if (text.StartsWith('+')) text = text[1..];
        if (text.Length == 0 || text.Length > 128) return false;
        decimal total = 0;
        var position = 0;
        foreach (Match match in DurationPart().Matches(text))
        {
            if (match.Index != position || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number)) return false;
            var multiplier = match.Groups[2].Value switch
            {
                "ns" => 1m, "us" or "µs" or "μs" => 1000m, "ms" => 1000000m,
                "s" => 1000000000m, "m" => 60000000000m, "h" => 3600000000000m, _ => 0m
            };
            if (number > long.MaxValue / multiplier) return false;
            total += number * multiplier;
            if (total > long.MaxValue) return false;
            position += match.Length;
        }
        if (position != text.Length) return false;
        nanoseconds = (long)decimal.Round(total, 0, MidpointRounding.AwayFromZero);
        return true;
    }

    private static void Fail(string key, string fallback) => throw new ArgumentException(LocalizationService.GetString(key, fallback));

    [GeneratedRegex(@"([0-9]+(?:\.[0-9]*)?|\.[0-9]+)(ns|us|µs|μs|ms|s|m|h)", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();
}
