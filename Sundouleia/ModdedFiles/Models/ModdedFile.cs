using SundouleiaAPI.Data;
using System.Text.RegularExpressions;

namespace Sundouleia.ModFiles;

/// <summary>
///     Primarily intended to help with efficient modded state calculations, 
///     ensuring it holds the correct state for updates. <para />
///     
///     These files are only used for collecting mod information and being 
///     stored in the ClientDataCache. They are not used elsewhere.
/// </summary>
public partial class ModdedFile
{
    public ModdedFile(string[] gamePaths, string filePath)
    {
        GamePaths = gamePaths.Select(g => g.Replace('\\', '/').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        ResolvedPath = filePath.Replace('\\', '/');
    }

    public ModdedFile(ModdedFile other)
    {
        GamePaths = new HashSet<string>(other.GamePaths, StringComparer.Ordinal);
        ResolvedPath = other.ResolvedPath;
        Hash = other.Hash;
    }

    public HashSet<string> GamePaths { get; init; }
    public string ResolvedPath { get; init; }
    public string Hash { get; set; } = string.Empty;
    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));
    public bool IsFileSwap => !LocalPathRegex().IsMatch(ResolvedPath) && GamePaths.All(p => !LocalPathRegex().IsMatch(p));

    public ModFile ToModFileDto()
        => new ModFile(Hash, GamePaths.ToArray(), IsFileSwap ? ResolvedPath : string.Empty);

    public override string ToString()
        => $"{(HasFileReplacement ? "Replaces" : "NoReplace")} | {(IsFileSwap ? "FileSwap" : "NoSwap")} | {string.Join(",", GamePaths)} => {ResolvedPath}";

    [GeneratedRegex(@"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript)]
    private static partial Regex LocalPathRegex();
}