using System.IO;
using System.Text.RegularExpressions;
using ExWSLC.Models;

namespace ExWSLC.Services;

internal sealed class BuildOutputRedactor(IReadOnlyList<string> values)
{
    public static async Task<BuildOutputRedactor> CreateAsync(IReadOnlyList<ImageBuildSecret> secrets, CancellationToken token)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var secret in secrets)
            {
                token.ThrowIfCancellationRequested();
                var value = secret.SourceType == BuildSecretSource.File
                    ? await File.ReadAllTextAsync(secret.Source, token)
                    : Environment.GetEnvironmentVariable(secret.Source);
                if (value is null) throw new InvalidOperationException();
                if (value.Length == 0) continue;
                values.Add(value);
                foreach (var line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) values.Add(line);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException(LocalizationService.GetString("BuildSecretSourceFailed", "Cannot read a build secret source. Check the file or environment variable."));
        }
        return new BuildOutputRedactor(values.OrderByDescending(value => value.Length).ToArray());
    }

    public string Clean(string text)
    {
        // Remove terminal styling without interpreting BuildKit's step numbers as percentages.
        text = Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        foreach (var value in values) text = text.Replace(value, "[REDACTED]", StringComparison.Ordinal);
        return text;
    }
}
