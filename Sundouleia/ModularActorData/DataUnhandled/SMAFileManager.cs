using Dalamud.Interface.Textures.TextureWraps;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;

namespace Sundouleia.ModularActor;

public class SMAFileManager
{
    private readonly ILogger<SMAFileManager> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;

    public SMAFileManager(ILogger<SMAFileManager> logger, MainConfig mainConfig,
        ModularActorsConfig smaConfig, FileCacheManager fileCache,
        SMAFileCacheManager smaFileCache)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileCache = fileCache;
        _smaFileCache = smaFileCache;

        InitializeData();
    }

    public List<ModularActorData> SMAD { get; private set; } = [];
    public List<ModularActorBase> Bases { get; private set; } = [];
    public List<ModularActorOutfit> Outfits { get; private set; } = [];
    public List<ModularActorItem> Items { get; private set; } = [];


    public void InitializeData()
    {
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