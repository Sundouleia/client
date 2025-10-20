using System.Collections.Immutable;

namespace Sundouleia;

public static class Constants
{
    public const int Blake3HashLength = 64;
    public const int SundesmoTimeoutSeconds = 7;
    // Default, Unchangeable Tags used for base Sundesmo grouping.
    public const string CustomAllTag = "Sundouleia_All";
    public const string CustomOfflineTag = "Sundouleia_Offline";
    public const string CustomOnlineTag = "Sundouleia_Online";
    public const string CustomVisibleTag = "Sundouleia_Visible";
    // CachePrefixes
    public const string PrefixCache = "{cache}";
    public const string PrefixPenumbra = "{penumbra}";
    public const string CsvSplit = "|";
    // File Filtering.
    public static readonly IImmutableList<string> ValidExtensions   = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];
    public static readonly IEnumerable<string> HandledExtensions   = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    public static readonly IEnumerable<string> RecordingExtensions = ["tex", "mdl", "mtrl"];
}
