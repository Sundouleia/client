using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sundouleia.DrawSystem;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class FolderStorage
{
    // All Created groups. Not tracked as a dictionary with label keys to allow renaming while keeping references.
    public List<SundesmoGroup> Groups { get; set; } = new();

    // Cached sort order filters. Maybe revise later idk.
    public List<FolderSortPreset> SortPresets { get; set; } = new();

    // RequestFolder Swapper
    public bool ViewingIncoming { get; set; } = true;

    // WhitelistFolder Swapper
    public bool ViewingGroups { get; set; } = false;

    // Main WhitelistFolders config.
    public bool FavoritesFirst { get; set; } = true;
    public bool NickOverPlayerName { get; set; } = false;
    public bool VisibleFolder { get; set; } = true;
    public bool OfflineFolder { get; set; } = true;
    public bool TargetWithFocus { get; set; } = false;
}

public class SundesmoGroup
{
    public FAI Icon { get; set; } = FAI.User;
    public string Label { get; set; } = string.Empty;
    public uint IconColor { get; set; } = 0xFFFFFFFF;
    public uint LabelColor { get; set; } = 0xFFFFFFFF;
    public uint BorderColor { get; set; } = ImGui.GetColorU32(ImGuiCol.TextDisabled);
    public uint GradientColor { get; set; } = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.TextDisabled), .9f);
    public bool ShowIfEmpty { get; set; } = true;
    public bool ShowOffline { get; set; } = true;
    public List<FolderSortFilter> SortOrder { get; set; } = new(); // Refactor?
    public HashSet<string> LinkedUids { get; set; } = new();
}

public record FolderSortPreset(string Name, List<FolderSortFilter> SortFilters);

// Configuration for everything relating to dynamic folder and draw entity displays.
public class FolderConfig : IHybridSavable
{
    private readonly ILogger<FolderConfig> _logger;
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
            ["Config"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }
    public FolderConfig(ILogger<FolderConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
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
                LoadV0(jObject["Config"]);
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
        Current = serverNicknames.ToObject<FolderStorage>() ?? throw new Exception("Failed to load GroupsStorage.");
        // Clean up any invalid group entries. Invalid entries have empty names or an FAI value of 0.
        Current.Groups.RemoveAll(g => g.Icon == 0 || g.Label.IsNullOrWhitespace());
    }

    public FolderStorage Current { get; set; } = new FolderStorage();

    public bool LabelExists(string l) 
         => Constants.OwnedFolders.Contains (l) || Current.Groups.Any(g => g.Label == l);
}