using CkCommons.HybridSaver;
using Sundouleia.Services;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class TransientCacheStorage
{
    // holds a personal transient cache for every player we want to hold data on.
    // note that this is only for our logged in characters and not other players.

    // Players are stored by key in the format ([GameObject->NameString]_[GameObject->HomeWorldId])
    public Dictionary<string, TransientPlayerCache> PlayerCaches { get; set; } = [];
}

// note that this is storing which game paths have modified paths to watch for.
public class TransientPlayerCache
{
    // Effected globally across all jobs.
    public List<string> PersistantCache { get; set; } = [];

    // Individual caches per job ID stored for the player. These contain things such as job spesific VFX's or animations,
    // so that we do not have to run as heavy of a check on every transient resource validation.
    public Dictionary<uint, List<string>> JobBasedCache { get; set; } = [];

    // Pets are constantly called away and re-summoned, it is nice to retain any files
    // we have defined for them so they can stay persistant between summons.
    public Dictionary<uint, List<string>> JobBasedPetCache { get; set; } = [];

    public TransientPlayerCache()
    { }

    // Could move methods down into here as we do not need to worry about saving until plugin close.
    // Would give us more direct access and avoid an additional TryGetValue call.
}


public class TransientCacheConfig : IHybridSavable
{
    private readonly ILogger<TransientCacheConfig> _logger;
    private readonly HybridSaveService _saver;
    [JsonIgnore] public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    [JsonIgnore] public HybridSaveType SaveType => HybridSaveType.Json;
    public int ConfigVersion => 0;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.TransientCache).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Cache"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }

    public TransientCacheConfig(ILogger<TransientCacheConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.TransientCache;
        _logger.LogInformation($"Loading in Config for file: {file}");
        try
        {
            if (!File.Exists(file))
            {
                _logger.LogWarning($"Config file not found for: {file}");
                _saver.Save(this);
                return;
            }

            var jsonText = File.ReadAllText(file);
            var jObject = JObject.Parse(jsonText);
            var version = jObject["Version"]?.Value<int>() ?? 0;

            // Load instance configuration (no version migrations needed yet)
            Current = jObject["Current"]?.ToObject<TransientCacheStorage>() ?? new TransientCacheStorage();

            _logger.LogInformation("Config loaded.");
            Save();
        }
        catch (Bagagwa ex) { _logger.LogError("Failed to load config." + ex); }
    }

    public TransientCacheStorage Current { get; private set; } = new();

    /// <summary>
    ///     Elevates a gamepath for a spesified jobBasedCache to the global persistent cache. 
    /// </summary>
    private bool ElevateIfNeeded(string key, uint jobId, string gamePath)
    {
        if (!Current.PlayerCaches.TryGetValue(key, out var playerCache))
            return false;

        // check if it's in the job cache of other jobs and elevate if needed
        foreach (var kvp in playerCache.JobBasedCache)
        {
            // only want to elevate for files matching this job id.
            if (kvp.Key == jobId)
                continue;

            if (kvp.Value.Contains(gamePath, StringComparer.Ordinal))
            {
                playerCache.JobBasedCache[kvp.Key].Remove(gamePath);
                playerCache.PersistantCache.Add(gamePath);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Removes the gamepath from the caches defined by the OwnedObject kind.
    /// </summary>
    /// <returns> How many were removed. </returns>
    public int RemovePath(string key, OwnedObject kind, string gamePath)
    {
        int removedEntries = 0;

        if (!Current.PlayerCaches.TryGetValue(key, out var playerCache))
            return removedEntries;

        // If player remove from both global and from any job caches.
        if (kind is OwnedObject.Player)
        {
            if (Current.PlayerCaches.Remove(gamePath)) 
                removedEntries++;
            foreach(var kvp in playerCache.JobBasedCache)
                if (kvp.Value.Remove(gamePath))
                    removedEntries++;
        }
        // Pet ones should be handled seperately.
        if (kind is OwnedObject.Pet)
        {
            foreach (var kvp in playerCache.JobBasedPetCache)
                if (kvp.Value.Remove(gamePath))
                    removedEntries++;
        }

        return removedEntries;
    }

    /// <summary>
    ///     Appends or elevates a gamepath for a certain jobID to a players transient cache.
    /// </summary>
    public void AddOrElevate(string key, uint jobId, string gamePath)
    {
        if (!Current.PlayerCaches.TryGetValue(key, out var playerCache))
            return;

        // Check if in global cache, if so, do not do anything as we must keep it.
        if (playerCache.PersistantCache.Contains(gamePath, StringComparer.Ordinal))
            return;
        
        if (ElevateIfNeeded(key, jobId, gamePath))
            return;

        // check if the jobid is already in the cache to start
        if (!playerCache.JobBasedCache.TryGetValue(jobId, out var jobCache))
            playerCache.JobBasedCache[jobId] = jobCache = new();

        // check if the path is already in the job specific cache
        if (!jobCache.Contains(gamePath, StringComparer.Ordinal))
            jobCache.Add(gamePath);
    }

}
