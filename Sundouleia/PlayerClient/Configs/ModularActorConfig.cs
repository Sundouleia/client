using CkCommons.HybridSaver;
using Penumbra.String.Classes;
using Sundouleia.ModularActor;
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
    public string GetFileName(ConfigFileProvider files, out bool upa) => (upa = false, files.OwnedSMAFilesConfig).Item2;
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["OwnedSMAFiles"] = JObject.FromObject(Current),
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
        var file = _saver.FileNames.OwnedSMAFilesConfig;
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
                LoadV0(jObject["OwnedSMAFiles"]);
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
        Current = storage.ToObject<OwnedSMAFileStorage>() ?? throw new Exception("Failed to load ModularActorStorage.");
    }

    public OwnedSMAFileStorage Current { get; set; } = new OwnedSMAFileStorage();

    public bool AddSMADFile(FileDataSummary summary, string filePath, string fileDataHash, string fileKey, string? password = "")
    {
        var res = Current.OwnedSMADFiles.TryAdd(summary.FileId, new SMABaseFileMeta
        {
            Id = summary.FileId,
            Name = summary.Name,

            FilePath = filePath,
            DataHash = fileDataHash,
            Password = password ?? string.Empty,
            PrivateKey = fileKey
        });
        if (res) Save();
        return res;
    }

    public bool AddSMABFile(FileDataSummary summary, string filePath, string fileDataHash, string fileKey, string? password = "")
    {
        var res = Current.OwnedSMABFiles.TryAdd(summary.FileId, new SMABaseFileMeta
        {
            Id = summary.FileId,
            Name = summary.Name,
            FilePath = filePath,
            DataHash = fileDataHash,
            Password = password ?? string.Empty,
            PrivateKey = fileKey
        });
        if (res) Save();
        return res;
    }

    public bool AddFile(SMAFileType ext, FileDataSummary summary, string filePath, string fileDataHash)
    {
        if (!File.Exists(filePath))
            return false;
        var fileMeta = new SMAFileMeta
        {
            Id = summary.FileId,
            Name = summary.Name,
            FilePath = filePath,
            DataHash = fileDataHash,
        };
        var res = ext switch
        {
            SMAFileType.Outfit => Current.OwnedSMAOFiles.TryAdd(summary.FileId, fileMeta),
            SMAFileType.Item => Current.OwnedSMAIFiles.TryAdd(summary.FileId, fileMeta),
            SMAFileType.ItemPack => Current.OwnedSMAIPFiles.TryAdd(summary.FileId, fileMeta),
            _ => false,
        };
        if (res) Save();
        return res;
    }

    public bool RemoveFile(Guid id, SMAFileType extension)
    {
        var res = extension switch
        {
            SMAFileType.Full => Current.OwnedSMADFiles.Remove(id),
            SMAFileType.Base => Current.OwnedSMABFiles.Remove(id),
            SMAFileType.Outfit => Current.OwnedSMAOFiles.Remove(id),
            SMAFileType.Item => Current.OwnedSMAIFiles.Remove(id),
            SMAFileType.ItemPack => Current.OwnedSMAIPFiles.Remove(id),
            _ => false,
        };
        if (res) Save();
        return res;
    }
}

public sealed class OwnedSMAFileStorage
{
    public Dictionary<Guid, SMABaseFileMeta> OwnedSMADFiles { get; set; } = new();
    public Dictionary<Guid, SMABaseFileMeta> OwnedSMABFiles { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     OwnedSMAOFiles { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     OwnedSMAIFiles { get; set; } = new();
    public Dictionary<Guid, SMAFileMeta>     OwnedSMAIPFiles{ get; set; } = new(); // Maybe remove? Unsure.
}


// A Metadata record for SMA Base Files. If the filePath does not match the file contents, we can assume it is invalid.
public record SMAFileMeta
{
    public string FilePath  { get; set; } = string.Empty; // Expected Location on Disk.
    public string DataHash  { get; set; } = string.Empty; // Hash of the file data for integrity checks.

    public Guid   Id        { get; set; } = Guid.Empty;   // Unique Identifier for the file.
    public string Name      { get; set; } = string.Empty; // Name of the file. (In the case it fails to load)

    public bool IsValid() => IsValidPath() && IsValidData();
    public bool IsValidPath() => File.Exists(FilePath);
    public virtual bool IsValidData()
    {
        // Maybe something here for unencrypted data filter.
        return true;
    }

}

public record SMABaseFileMeta : SMAFileMeta
{
    public List<string> AllowedDataHashes { get; set; } = new();        // What other files can be used with this base.
    public string       PrivateKey        { get; set; } = string.Empty; // Private Key to decrypt the file.
    public string       Password          { get; set; } = string.Empty; // Password to access the file. (Blank assumes none)

    public override bool IsValidData()
    {
        var dataHash = SundouleiaSecurity.GetFileHashSHA256(FilePath);
        if (string.IsNullOrEmpty(dataHash))
            return false;
        return string.Equals(DataHash, dataHash, StringComparison.Ordinal);
    }
}