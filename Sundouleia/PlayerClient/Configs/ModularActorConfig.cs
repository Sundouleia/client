using CkCommons.HybridSaver;
using Sundouleia.Services.Configs;

namespace Sundouleia.PlayerClient;


public class ModularActorsConfig : IHybridSavable
{
    private readonly ILogger<ModularActorsConfig> _logger;
    private readonly HybridSaveService _saver;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.ModularActorsConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["ModularActors"] = JObject.FromObject(Current),
        }.ToString(Formatting.Indented);
    }
    public ModularActorsConfig(ILogger<ModularActorsConfig> logger, HybridSaveService saver)
    {
        _logger = logger;
        _saver = saver;
        Load();
    }

    public void Save() => _saver.Save(this);
    public void Load()
    {
        var file = _saver.FileNames.ModularActorsConfig;
        _logger.LogInformation("Loading in Config for file: " + file);
        if (!File.Exists(file))
        {
            _logger.LogWarning("Config file not found for: " + file);
            _saver.Save(this);
            return;
        }

        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        // execute based on version.
        switch (version)
        {
            case 0:
                LoadV0(jObject["ModularActors"]);
                break;
            default:
                _logger.LogError("Invalid Version!");
                return;
        }
        Save();
    }

    private void LoadV0(JToken? data)
    {
        if (data is not JObject storage)
            return;
        Current = storage.ToObject<ModularActorStorage>() ?? throw new Exception("Failed to load ModularActorStorage.");
    }

    public ModularActorStorage Current { get; set; } = new ModularActorStorage();

    public bool AddBaseFile(Guid fileId, string fileName, string filePath, string password, byte[] publicKey, byte[] privateKey)
    {
        // If there is an entry already, ignore it.
        if (Current.BaseFiles.ContainsKey(fileId))
            return false;

        // Otherwise, add it.
        Current.BaseFiles[fileId] = new ModularActorBaseEntry(fileId, fileName, filePath)
        {
            AccessPassword = password,
            FilePublicKey = publicKey,
            FilePrivateKey = privateKey
        };
        Save();
        return true;
    }

    public bool AddOutfitFile(Guid fileId, string fileName, string filePath)
    {
        if (Current.OutfitFiles.ContainsKey(fileId))
            return false;
        Current.OutfitFiles[fileId] = new ModularActorOtherEntry(fileId, fileName, filePath);
        Save();
        return true;
    }

    public bool AddItemFile(Guid fileId, string fileName, string filePath)
    {
        if (Current.ItemFiles.ContainsKey(fileId))
            return false;
        Current.ItemFiles[fileId] = new ModularActorOtherEntry(fileId, fileName, filePath);
        Save();
        return true;
    }

    public bool AddItemPackFile(Guid fileId, string fileName, string filePath)
    {
        if (Current.ItemPackFiles.ContainsKey(fileId))
            return false;
        Current.ItemPackFiles[fileId] = new ModularActorOtherEntry(fileId, fileName, filePath);
        Save();
        return true;
    }

    // Validate the integrity of all files listed in storage.
    // If any file is not in its expected location, flag it as invalid.
    public void CheckBaseIntegrity(out List<Guid> invalidFiles)
    {
        invalidFiles = new List<Guid>();
        foreach (var files in Current.BaseFiles.Values)
        {
            if (!File.Exists(files.FilePath))
            {
                _logger.LogWarning($"SMABase file missing ({files.FileName}): {files.FilePath}");
                invalidFiles.Add(files.FileId);
            }
        }
        // Maybe remove invalid files? Or keep to mark as invalid? Idk yet.
    }

    public void CheckOutfitIntegrity(out List<Guid> invalidIds)
    {
        invalidIds = new List<Guid>();
        foreach (var files in Current.OutfitFiles.Values)
        {
            if (!File.Exists(files.FilePath))
            {
                _logger.LogWarning($"SMAOutfit file missing ({files.FileName}): {files.FilePath}");
                invalidIds.Add(files.FileId);
            }
        }
    }

    public void CheckItemIntegrity(out List<Guid> invalidIds)
    {
        invalidIds = new List<Guid>();
        foreach (var files in Current.ItemFiles.Values)
        {
            if (!File.Exists(files.FilePath))
            {
                _logger.LogWarning($"SMAItem file missing ({files.FileName}): {files.FilePath}");
                invalidIds.Add(files.FileId);
            }
        }
    }

    public bool AirstrikeBaseFile(Guid id)
    {
        if (!Current.BaseFiles.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }

    public bool AirstrikeOutfitFile(Guid id)
    {
        if (!Current.OutfitFiles.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }

    public bool AirstrikeItemFile(Guid id)
    {
        if (!Current.ItemFiles.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }
}

/// <summary>
///     Private data associated with all created Sundouleia Modular Actor 
///     Data, Base, Outfit, Item, and ItemPack files.
/// </summary>
/// <remarks> If lost, you will need to resend these files (if a backup cannot be recovered). </remarks>
public class ModularActorStorage
{
    /// <summary>
    ///     A Global password to use when saving a file that no password was provided for.
    /// </summary>
    public string FallbackPassword { get; set; } = string.Empty;

    // All created ModularActorBase files details and private access data.
    public Dictionary<Guid, ModularActorBaseEntry> BaseFiles { get; set; } = new();

    // All created ModularActorOutfit file details. (Could maybe key by hash but idk yet.)
    public Dictionary<Guid, ModularActorOtherEntry> OutfitFiles { get; set; } = new();

    // All created ModularActorItem file details. (Could maybe key by hash but idk yet.)
    public Dictionary<Guid, ModularActorOtherEntry> ItemFiles { get; set; } = new();

    // Might not need to store created item packs since they are just a list of allowed items effectively.
    public Dictionary<Guid, ModularActorOtherEntry> ItemPackFiles { get; set; } = new();

}

/// <summary>
///     Holds essential info needed to decrypt a ModularActorBase file.
/// </summary>
public record ModularActorBaseEntry(Guid FileId, string FileName, string FilePath)
{
    internal string AccessPassword { get; set; } = string.Empty; // Required to open the base file.
    internal byte[] FilePublicKey { get; set; } = Array.Empty<byte>(); // Required to access & decrypt. (may not need)
    internal byte[] FilePrivateKey { get; set; } = Array.Empty<byte>(); // Required to make access updates to files.
}

/// <summary>
///     Holds essential info to location and identify other Modular Actor file types.
/// </summary>
public record ModularActorOtherEntry(Guid FileId, string FileName, string FilePath);