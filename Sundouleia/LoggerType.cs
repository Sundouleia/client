namespace Sundouleia;

// Categories of logging.
[Flags]
public enum LoggerType : long
{
    None                = 0L,
    // Achievements (if ever added)
    Achievements        = 1L << 0,
    AchievementEvents   = 1L << 1,
    AchievementInfo     = 1L << 2,

    // Interop 
    IpcSundouleia       = 1L << 3,
    IpcPenumbra         = 1L << 4,
    IpcGlamourer        = 1L << 5,
    IpcCustomize        = 1L << 6,
    IpcMoodles          = 1L << 7,
    IpcHeels            = 1L << 8,
    IpcHonorific        = 1L << 9,
    IpcPetNames         = 1L << 10,

    // Client Player Object Data
    ResourceMonitor     = 1L << 11,
    OwnedObjects        = 1L << 12,
    ClientUpdates       = 1L << 13,
    DataDistributor     = 1L << 14,

    // File Info
    FileCache           = 1L << 15,
    FileCsv             = 1L << 16,
    FileMonitor         = 1L << 17,
    FileCompactor       = 1L << 18,
    FileWatcher         = 1L << 19,
    FileUploads         = 1L << 20,
    FileDownloads       = 1L << 21,
    FileService         = 1L << 22,

    // Pair Data (Pair Handling)
    PairManagement      = 1L << 23, // Adding / removing / updates
    PairDataTransfer    = 1L << 24, // All Data transfer updates.
    PairHandler         = 1L << 25, // Anything related to handled pair object.
    PairMods            = 1L << 26, // Mod info changes.
    PairAppearance      = 1L << 27, // Appearance info changes.

    // Radar Logging
    RadarManagement     = 1L << 28,
    RadarData           = 1L << 29,
    RadarChat           = 1L << 30,

    // General Services
    UIManagement        = 1L << 31,
    Textures            = 1L << 32,
    DtrBar              = 1L << 33,
    Profiles            = 1L << 34,
    Mediator            = 1L << 35,
    Combos              = 1L << 36,

    // WebAPI (SundouleiaHub)
    ApiCore             = 1L << 37,
    Callbacks           = 1L << 38,
    HubFactory          = 1L << 39,
    Health              = 1L << 40,
    JwtTokens           = 1L << 41,

    // All Recommended types.
    Recommended =
        Achievements |
        IpcSundouleia | IpcPenumbra | IpcGlamourer | IpcCustomize | IpcMoodles | IpcHeels | IpcHonorific | IpcPetNames |
        OwnedObjects | DataDistributor |
        FileCache | FileUploads | FileService |
        PairManagement | PairDataTransfer | PairHandler |
        RadarManagement | RadarData |
        DtrBar |
        ApiCore | Callbacks | HubFactory,
}
