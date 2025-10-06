using MessagePack;
using SundouleiaAPI.Data;
using SundouleiaAPI.Enums;
using System.Text.RegularExpressions;

namespace Sundouleia.PlayerClient;

// Just doing this to try and digest the process.
public partial class ModdedFile
{
    public ModdedFile(string[] gamePaths, string filePath)
    {
        GamePaths = gamePaths.Select(g => g.Replace('\\', '/').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        ResolvedPath = filePath.Replace('\\', '/');
    }

    public HashSet<string> GamePaths { get; init; }

    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));

    public string Hash { get; set; } = string.Empty;
    public bool IsFileSwap => !LocalPathRegex().IsMatch(ResolvedPath) && GamePaths.All(p => !LocalPathRegex().IsMatch(p));
    public string ResolvedPath { get; init; }

    public PlaceholderLazyFileData ToFileReplacementDto()
    {
        return new PlaceholderLazyFileData
        {
            GamePaths = [.. GamePaths],
            Hash = Hash,
            FileSwapPath = IsFileSwap ? ResolvedPath : string.Empty,
        };
    }

    public override string ToString()
    {
        return $"HasReplacement:{HasFileReplacement},IsFileSwap:{IsFileSwap} - {string.Join(",", GamePaths)} => {ResolvedPath}";
    }

    [GeneratedRegex(@"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript)]
    private static partial Regex LocalPathRegex();
}