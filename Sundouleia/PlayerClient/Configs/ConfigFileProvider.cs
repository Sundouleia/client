using CkCommons.HybridSaver;
using Sundouleia.PlayerClient;

namespace Sundouleia;

/// <summary> Helps encapsulate all the configuration file names into a single place. </summary>
public class ConfigFileProvider : IConfigFileProvider
{
    private readonly ILogger<ConfigFileProvider> _logger;
    // Shared Config Directories
    public static string AssemblyLocation      => Svc.PluginInterface.AssemblyLocation.FullName;
    public static string AssemblyDirectoryName => Svc.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
    public static string AssemblyDirectory     => Svc.PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
    public static string ConfigDirectory       => Svc.PluginInterface.ConfigDirectory.FullName;

    // Universal Subdirectories
    public static string ServerHubDirectory { get; private set; }
    public static string FileSysDirectory   { get; private set; }
    public static string EventDirectory     { get; private set; }

    // Universal configs
    public readonly string ChatConfig;
    public readonly string ServerConfig;
    public readonly string PerformanceConfig;
    public readonly string FileCacheCsv;
    public readonly string TransientCache;
    public readonly string PlzNoCrashFriends;
    public readonly string LoadedResources;
    public readonly string OwnedSMAFilesConfig;

    // Account-Authoritative Configs
    public string MainConfig     { get; private set; }
    public string AccountConfig  { get; private set; }
    public string Favorites      { get; private set; }
    public string NicknameConfig { get; private set; }

    // Per Account-Profile Configs.
    public string AccountProfileDirectory => Path.Combine(ServerHubDirectory, CurrentProfileUID ?? "NO_USER"); // Will fail bad profiles.
    public string DDS_Groups => Path.Combine(AccountProfileDirectory, "dds-groups.json");
    public string SundesmoGroups => Path.Combine(AccountProfileDirectory, "sundesmo-groups.json");

    // DDS universal Configs
    public string DDS_Requests => Path.Combine(FileSysDirectory, "dds-requests.json");
    public string DDS_Whitelist => Path.Combine(FileSysDirectory, "dds-whitelist.json");
    public string DDS_Radar => Path.Combine(FileSysDirectory, "dds-radar.json");
    public string DDS_MCDFData => Path.Combine(FileSysDirectory, "dds-mcdfdata.json");

    // Helpers
    public bool IsOnMainServer => string.Equals(CurrentHubURI, ServerHubConfig.MAIN_SERVER_URI, StringComparison.Ordinal);
    public string CurrentHubURI { get; private set; } = string.Empty;
    public string? CurrentProfileUID { get; private set; } = null;
    public bool HasValidProfileConfigs => !string.IsNullOrEmpty(CurrentProfileUID);

    public ConfigFileProvider(ILogger<ConfigFileProvider> logger)
    {
        _logger = logger;

        // Create the Universal configs.
        ChatConfig = Path.Combine(ConfigDirectory, "chat.json");
        ServerConfig = Path.Combine(ConfigDirectory, "connections.json");
        PerformanceConfig = Path.Combine(ConfigDirectory, "performance.json");
        FileCacheCsv = Path.Combine(ConfigDirectory, "filecache.csv");
        TransientCache = Path.Combine(ConfigDirectory, "transientcache.json");
        PlzNoCrashFriends = Path.Combine(ConfigDirectory, "plznocrashfriends.json");
        LoadedResources = Path.Combine(ConfigDirectory, "loaded-resources.json");
        OwnedSMAFilesConfig = Path.Combine(ConfigDirectory, "ownedsmafiles.json");

        // Init the universal DDS configs.
        FileSysDirectory = Path.Combine(ConfigDirectory, "filesystem");
        Directory.CreateDirectory(FileSysDirectory);

        if (File.Exists(ServerConfig))
        {
            var json = JObject.Parse(File.ReadAllText(ServerConfig));
            var lastUri = json["LastConnectedURI"]?.Value<string>() ?? ServerHubConfig.MAIN_SERVER_URI;
            var lastUid = json["LastLoggedInUID"]?.Value<string>() ?? string.Empty;

            _logger.LogInformation($"Loaded LastConnectedURI [{lastUri}] and LastLoggedInUID [{lastUid}] from ServerConfig.");

            SetAllFoldersAndPaths(lastUri, lastUid);
        }
        else
        {
            SetFoldersAndPathsForHubUri(ServerHubConfig.MAIN_SERVER_URI);
        }

        LogAllPaths();
    }

    public bool TryUpdateForServerUri(ServerHubConfig config, string newUri)
    {
        if (string.Equals(CurrentHubURI, newUri, StringComparison.Ordinal))
        {
            _logger.LogInformation($"Hub URI [{newUri}] is already set. No changes made.");
            return false;
        }

        // Create or Update the nessisary directories.
        SetFoldersAndPathsForHubUri(newUri);
        SetFoldersAndPathsForHubProfile(null);
        // Update the data and save.
        config.LastJoinedUri = newUri;
        config.LastLoggedInUID = string.Empty; // Clear profile on hub change to prevent bad paths.
        config.Save();
#if DEBUG
        LogAllPaths();
#endif
        return true;
    }

    // This is the right structure
    public bool TrySetProfileConfigs(ServerHubConfig config, string? profileUid)
    {
        if (CurrentProfileUID == profileUid)
        {
            _logger.LogInformation($"Profile UID [{profileUid}] is already set. No changes made.");
            return false;
        }

        SetFoldersAndPathsForHubProfile(profileUid);
        // Update the data and save.
        config.LastLoggedInUID = profileUid ?? string.Empty;
        config.Save();
#if DEBUG
        LogAllPaths();
#endif
        return true;
    }

    // Internals
    private void SetAllFoldersAndPaths(string newUri, string? profileUid)
    {
        CurrentHubURI = newUri;
        // Update hub directory
        ServerHubDirectory = IsOnMainServer ? ConfigDirectory : Path.Combine(ConfigDirectory, GetHubFolderName(CurrentHubURI));
        Directory.CreateDirectory(ServerHubDirectory);

        // Update event directory
        EventDirectory = Path.Combine(ServerHubDirectory, "eventlog");
        Directory.CreateDirectory(EventDirectory);

        // Update account-authoritative configs
        MainConfig = Path.Combine(ServerHubDirectory, "config.json");
        AccountConfig = Path.Combine(ServerHubDirectory, "account.json");
        Favorites = Path.Combine(ServerHubDirectory, "favorites.json");
        NicknameConfig = Path.Combine(ServerHubDirectory, "nicknames.json");

        _logger.LogInformation($"Hub configs updated for URI [{CurrentHubURI}]");
        SetFoldersAndPathsForHubProfile(profileUid);
    }

    private void SetFoldersAndPathsForHubUri(string newUri)
    {
        CurrentHubURI = newUri;
        // Update hub directory
        ServerHubDirectory = IsOnMainServer ? ConfigDirectory : Path.Combine(ConfigDirectory, GetHubFolderName(CurrentHubURI));
        Directory.CreateDirectory(ServerHubDirectory);

        // Update event directory
        EventDirectory = Path.Combine(ServerHubDirectory, "eventlog");
        Directory.CreateDirectory(EventDirectory);

        // Update account-authoritative configs
        MainConfig = Path.Combine(ServerHubDirectory, "config.json");
        AccountConfig = Path.Combine(ServerHubDirectory, "account.json");
        Favorites = Path.Combine(ServerHubDirectory, "favorites.json");
        NicknameConfig = Path.Combine(ServerHubDirectory, "nicknames.json");

        _logger.LogInformation($"Hub configs updated for URI [{CurrentHubURI}]");
        SetFoldersAndPathsForHubProfile(null);

    }

    private void SetFoldersAndPathsForHubProfile(string? profileUid)
    {
        if (string.IsNullOrEmpty(profileUid))
        {
            CurrentProfileUID = null;
            _logger.LogInformation("Cleared profile configs because UID is null or empty.");
            return;
        }

        _logger.LogInformation($"Setting profile configs for UID [{profileUid}].");
        CurrentProfileUID = profileUid;
        // Create the directory for VALID directories.
        if (!Directory.Exists(AccountProfileDirectory))
        {
            _logger.LogInformation($"Profile directory does not exist. Creating new one at [{AccountProfileDirectory}].");
            Directory.CreateDirectory(AccountProfileDirectory);
        }
        _logger.LogInformation($"Profile configs updated. AccountProfileDirectory: [{AccountProfileDirectory}]");
    }

    public string GetHubFolderName(string uri)
    {
        if (uri == ServerHubConfig.MAIN_SERVER_URI)
            return "_hub_main";
        else if (uri == ServerHubConfig.DEV_SERVER_URI)
            return "_hub_dev";

        // Remove scheme prefix (wss:// or ws://)
        string cleanUri = uri.Replace("wss://", "").Replace("ws://", "");

        // Replace invalid filename chars with '_'
        foreach (var c in Path.GetInvalidFileNameChars())
            cleanUri = cleanUri.Replace(c, '_');

        // Remove trailing underscores
        cleanUri = cleanUri.TrimEnd('_');

        // Add _hub_ prefix
        return $"_hub_{cleanUri}";
    }

    public void LogAllPaths()
    {
        var logMessage = $@"
            Loaded Paths:
            ***********************
            # Shared Config Directories
            AssemblyLocation:       {AssemblyLocation}
            AssemblyDirectoryName:  {AssemblyDirectoryName}
            AssemblyDirectory:      {AssemblyDirectory}
            ConfigDirectory:        {ConfigDirectory}

            # Universal Subdirectories
            FileSysDirectory:       {FileSysDirectory}
            EventDirectory:         {EventDirectory}

            # Universal Configs
            ChatConfig:             {ChatConfig}
            ServerConfig:           {ServerConfig}
            PerformanceConfig:      {PerformanceConfig}
            FileCacheCsv:           {FileCacheCsv}
            TransientCache:         {TransientCache}
            PlzNoCrashFriends:      {PlzNoCrashFriends}
            LoadedResources:        {LoadedResources}
            OwnedSMAFilesConfig:    {OwnedSMAFilesConfig}

            # Account-Authoritative Configs
            MainConfig:             {MainConfig}
            AccountConfig:          {AccountConfig}
            Favorites:              {Favorites}
            NicknameConfig:         {NicknameConfig}

            # Account-Profile Configs
            DDS_Groups:             {DDS_Groups}
            SundesmoGroups:         {SundesmoGroups}

            # DDS Universal Configs
            DDS_Requests:           {DDS_Requests}
            DDS_Whitelist:          {DDS_Whitelist}
            DDS_Radar:              {DDS_Radar}
            DDS_MCDFData:           {DDS_MCDFData}

            # Helpers
            IsOnMainServer:         {IsOnMainServer}
            CurrentHubURI:          {CurrentHubURI}
            CurrentProfileUID:      {CurrentProfileUID}
            ServerHubDirectory:     {ServerHubDirectory}
            AccountProfileDirectory:{AccountProfileDirectory}
            HasValidProfileConfigs: {HasValidProfileConfigs}
            ***********************";
        _logger.LogInformation(logMessage);
    }
}
