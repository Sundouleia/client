using System.Collections.Immutable;

namespace Sundouleia;

public static class Constants
{
    public const int Blake3HashLength = 64;
    // Default, Unchangeable Tags used for base Sundesmo grouping.
    public const string FolderTagAll            = "All Sundesmos";
    public const string FolderTagVisible        = "Visible";
    public const string FolderTagOnline         = "Online";
    public const string FolderTagOffline        = "Offline";
    // Radar
    public const string FolderTagRadarPaired    = "Paired";
    public const string FolderTagPendingRequest = "Pending Requests";
    public const string FolderTagRadarUnpaired  = "Unpaired";
    // Requests
    public const string FolderTagRequestInc= "Incoming Requests";
    public const string FolderTagRequestPending = "Pending Requests";

    // CachePrefixes
    public const string SMAFolderName = "ModularActorCache";
    public const string PrefixCache = "{cache}";
    public const string PrefixPenumbra = "{penumbra}";
    public const string CsvSplit = "|";
    // File Filtering.
    public static readonly IImmutableList<string> SMAExtensions     = [".smad", ".smab", ".smao", ".smai", ".smaip"];
    public static readonly IImmutableList<string> ValidExtensions   = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];
    public static readonly IEnumerable<string> HandledExtensions    = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    public static readonly IEnumerable<string> MdlMtrlTexExtensions = ["tex", "mdl", "mtrl"];
}
