namespace ExWSLC.Models;

public sealed class VolumePruneSpec
{
    public bool All { get; set; }
    public List<string> Filters { get; } = [];
}
