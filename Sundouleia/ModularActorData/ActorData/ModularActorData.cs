namespace Sundouleia.ModularActor;

/// <summary>
///     The combination of an imported <see cref="ActorBaseData"/>, with all
///     outfits and items allowed in <see cref="ActorBaseData.ValidHashes"/>
/// </summary>
public sealed class ModularActorData(ActorBaseData ActorBase)
{
    // Stored fileData imported by each actor kind.
    private Dictionary<Guid, ActorOutfitFileData> _importedOutfits = new();
    private Dictionary<Guid, ActorItemData> _importedItems = new();

    // The outfit selected to apply to this base, from the ones currently selected.
    private ActorOutfitFileData? _currentOutfit;
    private List<ActorItemData> _currentItems = new();

    public Guid   BaseId      => ActorBase.BaseId;
    public string Description => ActorBase.Description;

    // Needs some finalized composite data. (such as composite glamourer settings ext)
    public Dictionary<string, string> FinalModdedDict => ActorBase.ModdedDict;
    public string CompositeManips => ActorBase.ModManips; // Remove this later
    public string FinalGlamourData => ActorBase.GlamourData; // Convert to merged JObject format later.
    public string CPlusData => ActorBase.CPlusData; // Fine As-Is.
}

public class ActorItemData
{
    public readonly Guid Id;
    public ActorItemData(ActorItemFileData fileData)
    {
        Id = fileData.Id;
    }
}

public record ActorItemFileData(Guid Id, string FileDataHash);