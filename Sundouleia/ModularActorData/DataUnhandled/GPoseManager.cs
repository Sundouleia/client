using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.FileSystemGlobbing;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;

namespace Sundouleia.ModularActor;

/// <summary>
///     Manager for all owned files.
/// </summary>
public class GPoseManager
{
    private readonly ILogger<GPoseManager> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly SMAFileHandler _fileHandler;

    public GPoseManager(ILogger<GPoseManager> logger, MainConfig mainConfig,
        ModularActorsConfig smaConfig, SMAFileHandler fileHandler)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileHandler = fileHandler;
    }

    public List<ModularActorData> SMAD { get; private set; } = [];
    public List<ModularActorBase> Bases { get; private set; } = [];
    public List<ModularActorOutfit> Outfits { get; private set; } = [];
    public List<ModularActorItem> Items { get; private set; } = [];

    public void LoadSMADFile(string smadFilePath)
    {
        // Attempt to load a SMAD file.
    }

    public void LoadSMABFile(string smabFilePath)
    {
        if (_fileHandler.LoadSmabFile(smabFilePath) is not { } actorBase)
        {
            _logger.LogWarning($"Failed to load SMA Base: {smabFilePath}");
            return;
        }

        _logger.LogInformation($"Loaded SMA Base: {smabFilePath}");
        // Create a new ModularActorData object and append it to the list.
        var newActorData = new ModularActorData(actorBase);
        // reference the SMAD in the SMAB.
        actorBase.Parent = newActorData;
        // Add the data and base to their respective lists.
        SMAD.Add(newActorData);
        Bases.Add(actorBase);
        _logger.LogInformation($"Created ModularActorData for Base: {smabFilePath}");
    }

    public void LoadSMAOFile(string smaoFilePath)
    {
        // Attempt to load an outfit file.
    }

    public void LoadSMAIFile(string smaiFilePath)
    {
        // Attempt to load an item file.
    }

    public void LoadSMAIPFile(string smaipFilePath)
    {
        // Attempt to load an item pack file.
    }

    // Attempt to load in multiple of any kind.
    // Accept regardless of inclusion (maybe?) (or reject if no base)

    public void LoadFiles(IEnumerable<string> filePaths)
    {
        // Attempt to load in all of the files. 


        // Get the directory we expect the files to be in
        var dir = _mainConfig.Current.SMAExportFolder;

        // If the string was not set, return.
        if (string.IsNullOrEmpty(dir))
            return;

        // If the directory does not exist, create it, and then exit.
        if (Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        // Process over all files and attempt to load them.
        // Even if they fail, we should create a dummy object and mark it as invalid.

        // Load all items first.

        // Load all outfits next.

        // Load all bases next.

        // Load all SMAD files last.

    }

    public void RemoveItem(ModularActorItem item)
    {
        Items.Remove(item);
    }

    public void RemoveOutfit(ModularActorOutfit outfit)
    {
        Outfits.Remove(outfit);
    }

    public void RemoveBase(ModularActorBase actorBase)
    {
        Bases.Remove(actorBase);
    }
}