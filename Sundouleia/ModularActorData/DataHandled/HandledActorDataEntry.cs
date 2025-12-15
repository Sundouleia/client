using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sundouleia.ModularActor;

// Representation of an actor that is currently rendered in GPose.
public unsafe sealed class HandledActorDataEntry(string nameString, GameObject* gPoseObject, ModularActorData data)
{
    public bool IsValid => gPoseObject != null;

    public string ActorName => nameString;
    public ModularActorData Data => data;
    public GameObject* GPoseObject => gPoseObject;

    public string CollectionName => $"SMA_{Data.BaseId}";
    public IntPtr ObjectAddress => (IntPtr)gPoseObject;
    public ushort ObjectIndex => gPoseObject->ObjectIndex;

    // Helpers and public setters.
    public string DisplayName { get; set; } = nameString ?? "Unknown";
    public Guid CollectionId { get; set; } = Guid.Empty;
    public Guid? CPlusId { get; set; }

    public string GetTempModBaseName()                => $"SMA_TempModBase_{Data.BaseId}";
    public string GetTempModOutfitName(Guid outfitId) => $"SMA_TempOutfit_{Data.BaseId}_{outfitId}";
    public string GetTempModItemName(Guid itemId)     => $"SMA_TempItem_{Data.BaseId}_{itemId}";
}
