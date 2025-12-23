using Dalamud.Interface.Textures.TextureWraps;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Utils;

namespace Sundouleia.ModularActor;

// ---- SMA FILE MANAGER -----
// RESPONSIBILITIES:
//  - Load in the ModularActorsConfig file to get info on all owned files.
//  - Construct loaded filedata from the respective files.
//  - Load in file contents of owned filedata as needed.
//  - Recover invalid files, remove, or locate missing files.
//  - Provide some way to import these within GPOSE.
//  - Allow files in here to be modified and updated.
public class SMAFileManager
{
    private readonly ILogger<SMAFileManager> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly SMAFileHandler _fileHandler;


    public SMAFileManager(ILogger<SMAFileManager> logger, MainConfig mainConfig,
        ModularActorsConfig smaConfig, SMAFileHandler fileHandler)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileHandler = fileHandler;

        CheckIntegrity();
    }

    public List<OwnedModularActorData> SMAD { get; private set; } = [];
    public List<OwnedModularActorBase> Bases { get; private set; } = [];
    public List<OwnedModularActorOutfit> Outfits { get; private set; } = [];
    public List<OwnedModularActorItem> Items { get; private set; } = [];

    public HashSet<Guid> InvalidFiles { get; private set; } = new();

    public void CheckIntegrity()
    {
        // Obtain all items from our config, and attempt to load them into the manager.
        // The actual data does not need to be calculated until requested.
        foreach (var (id, fileMeta) in _smaConfig.Current.OwnedSMADFiles)
        {
            // Do stuff
        }

        foreach (var (id, fileMeta) in _smaConfig.Current.OwnedSMABFiles)
        {
            if (!fileMeta.IsValid())
            {
                InvalidFiles.Add(id);
                continue;
            }

            // Otherwise, read in the file by header only.
            if (_fileHandler.LoadSmabFileHeader(fileMeta.FilePath) is not { } header)
            {
                InvalidFiles.Add(id);
                continue;
            }

            _logger.LogInformation($"Loaded SMA Base Header: {fileMeta.FilePath}");
            // Create a new OwnedModularActorBase object and append it to the list.
            // (Likely need something here to associate loaded bases with a matching loaded data or whatever)
            var newActorBase = new OwnedModularActorBase(fileMeta, header);
            Bases.Add(newActorBase);
        }

        foreach (var (id, fileMeta) in _smaConfig.Current.OwnedSMAOFiles)
        {
            // Do stuff
        }

        foreach (var (id, fileMeta) in _smaConfig.Current.OwnedSMAIFiles)
        {
            // Do stuff
        }

        _logger.LogInformation($"SMA File Manager Integrity Check Complete. Found {InvalidFiles.Count} invalid files.");
    }

    public void AddSavedBase(BaseFileDataSummary summary, string filePath, string fileKey, string? password = "")
    {
        if (!File.Exists(filePath))
            return;

        var dataHash = SundouleiaSecurity.GetFileHashSHA256(filePath);
        if (!_smaConfig.AddSMABFile(summary, filePath, dataHash, fileKey, password))
        {
            _logger.LogWarning($"Failed to add new SMAB file to config: {filePath}");
            return;
        }

        // Add it as a new OwnedModularActorBase.
        Bases.Add(new OwnedModularActorBase(_smaConfig.Current.OwnedSMABFiles[summary.FileId], summary));
        _logger.LogInformation($"Added new SMAB file to config and manager: {filePath}");
    }

    // Editor-based creation / build
    public ModularActorDataBuilder CreateBuilder()
    {
        return new ModularActorDataBuilder(this);
    }
}

public class ModularActorDataBuilder
{
    private readonly SMAFileManager _smaFileManager;

    public ModularActorBase? Base { get; set; }
    public List<ModularActorOutfit> Outfits { get; set; } = [];
    public List<ModularActorItem> Items { get; set; } = [];

    public ModularActorOutfit SelectedOutfit { get; set; }

    public List<ModularActorItem> SelectedItems { get; set; } = [];

    public ModularActorDataBuilder(SMAFileManager smaFileManager)
    {
        _smaFileManager = smaFileManager;
    }

    public void SaveToFile()
    {
        // Export it to the default export folder.
        // Can use the manager to update the containers for included bases.
    }
}