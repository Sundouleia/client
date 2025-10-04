using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class NickStorage
{
    public Dictionary<string, string> Nicknames { get; set; } = new(StringComparer.Ordinal);
}

public class NickConfig : IHybridSavable
{
    private readonly ILogger<NickConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.NicknameConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Nicknames"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }
    public NickConfig(ILogger<NickConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.NicknameConfig;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            _saver.Save(this);
            return;
        }

        // Do not try-catch these, invalid loads of these should not allow the plugin to load.
        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        switch (version)
        {
            case 0:
                LoadV0(jObject["Nicknames "]);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        Save();
    }

    private void LoadV0(JToken? data)
    {
        if (data is not JObject serverNicknames)
            return;
        Current = serverNicknames.ToObject<NickStorage>() ?? throw new Exception("Failed to load NicknamesStorage.");
        // clean out any kvp with null or whitespace values.
        foreach (var kvp in Current.Nicknames.Where(kvp => string.IsNullOrWhiteSpace(kvp.Value)).ToList())
            Current.Nicknames.Remove(kvp.Key);
    }

    public NickStorage Current { get; set; } = new NickStorage();
}