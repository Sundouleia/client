using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

// If we ever want to add more categories here, reference GSpeaks FavoriteManager:

// Defined internally via StreamWrite for ease of use.
public class FavoritesConfig : IHybridSavable
{
    private readonly ILogger<FavoritesConfig> _logger;
    private readonly HybridSaveService _saver;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider ser, out bool upa) => (upa = false, ser.Favorites).Item2;
    public string JsonSerialize() => throw new NotImplementedException();
    public FavoritesConfig(ILogger<FavoritesConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public readonly HashSet<string>  SundesmoUids = [];

    public void Load()
    {
        var file = _saver.FileNames.Favorites;
        _logger.LogInformation("Loading in Favorites Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("No Favorites Config file found at {0}", file);
            _saver.Save(this);
            return;
        }

        try
        {
            var load = JsonConvert.DeserializeObject<LoadIntermediary>(File.ReadAllText(file));
            if (load is null)
                throw new Exception("Failed to load favorites.");
            // Load favorites.
            // (No Migration Needed yet).
            SundesmoUids.UnionWith(load.SundesmoUids);
        }
        catch (Bagagwa e)
        {
            _logger.LogError(e, "Failed to load favorites.");
        }
        _logger.LogInformation("Favorites Config loaded.");
    }

    public bool TryAddUser(string sundesmo)
    {
        if (SundesmoUids.Add(sundesmo))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public void AddUsers(IEnumerable<string> sundesmos)
    {
        SundesmoUids.UnionWith(sundesmos);
        _saver.Save(this);
    }

    public bool RemoveUser(string sundesmo)
    {
        if (SundesmoUids.Remove(sundesmo))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public void RemoveUsers(IEnumerable<string> sundesmos)
    {
        SundesmoUids.ExceptWith(sundesmos);
        _saver.Save(this);
    }

    #region Saver
    public void WriteToStream(StreamWriter writer)
    {
        using var j = new JsonTextWriter(writer);
        j.Formatting = Formatting.Indented;
        j.WriteStartObject();

        j.WritePropertyName(nameof(LoadIntermediary.Version));
        j.WriteValue(ConfigVersion);

        j.WritePropertyName(nameof(LoadIntermediary.SundesmoUids));
        j.WriteStartArray();
        foreach (var uid in SundesmoUids)
            j.WriteValue(uid);
        j.WriteEndArray();

        j.WriteEndObject();
    }
    #endregion Saver

    // Used to help with object based deserialization from the json loader.
    private class LoadIntermediary
    {
        public int Version = 1;
        public IEnumerable<string>  SundesmoUids = [];
    }
}
