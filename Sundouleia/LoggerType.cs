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

    // File Info
    FileCache           = 1L << 13,
    FileMonitor         = 1L << 14,
    PairFileCache       = 1L << 15,

    // Pair Data (Pair Handling)
    PairManagement      = 1L << 16, // Adding / removing / updates
    PairDataTransfer    = 1L << 17, // All Data transfer updates.
    PairHandler         = 1L << 18, // Anything related to handled pair object.
    PairMods            = 1L << 19, // Mod info changes.
    PairAppearance      = 1L << 20, // Appearance info changes.
    PairVisibility      = 1L << 21, // Change between visibility states. (For visibility service)

    // Radar Logging
    RadarManagement     = 1L << 22,
    RadarData           = 1L << 23,
    RadarChat           = 1L << 24,

    // General Services
    UIManagement        = 1L << 25,
    Textures            = 1L << 26,
    DtrBar              = 1L << 27,
    Profiles            = 1L << 28,
    Mediator            = 1L << 29,
    Combos              = 1L << 30,

    // WebAPI (SundouleiaHub)
    ApiCore             = 1L << 31,
    Callbacks           = 1L << 32,
    HubFactory          = 1L << 33,
    Health              = 1L << 34,
    JwtTokens           = 1L << 35,

    // All Recommended types.
    Recommended =
        Achievements |
        IpcSundouleia | IpcPenumbra | IpcGlamourer | IpcCustomize | IpcMoodles | IpcHeels | IpcHonorific | IpcPetNames |
        OwnedObjects |
        FileCache |
        PairManagement | PairDataTransfer | PairHandler | PairVisibility |
        RadarManagement | RadarData |
        DtrBar |
        ApiCore | Callbacks | HubFactory,
}
