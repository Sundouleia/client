using CkCommons.DrawSystem;
using CkCommons.HybridSaver;
using Sundouleia.Pairs;
using Sundouleia.Radar;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.DrawSystem;

public sealed class RadarDrawSystem : DynamicDrawSystem<RadarUser>, IMediatorSubscriber, IDisposable, IHybridSavable
{
    private readonly ILogger<RadarDrawSystem> _logger;
    private readonly RadarManager _radar;
    private readonly SundesmoManager _sundesmos;
    private readonly HybridSaveService _hybridSaver;

    public SundouleiaMediator Mediator { get; init; }

    public RadarDrawSystem(ILogger<RadarDrawSystem> logger, SundouleiaMediator mediator,
        RadarManager radar, SundesmoManager sundesmos, HybridSaveService saver)
    {
        _logger = logger;
        Mediator = mediator;
        _radar = radar;
        _sundesmos = sundesmos;
        _hybridSaver = saver;

        // Load the hierarchy and initialize the folders.
        LoadData();

        Mediator.Subscribe<FolderUpdateRadar>(this, _ => UpdateFolders());

        DDSChanged += OnChange;
        CollectionUpdated += OnCollectionUpdate;
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        DDSChanged -= OnChange;
        CollectionUpdated -= OnCollectionUpdate;
    }

    private void OnChange(DDSChange type, IDynamicNode<RadarUser> obj, IDynamicCollection<RadarUser>? _, IDynamicCollection<RadarUser>? __)
    {
        if (type is not (DDSChange.FullReloadStarting or DDSChange.FullReloadFinished))
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void OnCollectionUpdate(CollectionUpdate kind, IDynamicCollection<RadarUser> collection, IEnumerable<DynamicLeaf<RadarUser>>? _)
    {
        if (kind is CollectionUpdate.OpenStateChange)
            _hybridSaver.Save(this);
    }

    private void LoadData()
    {
        // If any changes occurred, re-save the file.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Radar)))
        {
            _logger.LogInformation("Changes detected during load, saving updated config.");
            _hybridSaver.Save(this);
        }
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> _)
    {
        // Add both folders accordingly if necessary.
        bool anyAdded = false;
        anyAdded |= TryAddFolder(FAI.Link, Constants.FolderTagRadarPaired, () => [.. _radar.RadarUsers.Where(u => _sundesmos.ContainsSundesmo(u.UID))]);
        anyAdded |= TryAddFolder(FAI.SatelliteDish, Constants.FolderTagRadarUnpaired, () => [.. _radar.RadarUsers.Where(u => !_sundesmos.ContainsSundesmo(u.UID))]);
        return anyAdded;
    }

    private bool TryAddFolder(FAI icon, string name, Func<IReadOnlyList<RadarUser>> gen)
        => AddFolder(new RadarFolder(root, idCounter + 1u, icon, name, gen, [ByName]));

    // Sort Helpers.
    private static readonly ISortMethod<DynamicLeaf<RadarUser>> ByName = new SorterExtensions.RadarName();

    // HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.StreamWrite;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = false, files.DDS_Radar).Item2;

    public string JsonSerialize()
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer)
        => SaveToFile(writer);
}

