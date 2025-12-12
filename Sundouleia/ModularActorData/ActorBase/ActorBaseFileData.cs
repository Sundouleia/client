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

namespace Sundouleia.ModularActorData;

// This data is only used to reflect what goes into SundouleiaModularActor files!
// As such as can know when it is valid to throw an exception and when not to.
public record ActorBaseFileData
{
    public string Description { get; set; } = string.Empty;
    // For now use glamourer data, later we can make glamourer data store a different version of the base64 string
    // to compose the modular approach.
    public string GlamourerData { get; set; } = string.Empty;

    // The CustomizePlus data applied.
    public string CPlusData { get; set; } = string.Empty;

    // Note that when allowing layering with this multiple mods must be added to the list.
    public string ModManipulationData { get; set; } = string.Empty;

    public List<FileModData> Files { get; set; } = []; // Contains modded resources.
    public List<FileSwap> FileSwaps { get; set; } = []; // Don't contain modded resources, vanilla swap.

    public ActorBaseFileData()
    { }

    public ActorBaseFileData(FileCacheManager manager, OwnedObject actorKind, ModdedState state, string desc, bool noBodyLegs = true)
    {
        Description = desc;
        // As of right now we pull from the DistributionService's last cached client data to retrieve this information.
        // However, we should probably handle this differently down the line.

        // Assign GlamourerData, preferably update this in the future or something.
        if (DistributionService.LastCreatedData.GlamourerState.TryGetValue(actorKind, out var glamourerData))
            GlamourerData = glamourerData;

        if (DistributionService.LastCreatedData.CPlusState.TryGetValue(actorKind, out var cplusData))
            CPlusData = cplusData;

        // Mark the manipulation data string.
        ModManipulationData = DistributionService.LastCreatedData.ModManips;

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