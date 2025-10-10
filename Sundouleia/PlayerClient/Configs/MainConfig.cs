using CkCommons.HybridSaver;
using Sundouleia.Gui.Components;
using Sundouleia.Services;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class ConfigStorage
{
    public Version? LastRunVersion { get; set; } = null;
    public string LastUidLoggedIn { get; set; } = ""; // This eventually wont madder once we index via keys instead of UID's

    // used for detecting if in first install.
    public bool AcknowledgementUnderstood { get; set; } = false;
    public bool ButtonUsed { get; set; } = false;

    // File Info
    public bool InitialScanComplete { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public bool CompactCache { get; set; } = true;
    // Ideally we can remove this if our cleanup function works properly.
    // Which it should, because if we are using radars it better be lol.
    public double MaxCacheInGiB { get; set; } = 20;
    public string CacheScanComplete { get; set; } = string.Empty;
    public int MaxParallelDownloads { get; set; } = 10;
    public int DownloadLimitBytes { get; set; } = 0;
    public DownloadSpeeds DownloadSpeedType { get; set; } = DownloadSpeeds.MBps;
    // could add variables for the transfer bars but Idk if I really want to bother
    // with this, or if we even can detect it with our system we are developing.

    // Used to retain compatibility with existing (M)CDF export logic.
    public string ExportFolderCDF { get; set; } = string.Empty;


    // Radar Preferences
    public bool RadarSendPings { get; set; } = false; // If others can send you requests vis context menus.
    public bool RadarNearbyDtr { get; set; } = true;
    public bool RadarJoinChats { get; set; } = true;
    public bool RadarChatUnreadDtr { get; set; } = false;
    public bool RadarShowUnreadBubble { get; set; } = true;

    // UI Options
    public MainMenuTabs.SelectedTab MainUiTab { get; set; } = MainMenuTabs.SelectedTab.Whitelist;
    public bool OpenUiOnStartup { get; set; } = true;
    public bool ShowProfiles { get; set; } = true;
    public bool AllowNSFW { get; set; } = false;
    public float ProfileDelay { get; set; } = 1.5f;


    // pair listing preferences. This will have a long overhaul, as preferences
    // will mean very little once we can make custom group containers.
    public bool PreferNicknamesOverNames { get; set; } = false;
    public bool ShowVisibleUsersSeparately { get; set; } = true;
    public bool ShowOfflineUsersSeparately { get; set; } = true;
    public bool ShowContextMenus { get; set; } = true;
    public bool FocusTargetOverTarget { get; set; } = false;

    // Notification preferences
    public bool OnlineNotifications { get; set; } = true;
    public bool NotifyLimitToNickedPairs { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Both;
    public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;
    public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;
}

public class MainConfig : IHybridSavable
{
    private readonly ILogger<MainConfig> _logger;
    private readonly HybridSaveService _saver;
    [JsonIgnore] public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    [JsonIgnore] public HybridSaveType SaveType => HybridSaveType.Json;
    public int ConfigVersion => 0;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.MainConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Config"] = JObject.FromObject(Current),
            ["LogLevel"] = LogLevel.ToString(),
            ["Filters"] = JToken.FromObject(LoggerFilters)
        }.ToString(Formatting.Indented);
    }

    public MainConfig(ILogger<MainConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.MainConfig;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            _saver.Save(this);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

            // Load instance configuration
        Current = jObject["Config"]?.ToObject<ConfigStorage>() ?? new ConfigStorage();

        // Load static fields safely
        LogLevel = Enum.TryParse(jObject["LogLevel"]?.Value<string>(), out LogLevel lvl) ? lvl : LogLevel.Trace;

        // Handle outdated hash set format, and new format for log filters.
        var token = jObject["Filters"];
        if(token is JArray array)
        {
            var list = array.ToObject<List<LoggerType>>() ?? new List<LoggerType>();
            LoggerFilters = list.Aggregate(LoggerType.None, (acc, val) => acc | val);
        }
        else
        {
            LoggerFilters = token?.ToObject<LoggerType>() ?? LoggerType.Recommended;
        }

        Save();
    }

    public ConfigStorage Current { get; private set; } = new();
    public static LogLevel LogLevel = LogLevel.Trace;
    public static LoggerType LoggerFilters = LoggerType.Recommended;
}
