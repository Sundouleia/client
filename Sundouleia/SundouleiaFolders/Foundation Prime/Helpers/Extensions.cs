using Sundouleia.DrawSystem.Selector;

namespace Sundouleia.DrawSystem;

public static class DFSExtensions
{
    // Prevent .HasFlag overhead
    public static bool HasAny(this FolderFlags flags, FolderFlags check) => (flags & check) != 0;

    // A filesystem name may not contain forward-slashes, as they are used to split paths.
    // The empty string as name signifies the root, so it can also not be used.
    public static string FixName(this string name)
    {
        var fix = name.Replace('/', '\\').Trim();
        return fix.Length == 0 ? "<None>" : fix;
    }

    // Split a path string into directories.
    // Empty entries will be skipped.
    public static string[] SplitDirectories(this string path)
        => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
