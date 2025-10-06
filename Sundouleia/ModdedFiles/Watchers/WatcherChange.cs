namespace Sundouleia.ModFiles;

/// <summary>
///     Record detailing the changes of a monitored file.
/// </summary>
public record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);