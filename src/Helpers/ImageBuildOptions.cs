using System.Text.RegularExpressions;
using ExWSLC.Models;

namespace ExWSLC.Helpers;

public static class ImageBuildOptions
{
    public static string? Validate(ImageBuildRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContextPath)) return "BuildContextRequired";
        if (request.ContextPath.TrimStart().StartsWith('-') || request.Dockerfile.Trim() == "-") return "BuildInvalidOptions";
        if (!Enum.IsDefined(request.Output) || request.Progress is { } mode && !Enum.IsDefined(mode)) return "BuildInvalidOptions";
        if (request.Output == ImageBuildOutput.LocalImage && string.IsNullOrWhiteSpace(request.Tag)) return "BuildTagRequired";
        // Do not allow stdout binary output or CSV syntax to introduce another exporter option.
        if (request.Output == ImageBuildOutput.Tar &&
            (string.IsNullOrWhiteSpace(request.OutputPath) || request.OutputPath.Trim() == "-" ||
             request.OutputPath.IndexOfAny([',', '"', '\r', '\n', '\0']) >= 0)) return "BuildOutputPathRequired";
        if (request.BuildArguments.Any(value => !IsAssignment(value))) return "BuildArgumentsInvalid";
        if (request.Secrets.Select(secret => secret.Id).Distinct(StringComparer.Ordinal).Count() != request.Secrets.Count)
            return "BuildSecretsInvalid";
        foreach (var secret in request.Secrets)
        {
            if (!Regex.IsMatch(secret.Id, @"^[a-zA-Z0-9_.-]+$") || !Enum.IsDefined(secret.SourceType) ||
                string.IsNullOrWhiteSpace(secret.Source) || secret.Source.IndexOfAny([',', '"', '\r', '\n', '\0']) >= 0 ||
                secret.SourceType == BuildSecretSource.Environment && !Regex.IsMatch(secret.Source, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                return "BuildSecretsInvalid";
        }
        return null;
    }

    public static IReadOnlyList<string> Lines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<ImageBuildSecret> ParseSecrets(string files, string environment) =>
        Parse(files, BuildSecretSource.File).Concat(Parse(environment, BuildSecretSource.Environment)).ToArray();

    private static IEnumerable<ImageBuildSecret> Parse(string text, BuildSecretSource type)
    {
        foreach (var line in Lines(text))
        {
            var separator = line.IndexOf('=');
            yield return separator < 1
                ? new ImageBuildSecret(string.Empty, type, string.Empty)
                : new ImageBuildSecret(line[..separator].Trim(), type, line[(separator + 1)..].Trim());
        }
    }

    private static bool IsAssignment(string value) =>
        Regex.IsMatch(value, @"^[a-zA-Z_][a-zA-Z0-9_]*=[^\r\n\0]*$");

    public static IReadOnlyList<string> BuildArguments(ImageBuildRequest request)
    {
        if (Validate(request) is { } error) throw new ArgumentException(error);
        var arguments = new List<string> { "image", "build" };
        if (request.Output == ImageBuildOutput.LocalImage) arguments.AddRange(["--tag", request.Tag.Trim()]);
        if (!string.IsNullOrWhiteSpace(request.Dockerfile)) arguments.AddRange(["--file", request.Dockerfile]);
        foreach (var value in request.BuildArguments) arguments.AddRange(["--build-arg", value]);
        if (!string.IsNullOrWhiteSpace(request.Target)) arguments.AddRange(["--target", request.Target.Trim()]);
        if (request.NoCache) arguments.Add("--no-cache");
        if (request.Pull) arguments.Add("--pull");
        if (request.Output == ImageBuildOutput.Tar) arguments.AddRange(["--output", $"type=tar,dest={request.OutputPath}"]);
        if (request.Progress is { } mode) arguments.AddRange(["--progress", mode.ToString().ToLowerInvariant()]);
        foreach (var secret in request.Secrets)
            arguments.AddRange(["--secret", secret.SourceType == BuildSecretSource.File
                ? $"id={secret.Id},type=file,src={secret.Source}"
                : $"id={secret.Id},type=env,env={secret.Source}"]);
        // WSLC 2.9.10 rejects the conventional -- end-of-options delimiter.
        arguments.Add(request.ContextPath);
        return arguments;
    }
}
