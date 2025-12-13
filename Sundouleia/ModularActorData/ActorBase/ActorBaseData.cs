namespace Sundouleia.ModularActor;

/// <summary>
///     Post-Processed <see cref="ActorBaseFileData"/> record for caching.
/// </summary>
public record ActorBaseData
{
    private readonly SmabHeader _header;
    public ActorBaseData(SmabHeader header, ActorBaseFileData baseData, Dictionary<string, string> moddedDict)
    {
        _header = header;
        Description = baseData.Description;
        GlamourData = baseData.GlamourerData;
        CPlusData = baseData.CPlusData;
        ModManips = baseData.ModManipulationData;
        ModdedDict = moddedDict;
    }

    public Guid   BaseId      => _header.Id;
    public string Description { get; private set; } = string.Empty;
    public string GlamourData { get; private set; } = string.Empty;
    public string CPlusData   { get; private set; } = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")); // Default empty JSON.
    public string ModManips   { get; private set; } = string.Empty;
    
    /// <summary>
    ///     The FileHashes that are allowed to be applied to the modular actor holding this base file. <para />
    ///     Can only be changed by someone using a valid update token json.
    /// </summary>
    public IReadOnlyCollection<string> ValidHashes => _header.Hashes;

    /// <summary>
    ///     Mapping of GamePath -> Replacement Path. (Already organized for modded files and swaps)
    /// </summary>
    public Dictionary<string, string> ModdedDict { get; set; } = [];

    // Maybe some other helper methods that can be included later or something.

    public bool TrySetAllowedHashes(IEnumerable<string> hashes)
    {
        return false;
    }

    // WIP Update tokens.
    public bool TryAddAllowedHashes(IEnumerable<string> hashes)
    {
        return false;
    }
}