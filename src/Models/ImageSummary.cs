using System.Globalization;

namespace ExWSLC.Models;

public sealed record ImageSummary(
    string Id,
    string Repository,
    string Tag,
    string Size,
    string Created)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Tag) || Tag == "<none>"
        ? Repository
        : $"{Repository}:{Tag}";

    public string DisplaySize => FormatSize(Size);
    public string DisplayCreated => FormatCreated(Created);

    internal static string FormatSize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";
        if (!decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0)
        {
            return value;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;
        while (bytes >= 1000 && unitIndex < units.Length - 1)
        {
            bytes /= 1000;
            unitIndex++;
        }

        return $"{bytes.ToString(unitIndex == 0 ? "0" : "0.##", CultureInfo.CurrentCulture)} {units[unitIndex]}";
    }

    internal static string FormatCreated(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "-";

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return value;
            }
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var createdAt))
        {
            return createdAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }

        return value;
    }
}
