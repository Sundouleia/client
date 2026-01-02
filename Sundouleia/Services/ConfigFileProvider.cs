using CkCommons.HybridSaver;
using Sundouleia.PlayerClient;

namespace Sundouleia.Services.Configs;

/// <summary> Any file type that we want to let the HybridSaveService handle </summary>
public interface IHybridSavable : IHybridConfig<ConfigFileProvider> { }

/// <summary> Helps encapsulate all the configuration file names into a single place. </summary>
public class ConfigFileProvider : IConfigFileProvider
{
    private readonly ILogger<ConfigFileProvider> _logger;
    // Shared Config Directories
    public static string AssemblyLocation       => Svc.PluginInterface.AssemblyLocation.FullName;
    public static string AssemblyDirectoryName  => Svc.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
    public static string AssemblyDirectory      => Svc.PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
    public static string SundouleiaDirectory    => Svc.PluginInterface.ConfigDirectory.FullName;
    public static string ChatDirectory      { get; private set; } = string.Empty;
    public static string EventDirectory     { get; private set; } = string.Empty;
    public static string FileSysDirectory   { get; private set; } = string.Empty;
    
    // Shared Configs
    public readonly string MainConfig;
    public readonly string OwnedSMAFilesConfig;
    public readonly string RecentChatLog;
    public readonly string Favorites;
    public readonly string NicknameConfig;
    public readonly string AccountConfig;
    // Shared Sync-related Configs
    public readonly string TransientCache;
    public readonly string PlzNoCrashFriends;
    public readonly string LoadedResources;
    public readonly string FileCacheCsv;

    // Shared FileSystem Configs.
    public string DDS_Requests => Path.Combine(FileSysDirectory, "dds-requests.json");
    public string DDS_Whitelist => Path.Combine(FileSysDirectory, "dds-whitelist.json");

    // Maybe Maybe not? Unsure how I want to display this yet.
    public string DDS_MCDFData => Path.Combine(FileSysDirectory, "dds-mcdfdata.json");
    public string DDS_Radar => Path.Combine(FileSysDirectory, "dds-radar.json");

    // Per Account-Profile Configs.
    public string DDS_Groups => Path.Combine(CurrentProfileDirectory, "dds-groups.json");
    public string SundesmoGroups => Path.Combine(CurrentProfileDirectory, "sundesmo-groups.json"); // could merge this with favorites or something idk.

    // Profile Helpers.
    public string CurrentProfileDirectory => Path.Combine(SundouleiaDirectory, CurrentProfileUID ?? "InvalidFiles");
    public string? CurrentProfileUID { get; private set; } = null;

    // Previously profiles was determined by the logged in UID but now it is determined by the secret key IDX. 
    // We will need to update how this is handled later.
    public ConfigFileProvider(ILogger<ConfigFileProvider> logger)
    {
        _logger = logger;

        ChatDirectory = Path.Combine(SundouleiaDirectory, "chatData");
        EventDirectory = Path.Combine(SundouleiaDirectory, "eventlog");
        FileSysDirectory = Path.Combine(SundouleiaDirectory, "filesystem");

        // Ensure directory existence.
        if (!Directory.Exists(ChatDirectory)) Directory.CreateDirectory(ChatDirectory);
        if (!Directory.Exists(EventDirectory)) Directory.CreateDirectory(EventDirectory);
        if (!Directory.Exists(FileSysDirectory)) Directory.CreateDirectory(FileSysDirectory);

        // Configs.
        MainConfig = Path.Combine(SundouleiaDirectory, "config.json");
        OwnedSMAFilesConfig = Path.Combine(SundouleiaDirectory, "ownedsmafiles.json");
        TransientCache = Path.Combine(SundouleiaDirectory, "transientcache.json");
        PlzNoCrashFriends = Path.Combine(SundouleiaDirectory, "plznocrashfriends.json");
        RecentChatLog = Path.Combine(SundouleiaDirectory, "chat-recent.json");
        Favorites = Path.Combine(SundouleiaDirectory, "favorites.json");
        LoadedResources = Path.Combine(SundouleiaDirectory, "loaded-resources.json");
        FileCacheCsv = Path.Combine(SundouleiaDirectory, "filecache.csv");
        NicknameConfig = Path.Combine(SundouleiaDirectory, "nicknames.json");
        AccountConfig = Path.Combine(SundouleiaDirectory, "account.json");

        // attempt to load in the UID if the config.json exists.
        if (File.Exists(MainConfig))
        {
            var json = File.ReadAllText(MainConfig);
            var configJson = JObject.Parse(json);
            CurrentProfileUID = configJson["Config"]!["LastUidLoggedIn"]?.Value<string>() ?? string.Empty;
            // Set it is valid if the string is not empty.
            HasValidProfileConfigs = !string.IsNullOrEmpty(CurrentProfileUID);
            // Ensure the directory exists for this profile.
            if (!Directory.Exists(CurrentProfileDirectory) && HasValidProfileConfigs)
                Directory.CreateDirectory(CurrentProfileDirectory);

            _logger.LogInformation($"Loaded LastUidLoggedIn [{CurrentProfileUID}] from MainConfig.");
        }
    }

    // If this is not true, we should not be saving our configs anyways.
    public bool HasValidProfileConfigs { get; private set; } = false;

    // Updates the CurrentProfileDirectory to match the provided profile UID.
    public void UpdateConfigs(string profileUID)
    {
        bool isDifferent = CurrentProfileUID != profileUID;
        // If the profile UID changed, update latest in MainConfig and this provider.
        if (isDifferent)
        {
            _logger.LogInformation($"Updating Configs for Profile UID [{profileUID}]");
            CurrentProfileUID = profileUID;
            UpdateUidInConfig(profileUID);

            // If the directory doesnt yet exist for this profile, create it.
            if (!Directory.Exists(CurrentProfileDirectory))
                Directory.CreateDirectory(CurrentProfileDirectory);

            _logger.LogInformation("Configs Updated.");
            HasValidProfileConfigs = !string.IsNullOrEmpty(profileUID);
        }
    }

    private void UpdateUidInConfig(string? uid)
    {
        var uidFilePath = Path.Combine(SundouleiaDirectory, "config.json");
        if (!File.Exists(uidFilePath))
            return;

        var tempFilePath = uidFilePath + ".tmp";
        using (var reader = new StreamReader(uidFilePath))
        using (var writer = new StreamWriter(tempFilePath))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().StartsWith("\"LastUidLoggedIn\""))
                {
                    writer.WriteLine($"    \"LastUidLoggedIn\": \"{uid ?? ""}\",");
                }
                else
                {
                    writer.WriteLine(line);
                }
            }
        }
        File.Move(tempFilePath, uidFilePath, true);
    }
}
