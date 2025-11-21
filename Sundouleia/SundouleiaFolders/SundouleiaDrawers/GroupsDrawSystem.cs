using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using OtterGui.Text.Widget.Editors;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using TerraFX.Interop.Windows;

namespace Sundouleia.DrawSystem;

public sealed class GroupFolder : DynamicFolder<Sundesmo>
{
    // We store this to have a dynamically generated list without the need of a generator.
    private SundesmoGroup _group;
    private Func<IReadOnlyList<Sundesmo>> _generator;
    public GroupFolder(DynamicFolderGroup<Sundesmo> parent, uint id, SundesmoManager sundesmos, SundesmoGroup g)
        : base(parent, g.Icon, g.Label, id)
    {
        _group = g;
        // Define the generator.
        _generator = () => [.. sundesmos.DirectPairs.Where(u => g.LinkedUids.Contains(u.UserData.UID) && (g.ShowOffline || u.IsOnline))];
        // Apply Stylizations.
        ApplyGroupData();
    }

    public int Rendered => Children.Count(s => s.Data.IsRendered);
    public int Online => Children.Count(s => s.Data.IsOnline);
    protected override IReadOnlyList<Sundesmo> GetAllItems() => _generator();
    protected override DynamicLeaf<Sundesmo> ToLeaf(Sundesmo item) => new(this, item.UserData.UID, item);

    /// <summary>
    ///     Reapplies the Folder's SundesmoGroup data to the folder. <para/>
    ///     This includes all stylizations, and renaming. <para/>
    ///     <b> Be sure that you know what to update in the DrawSystem after calling this, if it returns true. </b>
    /// </summary>
    /// <returns> If the folder was renamed from the application and requires an update. </returns>
    public bool ApplyGroupData()
    {
        var oldLabel = Name;

        Icon = _group.Icon;
        IconColor = _group.IconColor;
        NameColor = _group.LabelColor;
        BorderColor = _group.BorderColor;
        // Update the flags.
        SetShowEmpty(_group.ShowIfEmpty);

        var newName = oldLabel != _group.Label;
        // Change labels if different.
        if (newName)
            SetName(_group.Label, true);
        // Return if we should update the folder's items.
        return newName;
    }

    // something to convert FolderSortFilter's to the FolderSortMethod<Sundesmo> items.
}

public sealed class GroupsDrawSystem : DynamicDrawSystem<Sundesmo>, IMediatorSubscriber, IDisposable, IHybridSavable
{
    private readonly ILogger<GroupsDrawSystem> _logger;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly HybridSaveService _hybridSaver;

    public SundouleiaMediator Mediator { get; init; }

    public GroupsDrawSystem(ILogger<GroupsDrawSystem> logger, SundouleiaMediator mediator,
        GroupsManager groups, SundesmoManager sundesmos, HybridSaveService saver)
    {
        _logger = logger;
        Mediator = mediator;
        _groups = groups;
        _sundesmos = sundesmos;
        _hybridSaver = saver;

        // Load the hierarchy and initialize the folders.
        LoadData();

        // Until the below is polished/fixed, every change from either of these sources will trigger 2x refresh!

        // TODO: Revise this to listen for spesific group changes, and perform respective changes to those folders only.
        Mediator.Subscribe<FolderUpdateGroups>(this, _ => UpdateFolders());

        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => UpdateFolders());

        // Subscribe to the changes (which is to change very, very soon, with overrides.
        Changed += OnChange;
    }

    public void Dispose()
    {
        Changed -= OnChange;
    }

    // Note that this will change very soon, as saves should only occur for certain changes.
    private void OnChange(DDSChangeType type, IDynamicNode<Sundesmo> obj, IDynamicCollection<Sundesmo>? prevParent, IDynamicCollection<Sundesmo>? newParent)
    {
        if (type != DDSChangeType.Reload)
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void LoadData()
    {
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Whitelist), out Dictionary<string, string> folderMap, out List<string> openedCollections))
        {
            _logger.LogDebug("Loaded WhitelistDrawSystem from file.");
            // Load in the folders (we dont care about the parent state, they are all root here)
            LoadGroupFolders(folderMap);
            // Now process OpenedState via the OpenedCollections. (could do this in the above method or not, idk. Maybe best to do seperate)
            OpenFolders(openedCollections);
            // Re-Save the file after all data is loaded and applied.
            _hybridSaver.Save(this);
        }
        else
            _logger.LogDebug("No saved WhitelistDrawSystem file found, starting fresh.");
    }

    private void LoadGroupFolders(Dictionary<string, string> folderMap)
    {
        // TODO:
        // - Load in all folders from the GroupManager.
        // - For all of the folders, assign them the parent defined in the folder map, or root if not in the map.
    }

    private void OpenFolders(List<string> openedCollections)
    {
        // TODO:
        // - Open all folders matching the name in the list.
    }

    // HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = true, files.DDS_Groups).Item2;

    public string JsonSerialize() 
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer) 
        => SaveToFile(writer);
}

