using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.HybridSaver;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.DrawSystem;

public class WhitelistDrawSystem : DynamicDrawSystem<Sundesmo>, IMediatorSubscriber, IDisposable, IHybridSavable
{
    private readonly ILogger<WhitelistDrawSystem> _logger;
    private readonly FolderConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly HybridSaveService _hybridSaver;

    private readonly object _folderUpdateLock = new();

    public SundouleiaMediator Mediator { get; init; }

    public WhitelistDrawSystem(ILogger<WhitelistDrawSystem> logger, SundouleiaMediator mediator,
        FolderConfig config, SundesmoManager sundesmos, HybridSaveService saver)
    {
        _logger = logger;
        Mediator = mediator;
        _config = config;
        _sundesmos = sundesmos;
        _hybridSaver = saver;

        // Load the hierarchy and initialize the folders.
        LoadData();

        // These can possibly occur at the same time and must be accounted for.
        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => { lock (_folderUpdateLock) UpdateFolders(); });
        Mediator.Subscribe<SundesmoPlayerRendered>(this, _ => { lock (_folderUpdateLock) UpdateFolder(Constants.FolderTagVisible); });
        Mediator.Subscribe<ConnectedMessage>(this, _ => { lock (_folderUpdateLock) UpdateFolders(); });

        // Subscribe to the changes (which is to change very, very soon, with overrides.
        DDSChanged += OnChange;
        CollectionUpdated += OnCollectionUpdate;
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        DDSChanged -= OnChange;
        CollectionUpdated -= OnCollectionUpdate;
    }

    // Note that this will change very soon, as saves should only occur for certain changes.
    private void OnChange(DDSChange type, IDynamicNode<Sundesmo> obj, IDynamicCollection<Sundesmo>? _, IDynamicCollection<Sundesmo>? __)
    {
        if (type is not (DDSChange.FullReloadStarting or DDSChange.FullReloadFinished))
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void OnCollectionUpdate(CollectionUpdate kind, IDynamicCollection<Sundesmo> collection, IEnumerable<DynamicLeaf<Sundesmo>>? _)
    {
        if (kind is CollectionUpdate.OpenStateChange)
            _hybridSaver.Save(this);
    }

    private void LoadData()
    {
        // Before we load anything, inverse the sort direction of root.
        SetSortDirection(root, true);
        // If any changes occured, re-save the file.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Whitelist)))
        {
            _logger.LogInformation("WhitelistDrawSystem folder structure changed on load, saving updated structure.");
            _hybridSaver.Save(this);
        }
        // See if the file doesnt exist, if it does not, load defaults.
        else if (!File.Exists(_hybridSaver.FileNames.DDS_Whitelist))
        {
            _logger.LogInformation("Loading Defaults and saving.");
            EnsureAllFolders(new Dictionary<string, string>());
            _hybridSaver.Save(this);
        }
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> _)
    {
        // Load in the folders, they are all descendants of root.
        bool anyChanged = false;
        anyChanged |= UpdateVisibleFolderState(_config.Current.VisibleFolder);
        anyChanged |= UpdateOfflineFolderState(_config.Current.OfflineFolder);
        _logger.LogInformation($"Ensured all folders, total now {FolderMap.Count} folders.");
        return anyChanged;
    }

    // Update the FolderSystem folders based on if it should be included or not.
    public bool UpdateVisibleFolderState(bool showFolder)
    {
        // If we want to show the folder and it already exists then change nothing.
        if (showFolder)
        {
            if (FolderMap.ContainsKey(Constants.FolderTagVisible))
                return false;

            // Try to add it.
            return AddFolder(new DefaultFolder(root, idCounter + 1u, FAI.Eye, Constants.FolderTagVisible, CkColor.TriStateCheck.Uint(), 
                () => [.. _sundesmos.DirectPairs.Where(u => u.IsRendered && u.IsOnline)], GetDefaultSorter()));
        }
        // Otherwise attempt to remove it.
        return Delete(Constants.FolderTagVisible);
    }

    // Not too worried about additional work here since it only happens on recalculations.
    public bool UpdateOfflineFolderState(bool showFolder)
    {
        // Assume no changes.
        bool anyChanges = false;
        // If we wanted to show offline/online..
        if (showFolder)
        {
            var sorter = GetDefaultSorter();
            anyChanges |= Delete(Constants.FolderTagAll);
            anyChanges |= AddFolder(new DefaultFolder(root, idCounter + 1u, FAI.Link, Constants.FolderTagOnline, CkColor.TriStateCheck.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => s.IsOnline)], new(sorter)));
            anyChanges |= AddFolder(new DefaultFolder(root, idCounter + 1u, FAI.Link, Constants.FolderTagOffline, CkColor.TriStateCross.Uint(), () => [.. _sundesmos.DirectPairs.Where(s => !s.IsOnline)], new(sorter)));
        }
        // Otherwise we wanted to only show ALL.
        else
        {
            var sorter = GetDefaultSorter();
            sorter.Prepend(SorterExtensions.ByRendered);
            sorter.Prepend(SorterExtensions.ByOnline);

            anyChanges |= Delete(Constants.FolderTagOnline);
            anyChanges |= Delete(Constants.FolderTagOffline);
            anyChanges |= AddFolder(new DefaultFolder(root, idCounter + 1u, FAI.Globe, Constants.FolderTagAll, uint.MaxValue, () => _sundesmos.DirectPairs, sorter));
        }
        // Return if anything was modified.
        return anyChanges;
    }

    public void UpdateFilters()
    {
        var sorter = GetDefaultSorter();
        // Update all children to be either favorites first or not.
        foreach (var folder in Root.Children.OfType<DefaultFolder>())
            SetSorterSteps(folder, sorter);
    }

    private IReadOnlyList<ISortMethod<DynamicLeaf<Sundesmo>>> GetDefaultSorter()
        => (_config.Current.PrioritizeFavorites, _config.Current.PrioritizeTemps) switch
        {
            (true, true) => [SorterExtensions.ByTemporary, SorterExtensions.ByFavorite, SorterExtensions.ByPairName],
            (true, false) => [SorterExtensions.ByFavorite, SorterExtensions.ByPairName],
            (false, true) => [SorterExtensions.ByTemporary, SorterExtensions.ByPairName],
            _ => [SorterExtensions.ByPairName]
        };

    // HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = false, files.DDS_Whitelist).Item2;

    public string JsonSerialize()
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer)
        => SaveToFile(writer);
}
