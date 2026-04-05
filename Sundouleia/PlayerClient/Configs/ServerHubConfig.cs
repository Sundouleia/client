using CkCommons.HybridSaver;
using FFXIVClientStructs.FFXIV.Common.Lua;
using Sundouleia.Services.Configs;
using Sundouleia.WebAPI;

namespace Sundouleia.PlayerClient;

public class ServerHubInfo
{
    public string HubUri { get; set; } = string.Empty;
    public string HubName { get; set; } = string.Empty;
}

public class ServerHubConfig : IHybridSavable
{
    public const string MAIN_SERVER_NAME = "Sundouleia Main";
    public const string MAIN_SERVER_URI = "wss://sundouleia.kinkporium.studio";

    public const string DEV_SERVER_NAME = "Sundouleia Dev";
    public const string DEV_SERVER_URI = "wss://sundouleia-dev.kinkporium.studio";
     
    private static readonly List<ServerHubInfo> OfficialHubs =
    [
        new ServerHubInfo
        {
            HubName = MAIN_SERVER_NAME,
            HubUri  = MAIN_SERVER_URI
        },
        new ServerHubInfo
        {
            HubName = DEV_SERVER_NAME,
            HubUri  = DEV_SERVER_URI
        }
    ];

    private readonly ILogger<ServerHubConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.ServerConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["LastConnectedURI"] = LastJoinedUri,
            ["LastLoggedInUID"] = LastLoggedInUID,
            ["SelectedHub"] = ChosenHubIndex,
            ["ServerHubs"] = JArray.FromObject(ServerHubs),
        }.ToString(Formatting.Indented);
    }
    public ServerHubConfig(ILogger<ServerHubConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    // Actual saved data
    public string LastJoinedUri { get; set; } = MAIN_SERVER_URI;
    public string LastLoggedInUID { get; set; } = string.Empty;
    
    public static int ChosenHubIndex { get; private set; } = 0;
    public static List<ServerHubInfo> ServerHubs { get; private set; } = OfficialHubs;
    
    // Quick access to the current hub info.
    public static ServerHubInfo CurrentHub => ServerHubs[ChosenHubIndex];
    public static string CurrentHubName => CurrentHub.HubName;
    public static string CurrentHubUri => CurrentHub.HubUri;

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.ServerConfig;
        _logger.LogInformation($"Loading in ServerConfig: {file}");
        if (!File.Exists(file))
        {
            _logger.LogWarning($"ServerConfig file not found: {file}");
            _saver.Save(this);
            return;
        }

        // Do not try-catch these, invalid loads of these should not allow the plugin to load.
        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        // Load additional fields safely.
        LastJoinedUri = jObject["LastConnectedURI"]?.Value<string>() ?? MAIN_SERVER_URI;
        LastLoggedInUID = jObject["LastLoggedInUID"]?.Value<string>() ?? string.Empty;
        ChosenHubIndex = jObject["SelectedHub"]?.Value<int>() ?? 0;
        ServerHubs = jObject["ServerHubs"]?.ToObject<List<ServerHubInfo>>() ?? OfficialHubs;

        // Validate hub index.
        if (ChosenHubIndex < 0 || ChosenHubIndex >= ServerHubs.Count)
        {
            // Move back to the last selected index. However, if 1, and not in devmode, move to 0.
            var newIdx = Math.Clamp(ChosenHubIndex, 0, ServerHubs.Count - 1);
#if !DEBUG
            if (newIdx is 1) newIdx = 0;
#endif
            _logger.LogWarning($"ChosenHubIndex {ChosenHubIndex} is out of range. Resetting to {newIdx}.");
            ChosenHubIndex = newIdx;
        }

        Save();
    }

    public void SetHubIndex(int newIdx)
    {
        if (newIdx < 0 || newIdx >= ServerHubs.Count)
        {
            newIdx = Math.Clamp(newIdx, 0, ServerHubs.Count - 1);
#if !DEBUG
                if (newIdx is 1) newIdx = 0;
#endif
        }
        ChosenHubIndex = newIdx;
        Save();
    }

    public bool AddServerHub(ServerHubInfo hubInfo)
    {
        // Ensure no URI duplication
        if (ServerHubs.Any(h => string.Equals(h.HubUri, hubInfo.HubUri, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning($"Attempted to add duplicate hub URI: {hubInfo.HubUri}");
            return false;
        }
        // Otherwise, add it.
        ServerHubs.Add(hubInfo);
        Save();
        return true;
    }

    public bool RemoveHub(ServerHubInfo hubInfo)
    {
        if (ServerHubs.Remove(hubInfo))
        {
            _logger.LogWarning($"Failed to remove hub: {hubInfo.HubName} with URI {hubInfo.HubUri}");
            return false;
        }
        // Ensure the index is within bounds.
        if (ChosenHubIndex >= ServerHubs.Count)
        {
            ChosenHubIndex = Math.Clamp(ChosenHubIndex, 0, ServerHubs.Count - 1);
#if !DEBUG
            if (ChosenHubIndex is 1) ChosenHubIndex = 0;
#endif
        }
        Save();
        return true;
    }
}
