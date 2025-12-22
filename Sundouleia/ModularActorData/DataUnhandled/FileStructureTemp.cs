using Dalamud.Interface.Textures.TextureWraps;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;

namespace Sundouleia.ModularActor;

public abstract class FileDataSummary
{
    public byte Version { get; init; }
    public Guid FileId { get; init; }
    public OwnedObject ActorKind { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ThumbnailBase64 { get; set; } = string.Empty; // Not used in base.

    public JObject GlamourState { get; set; } = new();

    public string ModManips { get; set; } = string.Empty;
    public List<FileModData> Files { get; set; } = [];
    public List<FileSwap> FileSwaps { get; set; } = [];

    protected abstract string GetMagic();
    protected abstract byte GetVersion();

    public void WriteHeader(BinaryWriter writer)
    {
        writer.Write(Encoding.ASCII.GetBytes(GetMagic()));
        writer.Write(GetVersion());
        var headerData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
        writer.Write(headerData.Length);
        writer.Write(headerData);
    }
}

public class BaseFileDataSummary : FileDataSummary
{
    internal static readonly string Magic = "SMAB";
    internal static readonly byte CurrentVersion = 1;

    public string CPlusData { get; set; } = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")); // Default empty JSON.

    protected override string GetMagic() => Magic;
    protected override byte GetVersion() => CurrentVersion;

    public static BaseFileDataSummary FromHeader(BinaryReader reader, string filePath)
    {
        var magic = new string(reader.ReadChars(Magic.Length));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            throw new InvalidDataException($"Bad Magic! Expected {Magic}, got {magic}");

        var version = reader.ReadByte();
        // If versions are different, return the loaded migration data.
        if (version != CurrentVersion)
            return FromOldHeader(reader, filePath, version);

        // Otherwise, it is on the right version, so read in expected data.
        var summaryLen = reader.ReadInt32();
        return JsonConvert.DeserializeObject<BaseFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(summaryLen)))!;
    }

    private static BaseFileDataSummary FromOldHeader(BinaryReader reader, string filePath, byte readVersion)
        => throw new NotImplementedException();
}

public class OutfitFileDataSummary : FileDataSummary
{
    internal static readonly string Magic = "SMAO";
    internal static readonly byte CurrentVersion = 1;

    public SMAGlamourParts PartsFilter { get; set; } = SMAGlamourParts.None;
    public SMAFileSlotFilter SlotFilter { get; set; } = SMAFileSlotFilter.MainHand;
    public SMAFileMetaFilter MetaFilter { get; set; } = SMAFileMetaFilter.None;

    // Optional: trimmed snapshot of GlamourState with filters? (Or maybe just keep full and trim on save?)
    // ((OR just keep the trimmed only and do a recalculation each time.))

    protected override string GetMagic() => Magic;
    protected override byte GetVersion() => CurrentVersion;

    public static OutfitFileDataSummary FromHeader(BinaryReader reader, string filePath)
    {
        var magic = new string(reader.ReadChars(Magic.Length));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            throw new InvalidDataException($"Bad Magic! Expected {Magic}, got {magic}");

        var version = reader.ReadByte();
        // If versions are different, return the loaded migration data.
        if (version != CurrentVersion)
            return FromOldHeader(reader, filePath, version);

        // Otherwise, it is on the right version, so read in expected data.
        var summaryLen = reader.ReadInt32();
        return JsonConvert.DeserializeObject<OutfitFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(summaryLen)))!;
    }

    private static OutfitFileDataSummary FromOldHeader(BinaryReader reader, string filePath, byte readVersion)
        => throw new NotImplementedException();
}

public class ItemFileDataSummary : FileDataSummary
{
    internal static readonly string Magic = "SMAI";
    internal static readonly byte CurrentVersion = 1;

    protected override string GetMagic() => Magic;
    protected override byte GetVersion() => CurrentVersion;

    public static ItemFileDataSummary FromHeader(BinaryReader reader, string filePath)
    {
        var magic = new string(reader.ReadChars(Magic.Length));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            throw new InvalidDataException($"Bad Magic! Expected {Magic}, got {magic}");

        var version = reader.ReadByte();
        // If versions are different, return the loaded migration data.
        if (version != CurrentVersion)
            return FromOldHeader(reader, filePath, version);

        // Otherwise, it is on the right version, so read in expected data.
        var summaryLen = reader.ReadInt32();
        return JsonConvert.DeserializeObject<ItemFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(summaryLen)))!;
    }

    private static ItemFileDataSummary FromOldHeader(BinaryReader reader, string filePath, byte readVersion)
        => throw new NotImplementedException();
}

// Make Manager Interfaces for abstraction between GPose and file mode.
public class ModularActorBase
{
    // maybe reference back to the manager here to perform internal actions or something.
    public ModularActorData Parent { get; internal set; } // Every base is connected to a SMAD container.
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

    public ModularActorBase(BaseFileDataSummary summary, Dictionary<string, string> modDict)
        : this(summary)
    {
        ModdedDict = modDict;
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
}
