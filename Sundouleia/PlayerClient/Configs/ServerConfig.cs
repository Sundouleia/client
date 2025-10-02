using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

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
        var file = _saver.FileNames.AccountConfig;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        // execute based on version.
        switch (version)
        {
            case 0:
                LoadV0(jObject["AccountStorage"]);
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

// reformat to reflect a hybrid of GS and Mare account management.
public class AccountStorage
{
    /// <summary>
    ///     If we have disconnected from the server manually.
    /// </summary>
    public bool FullPause { get; set; } = false;

    /// <summary>
    ///     The characters that used Sundouleia on this account. <para />
    ///     Do not confuse these with profiles, they are not the same. profiles are bound by key.
    /// </summary>
    public List<CharaAuthentication> LoginAuths { get; set; } = [];

    /// <summary>
    ///     These are the 'profiles' of your account.
    ///     A profile can be bound to any CharacterAuths and switched between. <para />
    ///     The order of these keys cannot be re-arranged, as they will mess up CharacterAuths.
    /// </summary>
    public Dictionary<int, AccountProfile> Profiles { get; set; } = new();
}


/// <summary>
///     An Authentication made by a logged in character. <para />
///     Holds basic information about the player, and which key they are linked to.
/// </summary>
public record CharaAuthentication
{
    public string PlayerName { get; set; } = string.Empty;
    public ushort WorldId { get; set; } = 0;
    public ulong ContentId { get; set; } = 0;
    // Which profile the auth is linked to. Can be changed freely.
    public int ProfileIdx { get; set; } = -1;
}


/// <summary>
///     A profile contains a friendly label, the actual secret key, 
///     and if we had a successful connection with it.
/// </summary>
public class AccountProfile
{
    public string ProfileLabel { get; set; } = string.Empty;
    public string UserUID { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    // If this is the primary key, all other keys are removed when it is removed.
    public bool IsPrimary { get; set; } = false;
    public bool HadValidConnection { get; set; } = false;
}
