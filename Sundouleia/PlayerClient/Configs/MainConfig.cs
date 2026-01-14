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
    public double MaxCacheInGiB { get; set; } = 20;
    public string CacheScanComplete { get; set; } = string.Empty;
    [JsonIgnore] public string SMACacheFolder => Path.Combine(CacheFolder, Constants.SMAFolderName);

    // Tab Selection Memory
    public MainMenuTabs.SelectedTab CurMainUiTab { get; set; } = MainMenuTabs.SelectedTab.Whitelist;
    public InteractionTabs.SelectedTab CurInteractionsTab { get; set; } = InteractionTabs.SelectedTab.Interactions;
    public GroupEditorTabs.SelectedTab CurGroupEditTab { get; set; } = GroupEditorTabs.SelectedTab.Arranger;
    // General
    public bool OpenUiOnStartup { get; set; } = true;
    public bool ShowContextMenus { get; set; } = true;
    public bool ShowProfiles { get; set; } = true;
    public float ProfileDelay { get; set; } = 1.5f;
    public bool AllowNSFW { get; set; } = false;

    // General - Radar
    public bool RadarEnabled { get; set; } = true;
    public bool RadarSendPings { get; set; } = true; // If others can send you requests vis context menus.
    public bool RadarJoinChats { get; set; } = true;
    public bool RadarNearbyDtr { get; set; } = true;
    public bool RadarChatUnreadDtr { get; set; } = false;
    public bool RadarShowUnreadBubble { get; set; } = true;

    // General - Sundouleia Modular Actor Files (Holds close relation with stored fileData...)
    public string SMAExportFolder { get; set; } = string.Empty;

    // Preferences - Downloads
    public int DownloadLimitBytes { get; set; } = 0;
    public DownloadSpeeds DownloadSpeedType { get; set; } = DownloadSpeeds.MBps;
    public int MaxParallelDownloads { get; set; } = 10;
    public bool ShowUploadingText { get; set; } = false;
    public bool TransferWindow { get; set; } = false;
    public bool TransferBars { get; set; } = true;
    public bool TransferBarText { get; set; } = true;
    public int TransferBarHeight { get; set; } = 30;
    public int TransferBarWidth { get; set; } = 250;

    // Preferences - Notifier
    public bool OnlineNotifications { get; set; } = true;
    public bool NotifyLimitToNickedPairs { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Toast;
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
