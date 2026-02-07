namespace Sundouleia;

/// <summary>
///     The Command definition outlines the CLI structure of: <para />
///     
///     /PREFIX <Entity> <Action> <PositionalArgs> [--Parameter] [-Flag]
/// </summary>
public sealed class CommandDefinition
{
    public string Entity { get; init; } = string.Empty; // "group", "folder", "request"
    public IReadOnlyList<string> Action { get; init; } = Array.Empty<string>(); // "create", "add", "move", etc.
    public IReadOnlyList<string> PositionalArgs { get; init; } = Array.Empty<string>();
    public IReadOnlySet<string> Parameters { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();
}