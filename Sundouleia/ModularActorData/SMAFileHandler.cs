using K4os.Compression.LZ4.Legacy;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Watchers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;


/// <summary>
///     Assists in how SMA Files are saved to and loaded from disk. <para />
///     Both services using these handles can necessary logic here. 
///     (Resolve circular dependencies as things go)
/// </summary> 
public class SMAFileHandler : IDisposable
{
    // A lot of the following below is a mess that is in need of organizing. Workflow process so far is great.
    private readonly ILogger<SMAFileHandler> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly IpcManager _ipc;
    private readonly ModdedStateManager _moddedState;
    private readonly CharaObjectWatcher _watcher;

    private Progress<(string fileName, int percent)> _saveProgress = new();
    public SMAFileHandler(ILogger<SMAFileHandler> logger, MainConfig mainConfig,
        ModularActorsConfig smaConfig, FileCacheManager cacheManager,
        SMAFileCacheManager smaFileCache, IpcManager ipc,
        ModdedStateManager moddedState, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _ipc = ipc;
        _moddedState = moddedState;
        _watcher = watcher;

        _saveProgress.ProgressChanged += UpdateProgress;
    }

    public string CurrentFile { get; private set; } = string.Empty;
    public int ScannedFiles { get; private set; } = 0;
    public int TotalFiles { get; private set; } = 0;

    private void UpdateProgress(object? sender, (string, int) e)
    {
        CurrentFile = e.Item1;
        ScannedFiles = e.Item2;
    }

    public void Dispose()
    {
        _saveProgress.ProgressChanged -= UpdateProgress;
    }

    public async Task<BaseFileDataSummary?> SaveSMABFile(OwnedObject actor, string filePath, string name, string description, CancellationToken ct = default)
        => await SaveSMABFile(actor, filePath, name, description, string.Empty, ct);

    public async Task<BaseFileDataSummary?> SaveSMABFile(OwnedObject actor, string filePath, string name, string description, string password, CancellationToken ct = default)
    {
        // Collect Modded State (Preferably from Resource Tree Allocation.
        var curState = await _moddedState.CollectActorModdedState(actor, ct);

        // Generate unique id to assign for this file.
        var fileId = Guid.NewGuid();

        // generate a random decryption fileKey. Does not need to be anything complicated.
        var fileKey = SmadCryptography.Random(32); // 256 bit key. (used to be salt previously)

        // Construct the base fileData to be stored in the file's header region.
        var headerData = new BaseFileDataSummary()
        {
            FileId = fileId,
            Name = name,
            Description = description,
            ModManips = _ipc.Penumbra.GetMetaManipulationsString() // Modify later to an owned object index.
        };

        // Set remaining header data using filecaches and other retrievals.
        headerData.GlamourState = await _ipc.Glamourer.GetClientState().ConfigureAwait(false);
        if (actor is OwnedObject.Player)
            headerData.CPlusData = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;

        // Gather up the ModFile and FileSwaps for the appearance.
        CollectModdedReferences(headerData, curState);

        // Hold written data in a temporary file.
        var tmpFile = $"{filePath}.tmp";
        _logger.LogInformation($"Writing SMAB to temp FilePath: {tmpFile}");
        try
        {
            using var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
            using var writer = new BinaryWriter(lz4);

            // Write out the data.
            headerData.WriteHeader(writer);
            await WriteModFileData(writer, headerData, ct).ConfigureAwait(false);

            // Flush and close streams.
            writer.Flush();
            await lz4.FlushAsync().ConfigureAwait(false);
            await fs.FlushAsync().ConfigureAwait(false);
            _logger.LogInformation($"SMAB Compressed Size: {writer.BaseStream.Length} bytes.");
            fs.Close();

            // Update file and return.
            File.Move(tmpFile, filePath, true);
            // Can handle returning filedata to the actor config later, but for now just worry about saving.
            return headerData;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving SMAB: {ex}");
            File.Delete  (tmpFile);
            return null;
        }
    }

    // This would be a hell of a lot easier once we can parse resource trees directly like we used to do.
    // (for now this will filter out body and leg models for everything.
    private void CollectModdedReferences(BaseFileDataSummary header, ModdedState state)
    {
        // Group the files by their hash.
        var grouped = state.AllFiles.GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase);
        foreach (var file in grouped)
        {
            // If there is no key, it is a file swap, so add it as a swap.
            // However, do not add files that are body or leg models if present.
            if (string.IsNullOrEmpty(file.Key))
            {
                foreach (var item in file)
                {
                    if (item.GamePaths.Any(IsBodyLegModel))
                        continue;
                    // Otherwise, add it
                    header.FileSwaps.Add(new FileSwap(item.GamePaths, item.ResolvedPath));
                }
            }
            // Otherwise it could be a modded file.
            else
            {
                // If it is a valid modded file, add it to the file data.
                if (_fileCache.GetFileCacheByHash(file.First().Hash)?.ResolvedFilepath is { } validFile)
                {
                    // Do not add if a body/leg model and requested.
                    if (file.Any(f => f.GamePaths.Any(IsBodyLegModel)))
                        continue;
                    // Otherwise, add it.
                    header.Files.Add(new FileModData(file.SelectMany(f => f.GamePaths), (int)new FileInfo(validFile).Length, file.First().Hash));
                }
            }
        }
    }

    // Write via lz4 compression with high compression settings to a byte array that we then return for file writing.
    private async Task WriteModFileData(BinaryWriter writer, BaseFileDataSummary header, CancellationToken ct)
    {
        // Force the write process off the framework thread so we dont lock up the game while saving! YIPPEE!
        while (Svc.Framework.IsInFrameworkUpdateThread && !ct.IsCancellationRequested)
            await Task.Delay(1).ConfigureAwait(false);

        // Now access each of the files, and write our their raw data into the file.
        int currentFile = 0;
        TotalFiles = header.Files.Count;
        foreach (var fileItem in header.Files)
        {
            // Report progress for UI Assistance.
            ((IProgress<(string, int)>)_saveProgress).Report((fileItem.GamePaths.First(), currentFile));

            // Identify the file we are saving so we can locate its resolved filepath and write.
            var file = _fileCache.GetFileCacheByHash(fileItem.FileHash)!;
            _logger.LogDebug($"Saving to SMAB: {fileItem.FileHash}:{file.ResolvedFilepath}");

            var fsRead = File.OpenRead(file.ResolvedFilepath);
            await using (fsRead.ConfigureAwait(false))
            {
                using var br = new BinaryReader(fsRead);
                byte[] buffer = new byte[fileItem.Length];
                br.Read(buffer, 0, fileItem.Length);
                writer.Write(buffer);
            }
            currentFile++;
        }
        TotalFiles = 0;
    }

    private bool IsBodyLegModel(string gp) => gp.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
        && (gp.Contains("/body/", StringComparison.OrdinalIgnoreCase) || gp.Contains("/legs/", StringComparison.OrdinalIgnoreCase));


    public BaseFileDataSummary? LoadSmabFileHeader(string filePath)
    {
        _logger.LogInformation($"Loading SMAB File Header from Disk: {filePath}");
        try
        {
            using var fs = File.OpenRead(filePath);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var br = new BinaryReader(lz4);
            var fileSummary = BaseFileDataSummary.FromHeader(br, filePath);
            _logger.LogInformation($"SMAB FileDataSummary created. (Version: {fileSummary.Version})");
            return fileSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading SMAB Header: {ex}");
            return null;
        }
    }

    // Iterate through a file data summary to get the final modded dictionary.
    // We should probably revise this later for some other method as cannot ensure proper extraction, but whatever.
    public Dictionary<string, string> GetModdedDict(FileDataSummary summary)
    {
        var moddedPathDict = new Dictionary<string, string>(StringComparer.Ordinal);
        // Iterate first over each modded file.
        foreach (var fileData in summary.Files)
        {
            // Get the file extension.
            var fileExt = fileData.GamePaths.First().Split(".")[^1];
            var hash = fileData.FileHash;
            var fileLength = fileData.Length;
            // If the file alreadyGetFileCacheByHash exists in the sundouleia cache, remap the link to that instead.
            if (_fileCache.GetFileCacheByHash(hash) is { } fileEntity)
            {
                // Set all the fileData's gamepaths to this resolved filepath instead.
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileEntity.ResolvedFilepath;
                    _logger.LogTrace($"{gamepath} => {fileEntity.ResolvedFilepath} [{hash}] (sundouleia cache)");
                }
            }
            else
            {
                // Create a file in the SMACache.
                var fileName = _smaFileCache.GetCacheFilePath(hash, fileExt);
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileName;
                    _logger.LogTrace($"{gamepath} => {fileName} [{fileData.FileHash}] (SMA cache)");
                }
            }
        }

        // Allow FileSwaps to take priority here.
        foreach (var entry in summary.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
        {
            moddedPathDict[entry.Key] = entry.Value;
            _logger.LogTrace($"[Swap] {entry.Key} => {entry.Value}");
        }

        return moddedPathDict;
    }

    // Attempt to load a SMABase file from disk.
    public ModularActorBase? LoadSmabFile(string filePath)
    {
        // Obtain the file header and encrypted contents.
        _logger.LogInformation($"Loading SMAB File from Disk: {filePath}");

        // Once we add protected files we can perform decryption prior to this.
        try
        {
            using var fs = File.OpenRead(filePath);
            using var lz4 = new LZ4Stream(fs, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var br = new BinaryReader(lz4);

            var fileSummary = BaseFileDataSummary.FromHeader(br, filePath);
            _logger.LogInformation($"SMAB FileDataSummary created. (Version: {fileSummary.Version})");

            // Import the file contents, updating their references to our existing cache, or
            // a temporary cache, writing new files during gpose lifetime.
            var moddedDict = CreateModdedDictionary(br, fileSummary);
            _logger.LogInformation("SMAB Modded Dictionary Created.");

            return new ModularActorBase(fileSummary, moddedDict);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading SMAB: {ex}");
            return null;
        }
    }

    /// <summary>
    ///     Reads through a SMA File's mod contents using the <see cref="FileDataSummary"/>'s ModFile and FileSwaps. <para />
    ///     For each item, if the path is found in the SundouleiaCache, it is remapped to that location. <para />
    ///     If an item is not found in our cache, it is written to a temporary cache exposed during GPose lifetime.
    /// </summary>
    /// <returns> The final calculated modded dictionary of gamepath and replacement paths. </returns>
    private Dictionary<string, string> CreateModdedDictionary(BinaryReader reader, FileDataSummary summary)
    {
        var contentsLength = reader.ReadInt32();

        // Now we need to extract out the file data into the cached folder, or hold a ref to our sundouleia cache if present there.
        long totalRead = 0;
        var moddedPathDict = new Dictionary<string, string>(StringComparer.Ordinal);
        // Iterate first over each modded file.
        foreach (var fileData in summary.Files)
        {
            // Get the file extension.
            var fileExt = fileData.GamePaths.First().Split(".")[^1];
            var hash = fileData.FileHash;
            var fileLength = fileData.Length; // Know how much to read.

            // If the file alreadyGetFileCacheByHash exists in the sundouleia cache, remap the link to that instead.
            if (_fileCache.GetFileCacheByHash(hash) is { } fileEntity)
            {
                // Set all the fileData's gamepaths to this resolved filepath instead.
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileEntity.ResolvedFilepath;
                    _logger.LogTrace($"{gamepath} => {fileEntity.ResolvedFilepath} [{hash}] (sundouleia cache)");
                }
                // We still must consume the bytes from the compressed stream so the reader is positioned correctly.
                var buffer = reader.ReadBytes(fileLength);
                if (buffer.Length is 0)
                    throw new EndOfStreamException("Unexpected EOF while skipping cached file data");
                totalRead += buffer.Length;
                _logger.LogTrace($"Skipped {SundouleiaEx.ByteToString(buffer.Length)} bytes for cached file {hash}");
                continue;
            }
            else
            {
                // Create a file in the SMACache.
                var fileName = _smaFileCache.GetCacheFilePath(hash, fileExt);
                // Open for writestream to write out the file data.
                using var fs = File.OpenWrite(fileName);
                using var wr = new BinaryWriter(fs);
                _logger.LogTrace($"Writing {SundouleiaEx.ByteToString(fileLength)} bytes into {fileName}");
                var buffer = reader.ReadBytes(fileLength);
                // write the buffer to the file then flush and close streams.
                wr.Write(buffer);
                wr.Flush();
                wr.Close();
                // If there was no buffer, throw an exception.
                if (buffer.Length is 0)
                    throw new EndOfStreamException("Unexpected EOF");
                // Ensure that all these gamepaths for this file have the swap to this filepath.
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileName;
                    _logger.LogTrace($"{gamepath} => {fileName} [{fileData.FileHash}] (SMA cache)");
                }
                // Inc the total read and report progress.
                totalRead += buffer.Length;
                _logger.LogDebug($"Read {SundouleiaEx.ByteToString(totalRead)}/{SundouleiaEx.ByteToString(contentsLength)} bytes");
            }
        }

        // Allow FileSwaps to take priority here.
        foreach (var entry in summary.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
        {
            moddedPathDict[entry.Key] = entry.Value;
            _logger.LogTrace($"[Swap] {entry.Key} => {entry.Value}");
        }

        return moddedPathDict;
    }
}