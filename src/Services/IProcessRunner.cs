using ExWSLC.Models;

namespace ExWSLC.Services;

public interface IProcessRunner
{
    Task<OperationResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
