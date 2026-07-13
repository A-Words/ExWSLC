namespace ExWSLC.Models;

public sealed record ContainerInspectDetails(
    string Id,
    ContainerInspectConfig Config,
    IReadOnlyList<ContainerKeyValue> EnvironmentVariables,
    string RawJson)
{
    public bool HasEnvironmentVariables => EnvironmentVariables.Count > 0;
}

public sealed record ContainerInspectConfig(IReadOnlyList<string> Command)
{
    public bool HasCommand => Command.Count > 0;
    public string DisplayCommand => Command.Count == 0
        ? "--"
        : string.Join(" ", Command.Select(QuoteWhenNeeded));

    private static string QuoteWhenNeeded(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
}

public sealed record ContainerKeyValue(string Key, string Value)
{
    public string DisplayValue => string.IsNullOrEmpty(Value) ? "\"\"" : Value;
}
