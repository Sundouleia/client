using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.ModularActor;

// ---- GPOSE MANAGER -----
// RESPONSIBILITIES:
//  - Load in SMA files (SMAD, SMAB, SMAO, SMAI, SMAIP)
//  - Extract file contents into their respective caches (store ones we dont have)
//  - Load in the modded dictionaries for owned file data we have (from the SMAFileManager)
//  - Be able to assemble SMA Files based on their respective allowances. (atm allow whatever but later dont)
//  - Revert / Clear all data and FileCache references upon GPose exit. (Maybe clear filecache in SMAFileHandler)
public class GPoseManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly SMAFileHandler _fileHandler;

    public GPoseManager(ILogger<GPoseManager> logger, SundouleiaMediator mediator,
        MainConfig mainConfig, ModularActorsConfig smaConfig, SMAFileHandler fileHandler)
        : base(logger, mediator)
    {
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
            Logger.LogWarning($"Failed to load SMA Base: {smabFilePath}");
            return;
        }

        Logger.LogInformation($"Loaded SMA Base: {smabFilePath}");
        // Create a new ModularActorData object and append it to the list.
        var newActorData = new ModularActorData(actorBase);
        // reference the SMAD in the SMAB.
        actorBase.Parent = newActorData;
        // Run the initial calculation over the base data.
        // Add the data and base to their respective lists.
        SMAD.Add(newActorData);
        Bases.Add(actorBase);
        Logger.LogInformation($"Created ModularActorData for Base: {smabFilePath}");
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

    public void RecalculateData(ModularActorData data)
    {
        // Recompile the finalized data for the latest data.
    }
}