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
}
