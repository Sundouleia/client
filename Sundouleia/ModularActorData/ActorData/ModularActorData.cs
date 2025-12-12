namespace Sundouleia.ModularActorData;

/// <summary>
///     The combination of an imported <see cref="ActorBaseData"/>, with all
///     outfits and items allowed in <see cref="ActorBaseData.ValidHashes"/>
/// </summary>
public sealed class ModularActorData(ActorBaseData ActorBase)
{
    // Remove later maybe.
    private string _importedPassword = string.Empty;

    // Stored fileData imported by each actor kind.
    private Dictionary<Guid, ActorOutfitData> _importedOutfits = new();
    private Dictionary<Guid, ActorItemData> _importedItems = new();

    // The outfit selected to apply to this base, from the ones currently selected.
    private ActorOutfitData? _currentOutfit;
    private List<ActorItemData> _currentItems = new();

    public string Description => ActorBase.Description;
    // Needs some finalized composite data. (such as composite glamourer settings ext)

    // Helper methods here for setting spesifics.

}

// Placeholders.
public class ActorOutfitData
{
    public readonly Guid Id;
    public ActorOutfitData(ActorOutfitFileData fileData)
    {
        Id = fileData.Id;
    }
}

public record ActorOutfitFileData(Guid Id, string FileDataHash);

public class ActorItemData
{
    public readonly Guid Id;
    public ActorItemData(ActorItemFileData fileData)
    {
        Id = fileData.Id;
    }
}

public record ActorItemFileData(Guid Id, string FileDataHash);