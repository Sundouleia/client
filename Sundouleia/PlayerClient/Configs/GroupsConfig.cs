using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class GroupsStorage
{
    public List<SundesmoGroup> Groups { get; set; } = new();
}

public class SundesmoGroup
{
    public FAI Icon { get; set; } = FAI.User;
    public string Label { get; set; } = string.Empty;
    public List<string> LinkedUids { get; set; } = new();
}

public class GroupsConfig : IHybridSavable
{
    private readonly ILogger<GroupsConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.SundesmoGroups).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Groups"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }
    public GroupsConfig(HybridSaveService saver)
    {
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.SundesmoGroups;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            return;
        }

        // Do not try-catch these, invalid loads of these should not allow the plugin to load.
        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        switch (version)
        {
            case 0:
                LoadV0(jObject["PairGroups"]);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        _logger.LogInformation("Config loaded.");
        Save();
    }

    private void LoadV0(JToken? data)
    {
        if (data is not JObject serverNicknames)
            return;
        Current = serverNicknames.ToObject<GroupsStorage>() ?? throw new Exception("Failed to load GroupsStorage.");
        // Clean up any invalid group entries. Invalid entries have empty names or an FAI value of 0.
        Current.Groups.RemoveAll(g => g.Icon == 0 || g.Label.IsNullOrWhitespace());
    }

    public GroupsStorage Current { get; set; } = new GroupsStorage();
}