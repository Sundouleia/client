using CkCommons.GarblerCore;
using Sundouleia.Services;

namespace Sundouleia.PlayerClient;
public class SundouleiaConfig
{
    public Version? LastRunVersion { get; set; } = null;
    public string LastUidLoggedIn { get; set; } = ""; // This eventually wont madder once we index via keys instead of UID's

    // used for detecting if in first install.
    public bool AcknowledgementUnderstood { get; set; } = false;
    public bool ButtonUsed { get; set; } = false;

    // File Info
    public string CacheFolder { get; set; } = string.Empty;
    // Ideally we can remove this if our cleanup function works properly.
    // Which it should, because if we are using radars it better be lol.
    public string MaxCacheInGiB { get; set; } = "20"; 
    public string CacheScanComplete { get; set; } = string.Empty;
    public int MaxParallelDownloads { get; set; } = 10;
    // could add variables for the transfer bars but Idk if I really want to bother
    // with this, or if we even can detect it with our system we are developing.

    // DTR bar preferences
    public bool RadarDtr { get; set; } = true;
    /* can add more here overtime */

    // pair listing preferences. This will have a long overhaul, as preferences
    // will mean very little once we can make custom group containers.
    public bool PreferNicknamesOverNames { get; set; } = false;
    public bool ShowVisibleUsersSeparately { get; set; } = true;
    public bool ShowOfflineUsersSeparately { get; set; } = true;
    public bool ShowContextMenus { get; set; } = true;
    public bool FocusTargetOverTarget { get; set; } = false;

    // UI Preferences.
    public bool OpenUiOnStartup { get; set; } = true;
    public bool ShowProfiles { get; set; } = true;
    public float ProfileDelay { get; set; } = 1.5f;

    // Notification preferences
    public bool NotifyForServerConnections { get; set; } = true;
    public bool NotifyForOnlinePairs { get; set; } = true;
    public bool NotifyLimitToNickedPairs { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Both;
    public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;
    public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;

    // For Thumbnail Folder Browsing
    public float FileIconScale { get; set; } = 1.0f; // File Icon Scale
}

