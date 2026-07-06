using System.Collections.ObjectModel;

namespace ExWSLC.Helpers;

public static class ObservableCollectionExtensions
{
    public static void ReplaceAll<T>(this ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
