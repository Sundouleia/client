namespace Sundouleia.ModularActor;

// Represents a modded file entry.
public record FileModData(IEnumerable<string> GamePaths, int Length, string FileHash);

// Represents a vanilla file swap entry.
public record FileSwap(IEnumerable<string> GamePaths, string FileSwapPath);
