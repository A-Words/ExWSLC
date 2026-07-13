using System.IO;

namespace ExWSLC.Tests;

internal static class TestPaths
{
    public static string SourceDirectory { get; } = Path.Combine(FindRepositoryRoot(), "src");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExWSLC.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the ExWSLC repository root.");
    }
}
