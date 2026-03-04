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
    IpcLoci          = 1L << 7,
    IpcHeels            = 1L << 8,
    IpcHonorific        = 1L << 9,
    IpcPetNames         = 1L << 10,

    // Client Player Object Data
    ResourceMonitor     = 1L << 11,
    PlayerMods          = 1L << 12,
    MinionMods          = 1L << 13,
    PetMods             = 1L << 14,
    CompanionMods       = 1L << 15,
    OwnedObjects        = 1L << 16,
    ClientUpdates       = 1L << 17,
    DataDistributor     = 1L << 18,

    // File Info
    FileCache           = 1L << 19,
    FileCsv             = 1L << 20,
    FileMonitor         = 1L << 21,
    FileCompactor       = 1L << 22,
    FileWatcher         = 1L << 23,
    FileUploads         = 1L << 24,
    FileDownloads       = 1L << 25,
    FileService         = 1L << 26,

    // Pair Data (Pair Handling)
    PairManagement      = 1L << 27, // Adding / removing / updates
    PairDataTransfer    = 1L << 28, // All Data transfer updates.
    PairHandler         = 1L << 29, // Anything related to handled pair object.
    PairMods            = 1L << 30, // Mod info changes.
    PairAppearance      = 1L << 31, // Appearance info changes.

    // Radar Logging
    RadarManagement     = 1L << 32,
    RadarData           = 1L << 33,
    RadarChat           = 1L << 34,

    // General Services
    UIManagement        = 1L << 35,
    Textures            = 1L << 36,
    DtrBar              = 1L << 37,
    Profiles            = 1L << 38,
    Mediator            = 1L << 39,
    Combos              = 1L << 40,

    // WebAPI (SundouleiaHub)
    ApiCore             = 1L << 41,
    Callbacks           = 1L << 42,
    HubFactory          = 1L << 43,
    Health              = 1L << 44,
    JwtTokens           = 1L << 45,

    // Loci Services
    LociMemory          = 1L << 46,
    LociProcessors      = 1L << 47,
    LociData            = 1L << 48,
    LociIpc             = 1L << 49,
    LociSheVfx          = 1L << 50,

    // SMA Serviecs
    SmaManagment        = 1L << 51,
    SmaHandling         = 1L << 52,
    SmaGpose            = 1L << 53,
    SmaTransfer         = 1L << 54,
    SmaImportExport     = 1L << 55,


    // All Recommended types.
    Recommended =
        Achievements |
        IpcSundouleia | IpcPenumbra | IpcGlamourer | IpcCustomize | IpcLoci | IpcHeels | IpcHonorific | IpcPetNames |
        OwnedObjects | DataDistributor |
        FileCache | FileUploads | FileService |
        PairManagement | PairDataTransfer | PairHandler |
        RadarManagement | RadarData |
        DtrBar |
        ApiCore | Callbacks | HubFactory,
}
