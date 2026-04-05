using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

/// <summary>
///   NOTE: None of these calculations are actually fully accurate.
/// </summary>
public class PerformanceStorage
{
    // Texture compression here maybe, idk lol. Model calculations are a lie anyways.
    public bool ShowOwnPerformance { get; set; } = true;
    public bool WarnOnOwnExceeding { get; set; } = true;
    public bool WarnOnOtherExceeding { get; set; } = true;
    public int VRAMWarningThresholdMiB { get; set; } = 375;
    public int VRAMPauseThresholdMiB { get; set; } = 550;
    public int TrisWarnThreshold { get; set; } = 165000;
    public int TrisPauseThreshold { get; set; } = 250000;
    public bool IgnoreForPairs { get; set; } = true;
}

public class PerformanceConfig : IHybridSavable
{
    private readonly ILogger<PerformanceConfig> _logger;
    private readonly HybridSaveService _saver;

    // Hybrid Savable stuff
    [JsonIgnore] public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    [JsonIgnore] public HybridSaveType SaveType => HybridSaveType.Json;
    public int ConfigVersion => 0;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.PerformanceConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Config"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }

    public PerformanceConfig(ILogger<PerformanceConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save()
        => _saver.Save(this);
    
    public void Load()
    {
        var file = _saver.FileNames.ChatConfig;
        _logger.LogInformation($"Loading in PerformanceConfig: {file}");
        if (!File.Exists(file))
        {
            _logger.LogWarning($"PerformanceConfig file not found: {file}");
            _saver.Save(this);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

            // Load instance configuration
        Current = jObject["Config"]?.ToObject<PerformanceStorage>() ?? new PerformanceStorage();
        Save();
    }

    public PerformanceStorage Current { get; private set; } = new();
}
