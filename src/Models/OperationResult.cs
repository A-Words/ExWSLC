namespace ExWSLC.Models;

public sealed record OperationResult(
    bool Success,
    int ExitCode,
    string Output,
    string Error,
    string DisplayCommand)
{
    public string CombinedOutput => string.Join(Environment.NewLine,
        new[] { Output, Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
