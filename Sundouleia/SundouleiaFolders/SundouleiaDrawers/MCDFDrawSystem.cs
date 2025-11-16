using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.DrawSystem;

public class MCDFDummyData
{
    // Placeholder for now.
}

public sealed class MCDFDrawSystem : DynamicDrawSystem<MCDFDummyData>, IHybridSavable
{
    private readonly ILogger<MCDFDrawSystem> _logger;
    // some manager for the MCDF save data stuff.
    private readonly HybridSaveService _hybridSaver;

    public MCDFDrawSystem(ILogger<MCDFDrawSystem> logger, HybridSaveService saver)
    {
        _logger = logger;
        _hybridSaver = saver;

        // Perform an initial reload of the folder structure.
        Reload();

        // Subscribe to the changes (which is to change very, very soon, with overrides.
        // Changed += OnChange;
    }

    private void Reload()
    {
        if (LoadFile(new FileInfo(_hybridSaver.FileNames.DDS_MCDFData)))
            _hybridSaver.Save(this);
        // The above will simply load in any saved structure and folder opened state, if we cannot get a way to
        // Generate the folders we want to have generated before, then modify the structure, but we will add it soon™.
        _logger.LogDebug("Reloaded MCDFDrawSystem.");
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
        => (isAccountUnique = false, files.DDS_MCDFData).Item2;

    public string JsonSerialize() 
        => throw new NotImplementedException();

    public void WriteToStream(StreamWriter writer) 
        => SaveToFile(writer);
}

