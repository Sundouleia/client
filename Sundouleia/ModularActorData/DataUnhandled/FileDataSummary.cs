using CkCommons;
using Dalamud.Interface.Textures.TextureWraps;
using Microsoft.IdentityModel.Tokens;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Textures;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;

public abstract class FileDataSummary
{
    public byte Version { get; init; }
    public Guid FileId { get; init; }
    public OwnedObject ActorKind { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageStr { get; set; } = string.Empty; // Not used in base.

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
        var headerData = ToByteArray();
        writer.Write(headerData.Length);
        writer.Write(headerData);
    }

    public byte[] ToByteArray()
        => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
}

// Everything collected together.
public class SmadFileDataSummary
{
    internal static readonly string Magic = "SMAD";
    internal static readonly byte CurrentVersion = 1;

    public byte Version { get; init; }
    public Guid FileId { get; init; }
    public OwnedObject ActorKind { get; set; }
    public string Name { get; set; }
    public string Description { get; set; } = string.Empty;

    public BaseFileDataSummary Base { get; set; } = new();
    public List<OutfitFileDataSummary> Outfits { get; set; } = [];
    public List<ItemFileDataSummary> Items { get; set; } = [];

    public Guid SelectedOutfit { get; set; } = Guid.Empty;
    public HashSet<Guid> SelectedItems { get; set; } = [];

    public void WriteHeader(BinaryWriter writer)
    {
        // write the magic & version.
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(CurrentVersion);

        // write global ActorKind
        writer.Write((byte)ActorKind);

        // write Base payload
        var baseBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Base));
        writer.Write(baseBytes.Length);
        writer.Write(baseBytes);

        // write Outfits payload
        writer.Write(Outfits.Count);
        foreach (var outfit in Outfits)
        {
            var outfitBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(outfit));
            writer.Write(outfitBytes.Length);
            writer.Write(outfitBytes);
        }

        // write Items payload
        writer.Write(Items.Count);
        foreach (var item in Items)
        {
            var itemBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(item));
            writer.Write(itemBytes.Length);
            writer.Write(itemBytes);
        }

        // write pre-defined selections
        writer.Write(SelectedOutfit.ToByteArray());
        writer.Write(SelectedItems.Count);
        foreach (var guid in SelectedItems)
            writer.Write(guid.ToByteArray());
    }

    public static SmadFileDataSummary FromHeader(BinaryReader reader, string filePath)
    {
        var magic = new string(reader.ReadChars(Magic.Length));
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            throw new InvalidDataException($"Bad Magic! Expected {Magic}, got {magic}");

        var version = reader.ReadByte();
        // If versions are different, return the loaded migration data.
        if (version != CurrentVersion)
            return FromOldHeader(reader, filePath, version);

        // Otherwise, it is on the right version, so read in expected data.
        var actorKind = (OwnedObject)reader.ReadByte();

        // Read Base payload
        var baseLen = reader.ReadInt32();
        var baseSummary = JsonConvert.DeserializeObject<BaseFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(baseLen)))!;

        // Read Outfits payload
        var outfitCount = reader.ReadInt32();
        var outfits = new List<OutfitFileDataSummary>();
        for (int i = 0; i < outfitCount; i++)
        {
            var outfitLen = reader.ReadInt32();
            var outfitSummary = JsonConvert.DeserializeObject<OutfitFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(outfitLen)))!;
            outfits.Add(outfitSummary);
        }

        // Read Items payload
        var itemCount = reader.ReadInt32();
        var items = new List<ItemFileDataSummary>();
        for (int i = 0; i < itemCount; i++)
        {
            var itemLen = reader.ReadInt32();
            var itemSummary = JsonConvert.DeserializeObject<ItemFileDataSummary>(Encoding.UTF8.GetString(reader.ReadBytes(itemLen)))!;
            items.Add(itemSummary);
        }

        // Read pre-defined selections
        var selectedOutfit = new Guid(reader.ReadBytes(16));
        var selectedItemCount = reader.ReadInt32();
        var selectedItems = new HashSet<Guid>();
        for (int i = 0; i < selectedItemCount; i++)
        {
            var itemGuid = new Guid(reader.ReadBytes(16));
            selectedItems.Add(itemGuid);
        }

        // Ret the complete summary.
        return new SmadFileDataSummary
        {
            Version = version,
            ActorKind = actorKind,
            Base = baseSummary,
            Outfits = outfits,
            Items = items,
            SelectedOutfit = selectedOutfit,
            SelectedItems = selectedItems
        };
    }

    public static SmadFileDataSummary FromOldHeader(BinaryReader reader, string filePath, byte readVersion)
        => throw new NotImplementedException();
}

public class BaseFileDataSummary : FileDataSummary
{
    internal static readonly string Magic = "SMAB";
    internal static readonly byte CurrentVersion = 1;

    // What is allowed to be used with the Base. If empty, allow anything.
    public HashSet<string> AllowedHashes { get; set; } = [];

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

    public static void MoveReaderToContents(BinaryReader reader)
    {
        reader.ReadChars(Magic.Length); // Magic
        reader.ReadByte(); // Version
        var summaryLen = reader.ReadInt32();
        _ = reader.ReadBytes(summaryLen);
    }
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