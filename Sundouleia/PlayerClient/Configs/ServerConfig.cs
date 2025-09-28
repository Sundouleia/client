using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;
using Sundouleia.WebAPI;

namespace Sundouleia.PlayerClient;


public class AccountConfig : IHybridSavable
{
    private readonly ILogger<AccountConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.AccountConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["AccountStorage"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }
    public AccountConfig(ILogger<AccountConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.ServerConfig;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        // if migrations needed, do logic for that here.
        if (jObject["ServerStorage"]?["ToyboxFullPause"] is not null)
        {
            // Contains old config, migrate it.
            jObject = ConfigMigrator.MigrateServerConfig(jObject, _saver.FileNames);
        }

        // execute based on version.
        switch (version)
        {
            case 0:
                LoadV0(jObject["ServerStorage"]);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        Svc.Logger.Information("Config loaded.");
        Save();
    }

    private void LoadV0(JToken? data)
    {
        if (data is not JObject storage)
            return;
        Current = storage.ToObject<AccountStorage>() ?? throw new Exception("Failed to load AccountStorage.");
    }

    public AccountStorage Current { get; set; } = new AccountStorage();
}

public class AccountStorage
{
    public List<Authentication> Authentications { get; set; } = [];  // the authentications we have for this client
    public bool FullPause { get; set; } = false;                     // if client is disconnected from the server (not integrated yet)
}


public record Authentication
{
    public ulong CharacterPlayerContentId { get; set; } = 0;
    public string CharacterName { get; set; } = string.Empty;
    public ushort WorldId { get; set; } = 0;
    public bool IsPrimary { get; set; } = false;
    public SecretKey SecretKey { get; set; } = new();
}


public class SecretKey
{
    public string Label { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool HasHadSuccessfulConnection { get; set; } = false;
    public string LinkedProfileUID { get; set; } = string.Empty;
}
