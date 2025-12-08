using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class NoCrashFriendsStorage
{
    // <DataHashOfAnimationFile, Dictionary<SkeletonName, List<BoneIndices>>>
    public ConcurrentDictionary<string, Dictionary<string, List<ushort>>> BonesDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // <FileHash, TriangleCount>, May not even need this if we allocate runtime caches per plugin instance,
    // assuming they do not take that long to process. If they do, then keep this.
    public ConcurrentDictionary<string, long> ModelTris { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class NoCrashFriendsConfig : IHybridSavable
{
    private readonly ILogger<NoCrashFriendsConfig> _logger;
    private readonly HybridSaveService _saver;
    [JsonIgnore] public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    [JsonIgnore] public HybridSaveType SaveType => HybridSaveType.Json;
    public int ConfigVersion => 0;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.PlzNoCrashFriends).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["SpookyScarySkeletons"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }

    public NoCrashFriendsConfig(ILogger<NoCrashFriendsConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.PlzNoCrashFriends;
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
            Current = jObject["SpookyScarySkeletons"]?.ToObject<NoCrashFriendsStorage>() ?? new NoCrashFriendsStorage();
            Save();
        }
        catch (Bagagwa ex) { _logger.LogError("Failed to load config." + ex); }
    }

    public NoCrashFriendsStorage Current { get; private set; } = new();
}
