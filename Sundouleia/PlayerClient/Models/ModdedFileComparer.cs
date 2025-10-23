using Sundouleia.PlayerClient;

namespace SundouleiaAPI.Data;

public class ModdedFileComparer : IEqualityComparer<ModdedFile>
{
    private static readonly ModdedFileComparer _instance = new();

    private ModdedFileComparer()
    { }

    public static ModdedFileComparer Instance => _instance;

    public bool Equals(ModdedFile? x, ModdedFile? y)
    {
        if (x == null || y == null) return false;
        return x.ResolvedPath.Equals(y.ResolvedPath) && CompareLists(x.GamePaths, y.GamePaths);
    }

    public int GetHashCode(ModdedFile obj)
    {
        return HashCode.Combine(obj.ResolvedPath.GetHashCode(StringComparison.OrdinalIgnoreCase), GetOrderIndependentHashCode(obj.GamePaths));
    }

    private static bool CompareLists(HashSet<string> list1, HashSet<string> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        for (int i = 0; i < list1.Count; i++)
        {
            if (!string.Equals(list1.ElementAt(i), list2.ElementAt(i), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static int GetOrderIndependentHashCode<T>(IEnumerable<T> source) where T : notnull
    {
        int hash = 0;
        foreach (T element in source)
        {
            hash = unchecked(hash +
                EqualityComparer<T>.Default.GetHashCode(element));
        }
        return hash;
    }
}

public class ModdedFileHashComparer : IEqualityComparer<ModdedFile>
{
    private static readonly ModdedFileHashComparer _instance = new();

    private ModdedFileHashComparer()
    { }

    public static ModdedFileHashComparer Instance => _instance;

    public bool Equals(ModdedFile? x, ModdedFile? y)
    {
        if (x == null || y == null) return false;
        return x.Hash.Equals(y.Hash);
    }

    public int GetHashCode(ModdedFile obj)
    {
        return HashCode.Combine(obj.ResolvedPath.GetHashCode(StringComparison.OrdinalIgnoreCase), GetOrderIndependentHashCode(obj.GamePaths));
    }

    private static int GetOrderIndependentHashCode<T>(IEnumerable<T> source) where T : notnull
    {
        int hash = 0;
        foreach (T element in source)
        {
            hash = unchecked(hash +
                EqualityComparer<T>.Default.GetHashCode(element));
        }
        return hash;
    }
}