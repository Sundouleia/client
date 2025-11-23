using CkCommons.HybridSaver;
using Dalamud.Bindings.ImGui;
using Sundouleia.Pairs;
using Sundouleia.Radar;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.DrawSystem;

public sealed class RadarFolder : DynamicFolder<RadarUser>
{
    private Func<IReadOnlyList<RadarUser>> _generator;
    public RadarFolder(DynamicFolderGroup<RadarUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarUser>> gen)
        : base(parent, icon, name, id)
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = gen;
    }

    public RadarFolder(DynamicFolderGroup<RadarUser> parent, uint id, FAI icon, string name,
        Func<IReadOnlyList<RadarUser>> generator, IReadOnlyList<ISortMethod<DynamicLeaf<RadarUser>>> sortSteps)
        : base(parent, icon, name, id, new(sortSteps))
    {
        // Can set stylizations here.
        BorderColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _generator = generator;
    }

    public int Rendered => Children.Count(s => s.Data.IsValid);
    public int Lurkers => Children.Count(s => !s.Data.IsValid);
    protected override IReadOnlyList<RadarUser> GetAllItems() => _generator();
    protected override DynamicLeaf<RadarUser> ToLeaf(RadarUser item) => new(this, item.UID, item);
}

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

        Changed += OnChange;
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
        Changed -= OnChange;
    }

    // Note that this will change very soon, as saves should only occur for certain changes.
    private void OnChange(DDSChangeType type, IDynamicNode<RadarUser> obj, IDynamicCollection<RadarUser>? prevParent, IDynamicCollection<RadarUser>? newParent)
    {
        if (type != DDSChangeType.Reload)
        {
            _logger.LogInformation($"DDS Change [{type}] for node [{obj.Name} ({obj.FullPath})] occured. Saving Config.");
            _hybridSaver.Save(this);
        }
    }

    private void LoadData()
    {
        // If any changes occured, re-save the file.
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Radar)))
        {
            _logger.LogInformation("Changes detected during load, saving updated config.");
            _hybridSaver.Save(this);
        }
    }

    protected override bool EnsureAllFolders(Dictionary<string, string> _)
    {
        // Add both folders accordingly if nessisary.
        bool anyAdded = false;
        anyAdded |= TryAddFolder(FAI.Link, Constants.FolderTagRadarPaired, () => [.. _radar.RadarUsers.Where(u => _sundesmos.ContainsSundesmo(u.UID))]);
        anyAdded |= TryAddFolder(FAI.SatelliteDish, Constants.FolderTagRadarUnpaired, () => [.. _radar.RadarUsers.Where(u => !_sundesmos.ContainsSundesmo(u.UID))]);
        return anyAdded;
    }

    private bool TryAddFolder(FAI icon, string name, Func<IReadOnlyList<RadarUser>> gen)
        => AddFolder(new RadarFolder(root, idCounter + 1u, icon, name, gen, [ByName]));

    // Sort Helpers.
    private static readonly ISortMethod<DynamicLeaf<RadarUser>> ByName = new DynamicSorterEx.RadarName();

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

