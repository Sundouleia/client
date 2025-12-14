using CkCommons.HybridSaver;
using Penumbra.String.Classes;
using Sundouleia.Services.Configs;
using Sundouleia.Utils;

namespace Sundouleia.PlayerClient;

// Currently contemplating the use of this file, since it only serves to help identify files, but not actually store them.
// Perhaps it will become more clear later.
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
        var res = Current.BaseFileInfo.TryAdd(fileId, new SMABaseFileMeta
        {
            Id = fileId,
            FilePath = filePath,
            Password = password,
            PrivateKey = Convert.ToBase64String(privateKey),
            DataHash = SundouleiaSecurity.GetFileHashSHA256(filePath)
        });
        if (res) Save();
        return res;
    }

    public bool AddOutfitFile(Guid fileId, string fileName, string filePath)
    {
        if (File.Exists(filePath))
            return false;

        var res = Current.OutfitFileInfo.TryAdd(fileId, new SMAFileMeta
        {
            Id = fileId,
            FilePath = filePath,
            DataHash = SundouleiaSecurity.GetFileHashSHA256(filePath)
        });
        if (res) Save();
        return res;
    }

    public bool AddItemFile(Guid fileId, string fileName, string filePath)
    {
        if (File.Exists(filePath))
            return false;
        var res = Current.ItemFileInfo.TryAdd(fileId, new SMAFileMeta
        {
            Id = fileId,
            FilePath = filePath,
            DataHash = SundouleiaSecurity.GetFileHashSHA256(filePath)
        });
        if (res) Save();
        return res;
    }

    public bool AddItemPackFile(Guid fileId, string fileName, string filePath)
    {
        if (File.Exists(filePath))
            return false;
        var res = Current.ItemPackFileInfo.TryAdd(fileId, new SMAFileMeta
        {
            Id = fileId,
            FilePath = filePath,
            DataHash = SundouleiaSecurity.GetFileHashSHA256(filePath)
        });
        if (res) Save();
        return res;
    }

    // Idk why not just update the state in the record itself but whatever.
    public void ValidateIntegrity(List<SMAFileMeta> files, out List<Guid> invalidFiles)
    {
        invalidFiles = new List<Guid>();
        foreach (var file in files)
            if (!file.IsValid())
            {
                _logger.LogWarning($"SMA file invalid ({file.FilePath}): {file.FilePath}");
                invalidFiles.Add(file.Id);
            }
    }

    public bool AirstrikeBaseFile(Guid id)
    {
        if (!Current.BaseFileInfo.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }

    public bool AirstrikeOutfitFile(Guid id)
    {
        if (!Current.OutfitFileInfo.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }

    public bool AirstrikeItemFile(Guid id)
    {
        if (!Current.ItemFileInfo.Remove(id, out var removed))
            return false;
        // Otherwise log removal and save.
        _logger.LogInformation($"Launched airstrikes on ({removed.FilePath}), removing it from storage.");
        Save();
        return true;
    }
}

// Helps us obtain correct file information for our own exported data on 
public class ModularActorStorage
{
    // Could maybe key this by file-path, or data-hash, idk yet.
    public Dictionary<Guid, SMABaseFileMeta> BaseFileInfo     { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     OutfitFileInfo   { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     ItemFileInfo     { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     ItemPackFileInfo { get; set; } = new();
}

// A Metadata record for SMA Base Files. If the filePath does not match the file contents, we can assume it is invalid.
public record SMAFileMeta
{
    public string FilePath      { get; set; } = string.Empty; // Expected Location on Disk.
    public Guid   Id            { get; set; } = Guid.Empty;   // Unique Identifier for the file.
    public string Name          { get; set; } = string.Empty; // Some info for UI Help.
    public string Description   { get; set; } = string.Empty; // Some info for UI Help.

    public string DataHash   { get; set; } = string.Empty; // Hash of the file data for integrity checks.

    public bool IsValid() => IsValidPath() && IsValidData();
    public bool IsValidPath() => File.Exists(FilePath);
    public bool IsValidData()
    {
        var dataHash = SundouleiaSecurity.GetFileHashSHA256(FilePath);
        if (string.IsNullOrEmpty(dataHash))
            return false;
        return string.Equals(DataHash, dataHash, StringComparison.Ordinal);
    }
}

public record SMABaseFileMeta : SMAFileMeta
{
    public List<string> AllowedDataHashes { get; set; } = new();        // What other files can be used with this base.
    public string       PrivateKey        { get; set; } = string.Empty; // Private Key to decrypt the file.
    public string       Password          { get; set; } = string.Empty; // Password to access the file. (Blank assumes none)
}