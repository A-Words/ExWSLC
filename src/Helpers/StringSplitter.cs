namespace ExWSLC.Helpers;

public static class StringSplitter
{
    public static IEnumerable<string> SplitValues(string value) =>
        value.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IEnumerable<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
