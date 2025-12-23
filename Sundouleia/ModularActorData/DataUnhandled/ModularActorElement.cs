using CkCommons;
using Dalamud.Interface.Textures.TextureWraps;
using Microsoft.IdentityModel.Tokens;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Textures;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;

// Holds essential information shared between all elements of SMA containers.
public abstract class ModularActorElement
{
    /// <summary>
    ///     Where Sundouleia expects this file to be located.
    ///     If there is a missmatch, loading data will fail.
    /// </summary>
    public abstract string ExpectedFilePath { get; }

    /// <summary>
    ///     The SHA256 FileHash of the data. If empty, file was not found.
    /// </summary>
    public abstract string FileHash { get; }

    /// <summary>
    ///     The Identifier of this SMA element.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    ///     What owned object this SMA element is for.
    /// </summary>
    public OwnedObject ActorKind { get; protected set; } = OwnedObject.Player;

    /// <summary>
    ///     Typically the FileName, if renamed, the new file should reflect this.
    /// </summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>
    ///     Details about the SMA element, if provided.
    /// </summary>
    public string Description { get; protected set; } = string.Empty;

    /// <summary>
    ///     The Base64 Penumbra ModManip string to apply.
    /// </summary>
    public abstract string ManipString { get; }

    /// <summary>
    ///     Dictionary of GamePaths -> Replacement Paths. <para />
    ///     This should be updated any time settings from other data are changed.
    ///     (Update this overtime?)
    /// </summary>
    public abstract Dictionary<string, string> FileReplacements { get; }

    /// <summary>
    ///     The JObject glamourState to apply when requested.
    /// </summary>
    public abstract JObject GlamourState { get; }

    /// <summary>
    ///     Method to obtain a elements thumbnail image. <para />
    ///     Throws an exception when called on elements that do not have thumbnails. <para />
    ///     (This may cause issues due to calling an interface with a virtual potentially leading
    ///     to poor performance so if you notice it spike, that's why)
    /// </summary>
    public virtual IDalamudTextureWrap GetImageOrDefault() 
        => throw new NotImplementedException();

    /// <summary>
    ///     Determines if a file is valid for re-saving, or saving in general.
    /// </summary>
    public abstract bool ValidForSaving();

    /// <summary>
    ///     If the contents of a file are currently valid to the point where an
    ///     existing file can be updated with the latest data.
    /// </summary>
    public abstract bool ValidForUpdateSave();
}

public class ModularActorData : ModularActorElement
{
    /// <summary>
    ///     Used by Owned ModularActorData for storing file metadata.
    /// </summary>
    internal SMABaseFileMeta? FileMeta = null;

    // Every SMA Data class must hold a respective Base.
    public ModularActorBase         Base            { get; private set; } = null!;
    public List<ModularActorOutfit> Outfits         { get; private set; } = new(); // Maybe hashset to avoid conflicts
    public List<ModularActorItem>   Items           { get; private set; } = new(); // Maybe hashset to avoid conflicts
    public ModularActorOutfit?      CurrentOutfit   { get; private set; } = null;
    public List<ModularActorItem>   CurrentItems    { get; private set; } = [];

    // Freshly created ModularActorData objects.
    public ModularActorData()
    {
        Id = Guid.NewGuid();
        ActorKind = OwnedObject.Player;
        Name = "[New SMAData]";
        Description = string.Empty;
    }

    public ModularActorData(ModularActorBase actorBase)
    {
        Id = Guid.NewGuid();
        ActorKind = actorBase.ActorKind;
        Name = $"[Parent] {actorBase.Name}";
        Description = $"[Parent] {actorBase.Description}";
        Base = actorBase;
    }

    public ModularActorData(SmadFileDataSummary summary)
    {
        Id = summary.FileId;
        ActorKind = summary.ActorKind;
        Name = summary.Name;
        Description = summary.Description;
        Base = new ModularActorBase(summary.Base);
        // Link back to this data.
        Base.Parent = this;
        // Load in all outfits.
        foreach (var outfitSummary in summary.Outfits)
            Outfits.Add(new ModularActorOutfit(outfitSummary));

        // Load in all items.
        foreach (var itemSummary in summary.Items)
            Items.Add(new ModularActorItem(itemSummary));

        // Set the current outfit if it exists.
        if (Outfits.FirstOrDefault(o => o.Id == summary.SelectedOutfit) is { } match)
            CurrentOutfit = match;

        // Iterate the Items, and if found in the list, append them.
        foreach (var item in Items)
        {
            if (summary.SelectedItems.Contains(item.Id))
                CurrentItems.Add(item);
        }
    }

    public ModularActorData(SmadFileDataSummary summary, SMABaseFileMeta meta)
        : this(summary)
    {
        FileMeta = meta;
    }

    public override string ExpectedFilePath => FileMeta?.FilePath ?? string.Empty;
    public override string FileHash => FileMeta?.DataHash ?? string.Empty;

    public override Dictionary<string, string> FileReplacements => GetReplacements();
    public override string  ManipString  => GetManipString();
    public override JObject GlamourState => GetCompiledState();
    public string           CPlusData    => Base?.CPlusData ?? string.Empty;

    private Dictionary<string, string> GetReplacements()
    {
        // Expand on this later!
        return Base?.FileReplacements ?? new Dictionary<string, string>();
    }

    private string GetManipString()
        => Base?.ManipString ?? string.Empty;

    private JObject GetCompiledState()
    {
        // Run a compilation over the current base, selected outfit, and items to get the final glamour data.
        return Base?.GlamourState ?? new JObject();
    }

    public override bool ValidForSaving()
        => Id != Guid.Empty && Base is not null && Base.ValidForSaving()
        && CurrentOutfit is not null && CurrentOutfit.ValidForSaving();

    public override bool ValidForUpdateSave() => ValidForSaving();
}

public class ModularActorBase : ModularActorElement
{
    // Self-Reference to associated parent data. (Can remove if we dont need)
    internal ModularActorData?  Parent;
    internal SMABaseFileMeta?   FileMeta;

    private Dictionary<string, string> _replacements = [];
    private string _manipString;
    private JObject _glamourState;

    public ModularActorBase(BaseFileDataSummary summary)
    {
        Id = summary.FileId;
        ActorKind = summary.ActorKind;
        Name = summary.Name;
        Description = summary.Description;
        AllowedHashes = summary.AllowedHashes;
        _manipString = summary.ModManips;
        _glamourState = summary.GlamourState;
        CPlusData = summary.CPlusData;
    }

    public ModularActorBase(BaseFileDataSummary summary, Dictionary<string, string> modDict)
        : this(summary) => _replacements = modDict;

    public ModularActorBase(BaseFileDataSummary summary, SMABaseFileMeta meta)
        : this(summary) => FileMeta = meta;

    public ModularActorBase(BaseFileDataSummary summary, SMABaseFileMeta meta, Dictionary<string, string> modDict)
        : this(summary, modDict) => FileMeta = meta;

    public override string ExpectedFilePath => FileMeta?.FilePath ?? string.Empty;
    public override string FileHash => FileMeta?.DataHash ?? string.Empty;

    public HashSet<string> AllowedHashes { get; } = [];
    public string CPlusData { get; private set; }
    public override Dictionary<string, string> FileReplacements => _replacements;
    public override string ManipString => _manipString;
    public override JObject GlamourState => _glamourState;

    public override bool ValidForSaving()
        => Id != Guid.Empty && _replacements.Count > 0 
        && string.IsNullOrEmpty(_manipString) && _glamourState.HasValues;

    public override bool ValidForUpdateSave()
        => ValidForSaving();
}

public class ModularActorOutfit : ModularActorElement, IDisposable // Does not reference to its base, could be a part of multiple SMAD's.
{
    internal SMAFileMeta? FileMeta;

    private Dictionary<string, string> _replacements = new();
    private string _manipString;
    private JObject _glamourState;
    private Lazy<byte[]> _imageData;
    private IDalamudTextureWrap? _storedImage;

    public ModularActorOutfit(OutfitFileDataSummary summary)
    {
        Id = summary.FileId;
        Name = summary.Name;
        Description = summary.Description;
        ImageStr = summary.ImageStr;
        _imageData = new Lazy<byte[]>(() => ImageStr.Length > 0
            ? Convert.FromBase64String(ImageStr) : Array.Empty<byte>());
        _glamourState = summary.GlamourState;
        _manipString = summary.ModManips;
    }

    public ModularActorOutfit(OutfitFileDataSummary summary, Dictionary<string, string> modDict)
        : this(summary) => _replacements = modDict;

    public ModularActorOutfit(OutfitFileDataSummary summary, SMAFileMeta meta)
        : this(summary) => FileMeta = meta;

    public ModularActorOutfit(OutfitFileDataSummary summary, SMAFileMeta meta, Dictionary<string, string> modDict)
        : this(summary, modDict) => FileMeta = meta;

    public override string ExpectedFilePath => FileMeta?.FilePath ?? string.Empty;
    public override string FileHash => FileMeta?.DataHash ?? string.Empty;

    public SMAGlamourParts      PartsFilter { get; set; } = SMAGlamourParts.None;
    public SMAFileSlotFilter    SlotFilter  { get; set; } = SMAFileSlotFilter.MainHand;
    public SMAFileMetaFilter    MetaFilter  { get; set; } = SMAFileMetaFilter.None;

    // Sadly this will only be full list until we have proper tree node filtering.
    public override Dictionary<string, string> FileReplacements => _replacements;
    public override string ManipString => _manipString;
    public override JObject GlamourState => GetFilteredState();
    public string CPlusData { get; private set; } = string.Empty;

    public string ImageStr
    {
        get;
        set
        {
            if (value != field)
                _imageData = new Lazy<byte[]>(() => string.IsNullOrEmpty(value)
                    ? Convert.FromBase64String(value) : Array.Empty<byte>());
            field = value;
        }
    }

    public void Dispose()
    {
        _storedImage?.Dispose();
        _storedImage = null;
    }

    private JObject GetFilteredState()
    {
        return _glamourState;
    }

    public override IDalamudTextureWrap GetImageOrDefault()
    {
        if (string.IsNullOrEmpty(ImageStr) || _imageData.Value.IsNullOrEmpty())
            return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];
        // Return existing if valid.
        if (_storedImage is not null)
            return _storedImage;
        // Otherwise create it, and return the default while it generates.
        Generic.Safe(() => _storedImage = Svc.Texture.CreateFromImageAsync(_imageData.Value).Result);
        return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];
    }

    public override bool ValidForSaving()
        => Id != Guid.Empty && _replacements.Count > 0
        && string.IsNullOrEmpty(_manipString) && _glamourState.HasValues;

    public override bool ValidForUpdateSave()
        => ValidForSaving();
}

public class ModularActorItem : ModularActorElement, IDisposable
{
    internal SMAFileMeta? FileMeta;

    private Dictionary<string, string> _replacements = new();
    private string _manipString;
    private JObject _glamourState;
    private Lazy<byte[]> _imageData;
    private IDalamudTextureWrap? _storedImage;

    public ModularActorItem(ItemFileDataSummary summary)
    {
        Id = summary.FileId;
        Name = summary.Name;
        Description = summary.Description;
        ImageStr = summary.ImageStr;
        _imageData = new Lazy<byte[]>(() => ImageStr.Length > 0
            ? Convert.FromBase64String(ImageStr) : Array.Empty<byte>());
        _glamourState = summary.GlamourState;
        _manipString = summary.ModManips;
    }

    public ModularActorItem(ItemFileDataSummary summary, Dictionary<string, string> modDict)
        : this(summary) => _replacements = modDict;

    public ModularActorItem(ItemFileDataSummary summary, SMAFileMeta meta)
        : this(summary) => FileMeta = meta;

    public ModularActorItem(ItemFileDataSummary summary, SMAFileMeta meta, Dictionary<string, string> modDict)
        : this(summary, modDict) => FileMeta = meta;

    public override string ExpectedFilePath => FileMeta?.FilePath ?? string.Empty;
    public override string FileHash => FileMeta?.DataHash ?? string.Empty;

    public override Dictionary<string, string> FileReplacements => _replacements;
    public override string ManipString => _manipString;
    public override JObject GlamourState => _glamourState;
    public string ImageStr
    {
        get;
        set
        {
            if (value != field)
                _imageData = new Lazy<byte[]>(() => string.IsNullOrEmpty(value)
                    ? Convert.FromBase64String(value) : Array.Empty<byte>());
            field = value;
        }
    }

    public void Dispose()
    {
        _storedImage?.Dispose();
        _storedImage = null;
    }

    public override IDalamudTextureWrap GetImageOrDefault()
    {
        if (string.IsNullOrEmpty(ImageStr) || _imageData.Value.IsNullOrEmpty())
            return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];
        // Return existing if valid.
        if (_storedImage is not null)
            return _storedImage;
        // Otherwise create it, and return the default while it generates.
        Generic.Safe(() => _storedImage = Svc.Texture.CreateFromImageAsync(_imageData.Value).Result);
        return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];
    }

    public override bool ValidForSaving()
        => Id != Guid.Empty && _replacements.Count > 0
        && string.IsNullOrEmpty(_manipString) && _glamourState.HasValues;

    public override bool ValidForUpdateSave()
        => ValidForSaving();
}
