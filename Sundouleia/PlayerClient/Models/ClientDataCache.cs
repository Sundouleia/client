using MessagePack;
using SundouleiaAPI.Data;
using SundouleiaAPI.Enums;

namespace Sundouleia.PlayerClient;

/// <summary>
///     Holds a cache of the client's current data for comparison purposes. <para />
///     This is not used over the network and designed for efficient lookups & updates.
/// </summary>
public class ClientDataCache
{
    // Key'd by mod hash.
    public Dictionary<string, ModFileData> AppliedMods { get; set; } = new();

    public Dictionary<OwnedObject, string> GlamourerState { get; set; } = [];
    public Dictionary<OwnedObject, string> CPlusState { get; set; } = [];
    
    public string ModManips     { get; set; } = string.Empty;
    public string HeelsOffset   { get; set; } = string.Empty;
    public string TitleData     { get; set; } = string.Empty;
    public string Moodles       { get; set; } = string.Empty;
    public string PetNames      { get; set; } = string.Empty;

    // True when an update has been applied since the last change.
    public bool IsCacheDirty    { get; set; } = false;

    public ClientDataCache()
    {
        // Ensure default keys for all owned objects.
        GlamourerState = new Dictionary<OwnedObject, string>
        {
            [OwnedObject.Player] = string.Empty,
            [OwnedObject.MinionOrMount] = string.Empty,
            [OwnedObject.Companion] = string.Empty,
            [OwnedObject.Pet] = string.Empty
        };
        CPlusState = new Dictionary<OwnedObject, string>
        {
            [OwnedObject.Player] = string.Empty,
            [OwnedObject.MinionOrMount] = string.Empty,
            [OwnedObject.Companion] = string.Empty,
            [OwnedObject.Pet] = string.Empty
        };
    }
}