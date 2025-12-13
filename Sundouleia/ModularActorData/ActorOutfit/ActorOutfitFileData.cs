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

[Flags]
public enum SMAOSlotFilter : short
{
    MainHand = 0 << 0,
    OffHand  = 1 << 0,
    Head     = 1 << 1,
    Body     = 1 << 2,
    Hands    = 1 << 3,
    Legs     = 1 << 4,
    Feet     = 1 << 5,
    Ears     = 1 << 6,
    Neck     = 1 << 7,
    Wrists   = 1 << 8,
    RFinger  = 1 << 9,
    LFinger  = 1 << 10,
    Bonus    = 1 << 11,
}

[Flags]
public enum SMAOMetaFilter : byte
{
    None      = 0 << 0,
    Hat       = 1 << 0,
    VieraEars = 1 << 1,
    Visor     = 1 << 2,
    Weapon    = 1 << 3,
}

// Maybe add the filters in here, maybe not.
public record SMAOMeta(string Name, string Description, string? ThumbnailBase64);


// An ActorOutfit file depends on being applied to an associated ActorBase.
// This data can limit what parts of it are applied, helping with layering.
//
// By Default, simply layering moddedDict's in the main ModularActorData will be fine, but glamourer needs stacking.
//
// Additionally we may need to handle merging metadata strings, but for now ignore this.
// 
// Parsing the modded files for selected slots will be a lot easier with our requested penumbraAPI calls, but we need to wait for those.
public record ActorOutfitFileData
{
    public string OutfitName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Base64Thumbnail { get; set; } = string.Empty; // Optional, obviously.

    // Filters to limit what is stored.
    public SMAOSlotFilter SlotFilter { get; set; } = SMAOSlotFilter.MainHand;
    public SMAOMetaFilter MetaFilter { get; set; } = SMAOMetaFilter.None;
    public bool Customizations       { get; set; } = false;
    public bool AdvCustomizations    { get; set; } = false;

    public string GlamourerData { get; set; } = string.Empty; // Base64 Glamourer Data.



    public string ManipString { get; set; } = string.Empty;

    // The files & FileSwaps to apply for the outfit.
    public List<FileModData> Files { get; set; } = []; // Contains modded resources.
    public List<FileSwap> FileSwaps { get; set; } = []; // Don't contain modded resources, vanilla swap.

    public ActorOutfitFileData()
    { }

    public ActorOutfitFileData(FileCacheManager manager, OwnedObject actorKind, ModdedState state, string desc, bool noBodyLegs = true)
    {
        Description = desc;
        // As of right now we pull from the DistributionService's last cached client data to retrieve this information.
        // However, we should probably handle this differently down the line.

        // Assign GlamourerData, preferably update this in the future or something.
        if (DistributionService.LastCreatedData.GlamourerState.TryGetValue(actorKind, out var glamourerData))
            GlamourerData = glamourerData;

        // Mark the manipulation data string.
        ManipString = DistributionService.LastCreatedData.ModManips;

        // Iterate over the modded state, if any files are present for the object.
        if (!state.FilesByObject.TryGetValue(actorKind, out var moddedFiles))
            return;

        // Group the files by their hash.
        var grouped = moddedFiles.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase);
        foreach (var file in grouped)
        {
            // If there is no key, it is a file swap, so add it as a swap.
            // However, do not add files that are body or leg models if present.
            if (string.IsNullOrEmpty(file.Key))
            {
                foreach (var item in file)
                {
                    if (noBodyLegs && item.GamePaths.Any(IsBodyLegModel))
                        continue;
                    // Otherwise, add it
                    FileSwaps.Add(new FileSwap(item.GamePaths, item.ResolvedPath));
                }
            }
            // Otherwise it could be a modded file.
            else
            {
                // If it is a valid modded file, add it to the file data.
                if (manager.GetFileCacheByHash(file.First().Hash)?.ResolvedFilepath is { } validFile)
                {
                    // Do not add if a body/leg model and requested.
                    if (noBodyLegs && file.Any(f => f.GamePaths.Any(IsBodyLegModel)))
                        continue;
                    // Otherwise, add it.
                    Files.Add(new FileModData(file.SelectMany(f => f.GamePaths), (int)new FileInfo(validFile).Length, file.First().Hash));
                }
            }
        }
    }

    // File filtering will be a lot better when we can get direct filtering from a resourceTree.
    private bool IsBodyLegModel(string gp)
        => gp.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) && 
        (gp.Contains("/body/", StringComparison.OrdinalIgnoreCase) || gp.Contains("/legs/", StringComparison.OrdinalIgnoreCase));

    // Convert this data into a byte array of information.
    public byte[] ToByteArray() 
        => Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(this));

    // Convert a byte array of data back into a ModularActorBaseFileData object.
    public static ActorBaseFileData FromByteArray(byte[] data)
        => System.Text.Json.JsonSerializer.Deserialize<ActorBaseFileData>(Encoding.UTF8.GetString(data))!;
}