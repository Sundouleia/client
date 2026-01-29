using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public enum ConnectionKind
{
    /// <summary>
    ///     You are connected normally, All data is sent and received.
    /// </summary>
    Normal,

    /// <summary>
    ///     Any changes you make are not sent to others, but you can still see others.
    /// </summary>
    WardrobeMode,

    /// <summary>
    ///     Your Appearance is shared to others, but others will look vanilla.
    /// </summary>
    StreamerMode,

    /// <summary>
    ///     No data is sent or received. (Avoid Connection / Disconnect)
    /// </summary>
    FullPause
}

public class AccountStorage
{
    /// <summary>
    ///     These are the 'profiles' of your account. An AccountProfile has UID and secret key. <para/>
    ///     Profiles are identified by a GUID on creation, and used for serialization referencing.
    /// </summary>
    public HashSet<AccountProfile> Profiles { get; set; } = new();

    /// <summary>
    ///     The characters tracked by sundouleia. <br/>
    ///     Each character is tracked via their content ID,
    ///     with name and world being updatable if it changes.
    /// </summary>
    public Dictionary<ulong, TrackedPlayer> TrackedPlayers { get; set; } = [];
}

/// <summary>
///     A profile contains a friendly label, the actual secret key, 
///     and if we had a successful connection with it.
/// </summary>
public class AccountProfile
{
    /// <summary>
    ///     Defined on Account Profile creation, and used in serialization and deserialization to link referenced profiles.
    /// </summary>
    public Guid Identifier { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     The display name given to a profile, that is visible on the UI.
    /// </summary>
    public string ProfileLabel { get; set; } = string.Empty;

    /// <summary>
    ///     The UserUID associated with this secret key. <para />
    ///     This is recieved from the server upon the first valid connection with this key.
    /// </summary>
    public string UserUID { get; set; } = string.Empty;

    /// <summary>
    ///     The secret key used to authenticate with the server and connect.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    ///     If this is the primary key, all other keys are removed when it is removed.
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    ///     If a valid connection was established. This could easily be removed or replaced with if UserUID != string.Empty (?)
    /// </summary>
    public bool HadValidConnection { get; set; } = false;
}


/// <summary>
///     An Authentication made by a logged in character. <para />
///     Holds basic information about the player, and which key they are linked to.
/// </summary>
public record TrackedPlayer
{
    /// <summary>
    ///     The unique value of this authentication. <para />
    ///     A ContentID is a static value given to a character of a FFXIV Service Account. <br/>
    ///     Persists through name and world changes.
    /// </summary>
    public ulong ContentId { get; set; } = 0;

    /// <summary>
    ///     The Character Name associated with this ContentID.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    ///     The HomeWorld associated with this ContentID.
    /// </summary>
    public ushort WorldId { get; set; } = 0;

    /// <summary>
    ///     The linked account profile this character is connected to. <br/>
    ///     If null, assume not set.
    /// </summary>
    public AccountProfile? LinkedProfile { get; set; } = null;

    public bool IsLinked() => LinkedProfile is not null;
}

public class AccountConfig : IHybridSavable
{
    private readonly ILogger<AccountConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 1;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.AccountConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        // Project TrackedPlayers so LinkedProfile is just the GUID
        var trackedPlayersJObj = Current.TrackedPlayers.ToDictionary(
            kvp => kvp.Key,
            kvp => new
            {
                kvp.Value.ContentId,
                kvp.Value.PlayerName,
                kvp.Value.WorldId,
                LinkedProfile = kvp.Value.LinkedProfile?.Identifier
            });

        // Build the final JObject manually
        return new JObject
        {
            ["Version"] = ConfigVersion,
            ["ConnectionKind"] = (int)ConnectionKind,
            ["AccountStorage"] = new JObject
            {
                ["Profiles"] = JArray.FromObject(Current.Profiles),
                ["TrackedPlayers"] = JObject.FromObject(trackedPlayersJObj)
            }
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
            _saver.Save(this);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        // execute based on version.
        switch (version)
        {
            case 0:
                MigrateAndLoadV0AsV1(jObject);
                break;
            case 1:
                LoadV1(jObject);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        Save();
    }

    /// <summary>
    ///     How we should behave when connected, or if we should at all. <para />
    ///     May want to have a mediator fire on this being set or something, idk.
    /// </summary>
    public ConnectionKind ConnectionKind { get; set; } = ConnectionKind.Normal;

    /// <summary>
    ///     The detailed information about the current account.
    /// </summary>
    public AccountStorage Current { get; set; } = new AccountStorage();

    public bool IsConfigValid()
        => Current.Profiles.Count > 0 && Current.TrackedPlayers.Any(c => c.Value.LinkedProfile is not null);

    // Remove this after for open beta launch, the new format is all we need for that.
    private void MigrateAndLoadV0AsV1(JObject root)
    {
        // If it fails, good. Bomb them for all i care. I dont want to fuck up peoples passwords.
        if (root["AccountStorage"] is not JObject oldStorage)
            throw new Exception("Failed to load AccountStorage for migration.");

        ConnectionKind = (oldStorage["FullPause"]?.Value<bool>() ?? false) ? ConnectionKind.FullPause : ConnectionKind.Normal;
        var newStorage = new AccountStorage()
        {
            Profiles = new HashSet<AccountProfile>(),
            TrackedPlayers = new Dictionary<ulong, TrackedPlayer>()
        };

        // Track the mainProfileIdx so we know which profiles to migrate to the main ID.
        var mainProfiles = new HashSet<int>();
        // Iterate through all of the account profiles first, so we can use them for the other things later.
        if (oldStorage["Profiles"] is JObject profilesObj)
        {
            foreach (var (idx, profileInfo) in profilesObj)
            {
                if (profileInfo is null)
                    continue;

                // This should auto-generate a new profile for the account.
                var newProfile = new AccountProfile()
                {
                    ProfileLabel = profileInfo["ProfileLabel"]?.Value<string>() ?? string.Empty,
                    UserUID = profileInfo["UserUID"]?.Value<string>() ?? string.Empty,
                    Key = profileInfo["Key"]?.Value<string>() ?? string.Empty,
                    IsPrimary = profileInfo["IsPrimary"]?.Value<bool>() ?? false,
                    HadValidConnection = profileInfo["HadValidConnection"]?.Value<bool>() ?? false,
                };

                if (newProfile.ProfileLabel.Length > 20)
                    newProfile.ProfileLabel = newProfile.ProfileLabel[..20];

                if (newProfile.IsPrimary)
                    mainProfiles.Add(int.Parse(idx));

                // Add it to the new storage.
                newStorage.Profiles.Add(newProfile);
            }
        }

        // get what is intended to be the 'main' profile.
        var mainProfile = newStorage.Profiles.FirstOrDefault(p => p.IsPrimary);

        // Now migrate all of the loginAuths to tracked players.
        if (oldStorage["LoginAuths"] is JArray loginsArr)
        {
            foreach (var trackedPlayer in loginsArr)
            {
                if (trackedPlayer is null)
                    continue;

                ulong contentId = trackedPlayer["ContentId"]?.Value<ulong>() ?? 0;
                string playerName = trackedPlayer["PlayerName"]?.ToString() ?? "";
                ushort worldId = trackedPlayer["WorldId"]?.Value<ushort>() ?? 0;
                int profileIdx = trackedPlayer["ProfileIdx"]?.Value<int>() ?? -1;

                var tracked = new TrackedPlayer
                {
                    ContentId = contentId,
                    PlayerName = playerName,
                    WorldId = worldId,
                    LinkedProfile = mainProfiles.Contains(profileIdx) ? mainProfile : null
                };
                // Add it to the storage.
                newStorage.TrackedPlayers[contentId] = tracked;
            }
        }

        // Return the migrated result.
        Current = newStorage;
    }

    private void LoadV1(JObject root)
    {
        // If it fails, good. Bomb them for all i care. I dont want to fuck up peoples passwords.
        if (root["AccountStorage"] is not JObject storage)
            throw new Exception("Failed to load AccountStorage for V1.");

        // Set ConnectionState.
        ConnectionKind = (ConnectionKind)(root["ConnectionKind"]?.Value<int>() ?? 0);

        // 1. Deserialize Profiles first
        var profiles = storage["Profiles"]!.ToObject<HashSet<AccountProfile>>() ?? throw new Exception("Failed to parse account profiles.");

        // Build a lookup by Guid.
        var profilesByGuid = profiles.ToDictionary(p => p.Identifier, p => p);

        // Build the account storage as-is.
        var accountStorage = new AccountStorage()
        {
            Profiles = profiles,
        };

        // Correct any longer names.
        foreach(var profile in accountStorage.Profiles)
            if (profile.ProfileLabel.Length > 20)
                profile.ProfileLabel = profile.ProfileLabel[..20];

        // Iterate the tracked players, if any.
        if (storage["TrackedPlayers"] is JObject trackedDictObj)
        {
            foreach (var (cid, playerInfo) in trackedDictObj)
            {
                if (playerInfo is not JObject infoObj)
                    continue;

                var linkedId = Guid.TryParse(infoObj["LinkedProfile"]?.Value<string>(), out var guid) ? guid : Guid.Empty;
                var trackedPlayer = new TrackedPlayer()
                {
                    ContentId = infoObj["ContentId"]?.Value<ulong>() ?? 0,
                    PlayerName = infoObj["PlayerName"]?.Value<string>() ?? string.Empty,
                    WorldId = infoObj["WorldId"]?.Value<ushort>() ?? 0,
                    LinkedProfile = profilesByGuid.GetValueOrDefault(linkedId),
                };
                // Add to storage
                accountStorage.TrackedPlayers[trackedPlayer.ContentId] = trackedPlayer;
            }
        }
        // Set the current account storage.
        Current = accountStorage;
    }
}