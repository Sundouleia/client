using CkCommons.HybridSaver;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;

namespace Sundouleia.DrawSystem;

public sealed class GroupsDrawSystem : DynamicDrawSystem<Sundesmo>, IHybridSavable
{
    private readonly ILogger<GroupsDrawSystem> _logger;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly HybridSaveService _hybridSaver;

    public GroupsDrawSystem(ILogger<GroupsDrawSystem> logger, GroupsManager groups,
        SundesmoManager sundesmos, HybridSaveService saver)
    {
        _logger = logger;
        _groups = groups;
        _sundesmos = sundesmos;
        _hybridSaver = saver;

        // Perform an initial reload of the folder structure.
        Reload();

        // Subscribe to the changes (which is to change very, very soon, with overrides.
        // Changed += OnChange;
    }

    private void Reload()
    {
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_Groups)))
            _hybridSaver.Save(this);
        // The above will simply load in any saved structure and folder opened state, if we cannot get a way to
        // Generate the folders we want to have generated before, then modify the structure, but we will add it soon™.
        _logger.LogDebug("Reloaded GroupsDrawSystem.");
    }

    // TODO: Bomb this with airstrikes and replace it with override methods later.
    //private void OnChange(FileSystemChangeType type, IPath _1, IPath? _2, IPath? _3)
    //{
    //    if (type != FileSystemChangeType.Reload)
    //        _hybridSaver.Save(this);
    //}

    // Can add additional methods for helpers with the RadarFolderConstruction here later.

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

