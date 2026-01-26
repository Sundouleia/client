using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using Sundouleia.Services;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;

public class FolderStorage
{
    // All existing SundesmoGroups, keyed by their Label.
    // Renamed Groups should properly retain references.
    public Dictionary<string, SundesmoGroup> Groups { get; set; } = new();

    // WhitelistFolder Swapper
    public bool ViewingGroups { get; set; } = false;

    // Main WhitelistFolders config.
    public bool FavoritesFirst { get; set; } = true;
    public bool NickOverPlayerName { get; set; } = false;
    public bool VisibleFolder { get; set; } = true;
    public bool OfflineFolder { get; set; } = true;
    public bool TargetWithFocus { get; set; } = false;

    // Groups config options can be added here.
    public bool StyleEditing   { get; set; } = false;
    public bool FilterEditing { get; set; } = false;
    public bool LocationEditing { get; set; } = false;
}

public class SundesmoGroup
{
    public FAI Icon { get; set; } = FAI.User;
    public string Label { get; set; } = string.Empty;
    public uint IconColor { get; set; } = 0xFFFFFFFF;
    public uint LabelColor { get; set; } = 0xFFFFFFFF;
    public uint BorderColor { get; set; } = ImGui.GetColorU32(ImGuiCol.TextDisabled);
    public uint GradientColor { get; set; } = ImGui.GetColorU32(ImGuiCol.TextDisabled);
    public bool ShowOffline { get; set; } = true;

    // Could move elsewhere later but here seems best for now.
    public bool InBasicView { get; set; } = false;

    // The UserUID's contained in this group.
    public HashSet<string> LinkedUids { get; set; } = new();

    // Becomes a DynamicSorter over conversion.
    public List<FolderSortFilter> SortOrder { get; set; } = new();

    /// <summary>
    ///     If the group auto-adds pairs that appear in the same scoped location set for the Group.
    /// </summary>
    public bool AreaBound { get; set; } = false;

    /// <summary>
    ///     The scope for AreaBound matching.
    /// </summary>
    public LocationScope Scope { get; set; } = LocationScope.None;
    
    /// <summary>
    ///     Location Data used in junction with Scope for matching.
    /// </summary>
    public LocationEntry Location { get; set; } = new();
}

// Configuration for everything relating to dynamic folder and draw entity displays.
public class FolderConfig : IHybridSavable
{
    private readonly ILogger<FolderConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 1;
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
                // Migrate to V1 first, then load V1.
                MigrateV0toV1(jObject);
                LoadV1(jObject["Config"]);
                break;
            case 1:
                LoadV1(jObject["Config"]);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        Save();
    }

    /// <summary>
    /// Upgrades a legacy V0 config JObject to V1 structure in-place.
    /// Modifies the passed JObject directly.
    /// </summary>
    private void MigrateV0toV1(JObject v0Data)
    {
        if (v0Data == null)
            throw new ArgumentNullException(nameof(v0Data));

        if (v0Data["Config"] is not JObject configToken)
            throw new ArgumentNullException("No Config JToken Exists!");

        // --- Migrate Groups from JArray to dictionary keyed by Label ---
        if (configToken["Groups"] is JArray legacyGroups)
        {
            var groupsDict = new JObject();

            foreach (var groupToken in legacyGroups)
            {
                var group = groupToken.ToObject<SundesmoGroup>();
                if (group == null || group.Label.IsNullOrWhitespace())
                    continue;

                if (!groupsDict.ContainsKey(group.Label))
                    groupsDict[group.Label] = JObject.FromObject(group);
            }

            // Replace the old Groups array with the new dictionary
            configToken["Groups"] = groupsDict;
        }
    }

    private void LoadV1(JToken? data)
    {
        if (data is not JObject serverNicknames)
            return;
        Current = serverNicknames.ToObject<FolderStorage>() ?? throw new Exception("Failed to load FolderStorage.");
        // Clean up any invalid group entries. Invalid entries have empty names or an FAI value of 0.
        foreach (var key in Current.Groups
            .Where(kvp => kvp.Value.Icon == 0 || kvp.Value.Label.IsNullOrWhitespace())
            .Select(kvp => kvp.Key)
            .ToList())
        {
            Current.Groups.Remove(key);
        }
    }

    public FolderStorage Current { get; set; } = new FolderStorage();


}