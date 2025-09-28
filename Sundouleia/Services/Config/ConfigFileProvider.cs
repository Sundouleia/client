using CkCommons.HybridSaver;

namespace Sundouleia.Services.Configs;

/// <summary> Any file type that we want to let the HybridSaveService handle </summary>
public interface IHybridSavable : IHybridConfig<ConfigFileProvider> { }

/// <summary> Helps encapsulate all the configuration file names into a single place. </summary>
public class ConfigFileProvider : IConfigFileProvider
{
    // Shared Config Directories
    public static string AssemblyLocation       => Svc.PluginInterface.AssemblyLocation.FullName;
    public static string AssemblyDirectoryName  => Svc.PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
    public static string AssemblyDirectory      => Svc.PluginInterface.AssemblyLocation.Directory?.FullName ?? string.Empty;
    public static string SundouleiaDirectory    => Svc.PluginInterface.ConfigDirectory.FullName;
    public static string EventDirectory     { get; private set; } = string.Empty;
    public static string FileSysDirectory   { get; private set; } = string.Empty;
    public static string ThumbnailDirectory { get; private set; } = string.Empty;
    
    // Shared Client Configs
    public readonly string MainConfig;
    public readonly string RecentChatLog;
    public readonly string Favorites;
    public readonly string LoadedResources; // for help with transient file data.

    // Leave this here as an example reference.
    public string CKFS_Triggers => Path.Combine(FileSysDirectory, "fs-triggers.json");

    // Shared Server Configs
    public readonly string NicknameConfig;
    public readonly string AccountConfig;

    // Unique Client Configs Per Account.
    public string SundesmoGroups => Path.Combine(CurrentPlayerDirectory, "sundesmo-groups.json"); // coiuld merge this with favorites or something idk.

    public string CurrentPlayerDirectory => Path.Combine(SundouleiaDirectory, CurrentUserUID ?? "InvalidFiles");
    public string? CurrentUserUID { get; private set; } = null;

    // Previously profiles was determined by the logged in UID but now it is determined by the secret key IDX. 
    // We will need to update how this is handled later.
    public ConfigFileProvider()
    {
        EventDirectory = Path.Combine(SundouleiaDirectory, "eventlog");
        FileSysDirectory = Path.Combine(SundouleiaDirectory, "filesystem");
        ThumbnailDirectory = Path.Combine(SundouleiaDirectory, "thumbnails");

        MainConfig = Path.Combine(SundouleiaDirectory, "config.json");
        RecentChatLog = Path.Combine(SundouleiaDirectory, "chat-recent.json");
        Favorites = Path.Combine(SundouleiaDirectory, "favorites.json");
        LoadedResources = Path.Combine(SundouleiaDirectory, "loaded-resources.json");

        Nicknames = Path.Combine(SundouleiaDirectory, "nicknames.json");
        ServerConfig = Path.Combine(SundouleiaDirectory, "server.json");

        // attempt to load in the UID if the config.json exists.
        if (File.Exists(MainConfig))
        {
            var json = File.ReadAllText(MainConfig);
            var configJson = JObject.Parse(json);
            CurrentUserUID = configJson["Config"]!["LastUidLoggedIn"]?.Value<string>() ?? "UNKNOWN_VOID";
        }
    }

    // If this is not true, we should not be saving our configs anyways.
    public bool HasValidProfileConfigs { get; private set; } = false;

    public void ClearUidConfigs()
    {
        HasValidProfileConfigs = false;
        UpdateUserUID(null);
    }

    public void UpdateConfigs(string uid)
    {
        Svc.Logger.Information("Updating Configs for UID: " + uid);
        UpdateUserUID(uid);

        if (!Directory.Exists(CurrentPlayerDirectory))
            Directory.CreateDirectory(CurrentPlayerDirectory);

        Svc.Logger.Information("Configs Updated.");
        HasValidProfileConfigs = true;
    }

    private void UpdateUserUID(string? uid)
    {
        if (CurrentUserUID != uid)
        {
            CurrentUserUID = uid;
            UpdateUidInConfig(uid);
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
