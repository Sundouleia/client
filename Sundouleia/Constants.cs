using System.Collections.Immutable;
using System.Windows.Forms;

namespace Sundouleia;

public static class Constants
{
    public const int Blake3HashLength = 64;
    public const int SundesmoTimeoutSeconds = 7;
    // Default, Unchangeable Tags used for base Sundesmo grouping.
    public const string FolderTagAllDragDrop    = "All Sundesmos (For Drag-Drop)";
    public const string FolderTagAll            = "All Sundesmos";
    public const string FolderTagVisible        = "Visible";
    public const string FolderTagOnline         = "Online";
    public const string FolderTagOffline        = "Offline";
    public const string FolderTagRadarPaired    = "Paired";
    public const string FolderTagRadarUnpaired  = "Unpaired";
    public const string FolderTagRequestIncoming= "Incoming Requests";
    public const string FolderTagRequestPending = "Pending Requests";
    // CachePrefixes
    public const string PrefixCache = "{cache}";
    public const string PrefixPenumbra = "{penumbra}";
    public const string CsvSplit = "|";
    // File Filtering.
    public static readonly IImmutableList<string> ValidExtensions   = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];
    public static readonly IEnumerable<string> HandledExtensions   = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    public static readonly IEnumerable<string> MdlMtrlTexExtensions = ["tex", "mdl", "mtrl"];
}
