using System.Collections.Immutable;

namespace Sundouleia;

public static class Constants
{
    public const int Blake3HashLength = 64;
    public const int SundesmoTimeoutSeconds = 7;
    // Default, Unchangeable Tags used for base Sundesmo grouping.
    public const string FolderTagAll            = "Sundouleia_All";
    public const string FolderTagVisible        = "Sundouleia_Visible";
    public const string FolderTagOnline         = "Sundouleia_Online";
    public const string FolderTagOffline        = "Sundouleia_Offline";
    public const string FolderTagRadarPaired    = "Sundouleia_Radar_Paired";
    public const string FolderTagRadarUnpaired  = "Sundouleia_Radar_Unpaired";
    public const string FolderTagRequestIncoming= "Sundouleia_Request_Incoming";
    public const string FolderTagRequestPending = "Sundouleia_Request_Pending";
    // CachePrefixes
    public const string PrefixCache = "{cache}";
    public const string PrefixPenumbra = "{penumbra}";
    public const string CsvSplit = "|";
    // File Filtering.
    public static readonly IImmutableList<string> ValidExtensions   = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];
    public static readonly IEnumerable<string> HandledExtensions   = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    public static readonly IEnumerable<string> MdlMtrlTexExtensions = ["tex", "mdl", "mtrl"];
}
