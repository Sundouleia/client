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

    public GroupFolder(DynamicFolderGroup<Sundesmo> parent, uint id, SundesmoManager sundesmos, 
        SundesmoGroup g, IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> sortSteps)
        : base(parent, g.Icon, g.Label, id, new(sortSteps))
    {
        // Store the group.
        _group = g;
        // Define the generator.
        _generator = () => [.. sundesmos.DirectPairs.Where(u => g.LinkedUids.Contains(u.UserData.UID))];
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

        // Before loading the data, re-define root with no sorter.
        root = DynamicFolderGroup<Sundesmo>.CreateRoot();

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
        // Handles loading, folder assignment, and setting opened states all in one.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Groups)))
        {
            _logger.LogWarning("Loaded GroupDrawSystem from file.");
            // Re-Save the file after all data is loaded and applied.
            _hybridSaver.Save(this);
        }
        else
            _logger.LogWarning("No saved GroupDrawSystem file found, starting fresh.");
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> map)
    {
        // Grab all groups from the group manager.
        var toCreate = _groups.Groups;
        var anyCreated = false;

        // For each existing group, ensure its folder exists.
        // If it is in the folder map, assign it to the respective parent, otherwise root.
        foreach (var groupToAdd in toCreate)
        {
            // If the folder exists, continue to prevent unnecessary work.
            if (FolderExists(groupToAdd.Label))
                continue;

            // It does not exist, so try and obtain it via mapping, with root as fallback.
            var parent = map.TryGetValue(groupToAdd.Label, out var pn) && TryGetFolderGroup(pn, out var match)
                ? match : root;
            // Now that we have defined the parent, ensure we are creating with the next peeked id.
            anyCreated |= TryAddFolder(parent, groupToAdd);
        }

        // Dont forget to add the 'AllSundesmos' folder at the end, the WhitelistFolder.
        anyCreated |= TryAddAllFolder();

        // Return true if any folders were created.
        return anyCreated;
    }

    /// <summary>
    ///     Helper to get if a folder already exists to skip excessive computation.
    /// </summary>
    private bool FolderExists(string folderName)
        => FolderMap.ContainsKey(folderName);

    /// <summary>
    ///     Attempts to add a folder to the DrawSystem.
    /// </summary>
    private bool TryAddFolder(DynamicFolderGroup<Sundesmo> parent, SundesmoGroup group)
        => AddFolder(new GroupFolder(parent, idCounter + 1u, _sundesmos, group, FromGroup(group)));

    // Special 'All Sundesmos' folder addition for the groups system.
    private bool TryAddAllFolder()
        => AddFolder(new WhitelistFolder(root, idCounter + 1u, FAI.Globe, Constants.FolderTagAll,
            uint.MaxValue, () => _sundesmos.DirectPairs, DynamicSorterEx.AllFolderSorter));

    /// <summary>
    ///     Parses out a DynamicSorter Constructor from a FolderGroup's SortOrder.
    /// </summary>
    private List<ISortMethod<DynamicLeaf<Sundesmo>>> FromGroup(SundesmoGroup group)
        => group.SortOrder.Select(f => f.ToSortMethod()).ToList();

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

