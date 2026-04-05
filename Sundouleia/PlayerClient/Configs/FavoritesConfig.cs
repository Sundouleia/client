using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

// Enum selector for hashsets of GUIDs
public enum FavoriteType
{
    Status,
    Preset,
    Smad,
    Smab,
    Smao,
    Smai,
}
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

    public static readonly HashSet<string>  SundesmoUids = new(StringComparer.Ordinal);
    public static readonly HashSet<Guid>    Statuses     = [];
    public static readonly HashSet<Guid>    Presets      = [];
    public static readonly HashSet<uint>    IconIDs      = [];

    public void Load()
    {
        var file = _saver.FileNames.Favorites;
        _logger.LogInformation($"Loading FavoritesConfig file: {file}");
        if (!File.Exists(file))
        {
            _logger.LogWarning($"FavoritesConfig file not found: {file}");
            SundesmoUids.Clear();
            Statuses.Clear();
            Presets.Clear();
            IconIDs.Clear();
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
            Statuses.UnionWith(load.Statuses);
            Presets.UnionWith(load.Presets);
        }
        catch (Bagagwa e)
        {
            _logger.LogError(e, "Failed to load favorites.");
        }
    }

    public bool Favorite(FavoriteType type, Guid id)
    {
        var res = type switch
        {
            FavoriteType.Status => Statuses.Add(id),
            FavoriteType.Preset => Presets.Add(id),
            _ => false
        };
        if (res)
            _saver.Save(this);
        return res;
    }

    public bool Favorite(string sundesmo)
    {
        if (SundesmoUids.Add(sundesmo))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public bool Favorite(uint iconId)
    {
        if (IconIDs.Add(iconId))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public void FavoriteBulk(FavoriteType type, IEnumerable<Guid> ids)
    {
        switch (type)
        {
            case FavoriteType.Status:
                Statuses.UnionWith(ids);
                break;
            case FavoriteType.Preset:
                Presets.UnionWith(ids);
                break;
        }
        _saver.Save(this);
    }

    public void FavoriteBulk(IEnumerable<string> sundesmos)
    {
        SundesmoUids.UnionWith(sundesmos);
        _saver.Save(this);
    }

    public void FavoriteBulk(IEnumerable<uint> iconIds)
    {
        IconIDs.UnionWith(iconIds);
        _saver.Save(this);
    }


    public bool Unfavorite(FavoriteType type, Guid id)
    {
        var res = type switch
        {
            FavoriteType.Status => Statuses.Remove(id),
            FavoriteType.Preset => Presets.Remove(id),
            _ => false
        };
        if (res)
            _saver.Save(this);
        return res;
    }

    public bool Unfavorite(string sundesmo)
    {
        if (SundesmoUids.Remove(sundesmo))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public bool Unfavorite(uint iconId)
    {
        if (IconIDs.Remove(iconId))
        {
            _saver.Save(this);
            return true;
        }
        return false;
    }

    public void ToggleFavorite(FavoriteType type, Guid id)
    {
        switch (type)
        {
            case FavoriteType.Status:
                if (!Statuses.Remove(id))
                    Statuses.Add(id);
                break;
            case FavoriteType.Preset:
                if (!Presets.Remove(id))
                    Presets.Add(id);
                break;

        }
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

        j.WritePropertyName(nameof(LoadIntermediary.Statuses));
        j.WriteStartArray();
        foreach (var status in Statuses)
            j.WriteValue(status);
        j.WriteEndArray();

        j.WritePropertyName(nameof(LoadIntermediary.Presets));
        j.WriteStartArray();
        foreach (var preset in Presets)
            j.WriteValue(preset);
        j.WriteEndArray();

        j.WritePropertyName(nameof(LoadIntermediary.IconIDs));
        j.WriteStartArray();
        foreach (var iconId in IconIDs)
            j.WriteValue(iconId);
        j.WriteEndArray();

        j.WriteEndObject();
    }
    #endregion Saver

    // Used to help with object based deserialization from the json loader.
    private class LoadIntermediary
    {
        public int Version = 1;
        public IEnumerable<string>  SundesmoUids = [];
        public IEnumerable<Guid>    Statuses     = [];
        public IEnumerable<Guid>    Presets      = [];
        public IEnumerable<uint>    IconIDs      = [];
    }
}
