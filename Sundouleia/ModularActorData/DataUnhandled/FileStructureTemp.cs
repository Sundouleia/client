using Dalamud.Interface.Textures.TextureWraps;
using K4os.Compression.LZ4.Legacy;
using Lumina.Data.Parsing.Scd;
using Penumbra.String.Classes;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Watchers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;

/// <summary>
///     Manager for all owned files.
/// </summary>
public class GPoseManager
{
    private readonly ILogger<GPoseManager> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;

    public GPoseManager(ILogger<GPoseManager> logger, MainConfig mainConfig,
        ModularActorsConfig smaConfig, FileCacheManager fileCache,
        SMAFileCacheManager smaFileCache)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileCache = fileCache;
        _smaFileCache = smaFileCache;
    }

    public List<ModularActorData>   SMAD    { get; private set; } = [];
    public List<ModularActorBase>   Bases   { get; private set; } = [];
    public List<ModularActorOutfit> Outfits { get; private set; } = [];
    public List<ModularActorItem>   Items   { get; private set; } = [];

    public void LoadSMADFile(string smadFilePath)
    {
        // Attempt to load a SMAD file.
    }

    public void LoadSMABFile(string smabFilePath)
    {
        // Attempt to load a base file.
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

public abstract class FileDataSummary
{
    public Guid FileId { get; init; }
    public string Name { get; protected set; } = string.Empty;
    public string Description { get; protected set; } = string.Empty;
    public string ThumbnailBase64 { get; protected set; } = string.Empty; // Not used in base.

    public JObject GlamourState { get; protected set; } = new();

    public string ModManips { get; protected set; } = string.Empty;
    public List<FileModData> Files { get; protected set; } = [];
    public List<FileSwap> FileSwaps { get; protected set; } = [];

    //public byte[] ToByteArray()
    //    => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this));

    //// Convert a byte array of data back into a ModularActorBaseFileData object.
    //public static ActorBaseFileData FromByteArray(byte[] data)
    //    => System.Text.Json.JsonSerializer.Deserialize<ActorBaseFileData>(Encoding.UTF8.GetString(data))!;
}

public class BaseFileDataSummary : FileDataSummary
{
    public string CPlusData { get; set; } = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{}")); // Default empty JSON.

    public BaseFileDataSummary()
    { }

    public byte[] ToByteArray()
        => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this));

    // Convert a byte array of data back into a ModularActorBaseFileData object.
    public static BaseFileDataSummary FromByteArray(byte[] data)
        => System.Text.Json.JsonSerializer.Deserialize<BaseFileDataSummary>(Encoding.UTF8.GetString(data))!;
}

public class OutfitFileDataSummary : FileDataSummary
{
    public SMAGlamourParts PartsFilter { get; set; } = SMAGlamourParts.None;
    public SMAFileSlotFilter SlotFilter { get; set; } = SMAFileSlotFilter.MainHand;
    public SMAFileMetaFilter MetaFilter { get; set; } = SMAFileMetaFilter.None;

    // Optional: trimmed snapshot of GlamourState with filters? (Or maybe just keep full and trim on save?)
    // ((OR just keep the trimmed only and do a recalculation each time.))

    public byte[] ToByteArray()
        => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this));

    // Convert a byte array of data back into a ModularActorBaseFileData object.
    public static OutfitFileDataSummary FromByteArray(byte[] data)
        => System.Text.Json.JsonSerializer.Deserialize<OutfitFileDataSummary>(Encoding.UTF8.GetString(data))!;
}

public class ItemFileDataSummary : FileDataSummary
{
    // Items do not have any extra data for now.
    public byte[] ToByteArray()
    => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this));

    // Convert a byte array of data back into a ModularActorBaseFileData object.
    public static ItemFileDataSummary FromByteArray(byte[] data)
        => System.Text.Json.JsonSerializer.Deserialize<ItemFileDataSummary>(Encoding.UTF8.GetString(data))!;
}

// Make Manager Interfaces for abstraction between GPose and file mode.
public class ModularActorBase
{
    // maybe reference back to the manager here to perform internal actions or something.
    public ModularActorData Parent { get; } // Every base is connected to a SMAD container.
    public Guid ID { get; }
    public string Name { get; }
    public string Description { get; }

    public JObject GlamourState { get; }
    public string CPlusData { get; }
    public string ModManipString { get; }

    // Calculated dictionary from Files + FileSwaps
    public Dictionary<string, string> ModdedDict { get; set; } = [];

    public ModularActorBase(BaseFileDataSummary summary)
    {
        ID = summary.FileId;
        Name = summary.Name;
        Description = summary.Description;
        GlamourState = summary.GlamourState;
        CPlusData = summary.CPlusData;
        ModManipString = summary.ModManips;
    }

    public bool IsValid => ID == Guid.Empty;
}

internal sealed class OwnedModularActorBase : ModularActorBase
{
    public SMABaseFileMeta FileMeta { get; set; }
    public OwnedModularActorBase(SMABaseFileMeta fileMeta, BaseFileDataSummary summary)
        : base(summary)
    {
        FileMeta = fileMeta;
    }
}

public class ModularActorOutfit : IDisposable // Does not reference to its base, could be a part of multiple SMAD's.
{
    private string _thumbnailBase64;
    private Lazy<byte[]> _imageData;
    private IDalamudTextureWrap? _storedImage;

    // maybe reference back to the manager here to perform internal actions or something.
    public Guid ID { get; }
    public string Name { get; }
    public string Description { get; }

    public JObject GlamourState { get; }
    public string CPlusData { get; }
    public string ModManipString { get; }

    // Calculated dictionary from Files + FileSwaps
    public Dictionary<string, string> ModdedDict { get; set; } = [];

    public ModularActorOutfit(OutfitFileDataSummary summary)
    {
        ID = summary.FileId;
        Name = summary.Name;
        Description = summary.Description;
        ThumbnailBase64 = summary.ThumbnailBase64;
        GlamourState = summary.GlamourState; 
        ModManipString = summary.ModManips;

        _imageData = new Lazy<byte[]>(() => ThumbnailBase64.Length > 0
            ? Convert.FromBase64String(ThumbnailBase64) : Array.Empty<byte>());

        // Maybe a subscriber to cleanup the image data or something, otherwise clears on disposal.
    }

    public bool IsValid => ID == Guid.Empty;

    public string ThumbnailBase64
    {
        get => _thumbnailBase64;
        set
        {
            if (_thumbnailBase64 != value)
            {
                _thumbnailBase64 = value;
                if (!string.IsNullOrEmpty(_thumbnailBase64))
                    _imageData = new Lazy<byte[]>(() => ToByteArr(ThumbnailBase64));
            }
        }
    }

    public void Dispose()
    {
        _storedImage?.Dispose();
        _storedImage = null;
    }

    private byte[] ToByteArr(string base64String)
    {
        if (string.IsNullOrEmpty(base64String))
            return Array.Empty<byte>();
        try
        {
            return Convert.FromBase64String(base64String);
        }
        catch (FormatException ex)
        {
            Svc.Logger.Error($"Invalid Base64 string for image: {ex}");
            return Array.Empty<byte>();
        }
    }
}

internal sealed class OwnedModularActorOutfit : ModularActorOutfit
{
    public SMAFileMeta FileMeta { get; set; }
    public OwnedModularActorOutfit(SMAFileMeta fileMeta, OutfitFileDataSummary summary)
        : base(summary)
    {
        FileMeta = fileMeta;
    }
}

public class ModularActorItem : IDisposable
{
    private string _base64ThumbnailString;
    private Lazy<byte[]> _imageData;
    private IDalamudTextureWrap? _storedImage;

    public Guid ID { get; }
    public string Name { get; }
    public string Description { get; }

    public JObject GlamourState { get; }
    public string ModManipString { get; }

    // Calculated dictionary from Files + FileSwaps
    public Dictionary<string, string> ModdedDict { get; set; } = [];

    public ModularActorItem(ItemFileDataSummary summary)
    {
        ID = summary.FileId;
        Name = summary.Name;
        Description = summary.Description;
        _base64ThumbnailString = summary.ThumbnailBase64;
        GlamourState = summary.GlamourState;
        ModManipString = summary.ModManips;
        try
        {
            _imageData = string.IsNullOrEmpty(_base64ThumbnailString)
                ? new Lazy<byte[]>(Array.Empty<byte>)
                : new Lazy<byte[]>(Convert.FromBase64String(_base64ThumbnailString));
        }
        catch (FormatException ex)
        {
            Svc.Logger.Error($"Invalid Base64 string for image: {ex}");
            _imageData = new Lazy<byte[]>(Array.Empty<byte>);
        }
    }

    public bool IsValid => ID == Guid.Empty;

    public void Dispose()
    {
        _storedImage?.Dispose();
        _storedImage = null;
    }
}

public sealed class OwnedModularActorItem : ModularActorItem
{
    public SMAFileMeta FileMeta { get; set; }
    public OwnedModularActorItem(SMAFileMeta fileMeta, ItemFileDataSummary summary)
        : base(summary)
    {
        FileMeta = fileMeta;
    }
}

public static class SMAExtensions
{
    //public static FileDataSummary CreateBase(FileCacheManager manager, OwnedObject actorKind, ModdedState state, string desc)
    //{
    //    var retValue = new FileDataSummary();

    //    Description = desc;
    //    // As of right now we pull from the DistributionService's last cached client data to retrieve this information.
    //    // However, we should probably handle this differently down the line.

    //    // Assign GlamourerData, preferably update this in the future or something.
    //    if (DistributionService.LastCreatedData.GlamourerState.TryGetValue(actorKind, out var glamourerData))
    //        GlamourerData = glamourerData;

    //    if (DistributionService.LastCreatedData.CPlusState.TryGetValue(actorKind, out var cplusData))
    //        CPlusData = cplusData;

    //    // Mark the manipulation data string.
    //    ModManipulationData = DistributionService.LastCreatedData.ModManips;

    //    // Iterate over the modded state, if any files are present for the object.
    //    if (!state.FilesByObject.TryGetValue(actorKind, out var moddedFiles))
    //        return;

    //    // Group the files by their hash.
    //    var grouped = moddedFiles.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase);
    //    foreach (var file in grouped)
    //    {
    //        // If there is no key, it is a file swap, so add it as a swap.
    //        // However, do not add files that are body or leg models if present.
    //        if (string.IsNullOrEmpty(file.Key))
    //        {
    //            foreach (var item in file)
    //            {
    //                if (item.GamePaths.Any(IsBodyLegModel))
    //                    continue;
    //                // Otherwise, add it
    //                FileSwaps.Add(new FileSwap(item.GamePaths, item.ResolvedPath));
    //            }
    //        }
    //        // Otherwise it could be a modded file.
    //        else
    //        {
    //            // If it is a valid modded file, add it to the file data.
    //            if (manager.GetFileCacheByHash(file.First().Hash)?.ResolvedFilepath is { } validFile)
    //            {
    //                // Do not add if a body/leg model and requested.
    //                if (noBodyLegs && file.Any(f => f.GamePaths.Any(IsBodyLegModel)))
    //                    continue;
    //                // Otherwise, add it.
    //                Files.Add(new FileModData(file.SelectMany(f => f.GamePaths), (int)new FileInfo(validFile).Length, file.First().Hash));
    //            }
    //        }
    //    }
    //}

    private static bool IsBodyLegModel(string gp)
        => gp.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) &&
        (gp.Contains("/body/", StringComparison.OrdinalIgnoreCase) || gp.Contains("/legs/", StringComparison.OrdinalIgnoreCase));
}
