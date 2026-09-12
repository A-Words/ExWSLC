using System.IO;
using ExWSLC.Models;
using ExWSLC.Services;

namespace ExWSLC.Helpers;

internal static class ContainerCopyOptions
{
    public static string Text(string key, string fallback) => LocalizationService.GetString(key, fallback);

    public static void Validate(ContainerCopyRequest request)
    {
        if (!Enum.IsDefined(request.Direction) || string.IsNullOrWhiteSpace(request.ContainerId) ||
            request.ContainerId.Length < 2 || request.ContainerId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')) ||
            !char.IsAsciiLetterOrDigit(request.ContainerId[0]))
            throw new ArgumentException(Text("CopyInvalidTarget", "Choose a container and a copy direction."));

        // Drive paths are unambiguous to this CLI. UNC, device paths and alternate data streams are not supported here.
        var local = request.LocalPath;
        if (string.IsNullOrWhiteSpace(local) || local.Length < 3 || !char.IsAsciiLetter(local[0]) || local[1] != ':' ||
            local[2] is not ('\\' or '/') || local[2..].Contains(':') || local.Any(c => char.IsControl(c) || c is '"' or '<' or '>' or '|' or '?' or '*') ||
            local.Split('\\', '/').Any(segment => segment is "." or "..") || local.TrimEnd('\\', '/').Length == 2)
            throw new ArgumentException(Text("CopyLocalAbsolute", "Choose an absolute Windows drive path below the drive root, without dot segments or alternate data streams."));

        var remote = request.ContainerPath;
        if (string.IsNullOrWhiteSpace(remote) || !remote.StartsWith('/') || remote.Any(char.IsControl) ||
            remote.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException(Text("CopyContainerAbsolute", "Enter an absolute container path without . or .. segments."));

        if (request.Direction == ContainerCopyDirection.Upload)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(local));
            // The current CLI passes this basename to tar without an option terminator.
            if (string.IsNullOrEmpty(name) || name.StartsWith('-'))
                throw new ArgumentException(Text("CopyUploadName", "Choose a file or directory whose name does not start with a hyphen; drive roots cannot be uploaded."));
            if (!File.Exists(local) && !Directory.Exists(local))
                throw new ArgumentException(Text("CopySourceMissing", "The local source does not exist or cannot be accessed."));
        }
        else if (!Directory.Exists(local))
        {
            throw new ArgumentException(Text("CopyDestinationDirectory", "Choose an existing local destination directory."));
        }
    }

    public static IReadOnlyList<string> BuildArguments(ContainerCopyRequest request)
    {
        Validate(request);
        var local = Path.TrimEndingDirectorySeparator(request.LocalPath);
        if (request.Direction == ContainerCopyDirection.Download)
            return ["container", "cp", $"{request.ContainerId}:{request.ContainerPath}", local + Path.DirectorySeparatorChar];
        return ["container", "cp", local, $"{request.ContainerId}:{request.ContainerPath}"];
    }
}
