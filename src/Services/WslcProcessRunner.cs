using System.Diagnostics;
using System.Text;
using ExWSLC.Models;

namespace ExWSLC.Services;

public sealed class WslcProcessRunner : IProcessRunner
{
    public async Task<OperationResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Append(args.Data, stdout, progress);
        process.ErrorDataReceived += (_, args) => Append(args.Data, stderr, progress);

        var displayCommand = BuildDisplayCommand(fileName, arguments);
        try
        {
            if (!process.Start())
            {
                return new OperationResult(false, -1, string.Empty, "Failed to start process.", displayCommand);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput);
                process.StandardInput.Close();
            }

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            });

            await process.WaitForExitAsync(cancellationToken);
            return new OperationResult(
                process.ExitCode == 0,
                process.ExitCode,
                stdout.ToString().TrimEnd(),
                stderr.ToString().TrimEnd(),
                displayCommand);
        }
        catch (OperationCanceledException)
        {
            return new OperationResult(false, -2, stdout.ToString().TrimEnd(), "Operation cancelled.", displayCommand);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new OperationResult(false, -1, stdout.ToString().TrimEnd(), exception.Message, displayCommand);
        }
    }

    internal static string BuildDisplayCommand(string fileName, IReadOnlyList<string> arguments)
    {
        static string Quote(string value) => value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

        return string.Join(' ', new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));
    }

    private static void Append(string? line, StringBuilder target, IProgress<string>? progress)
    {
        if (line is null)
        {
            return;
        }

        target.AppendLine(line);
        progress?.Report(line);
    }
}
