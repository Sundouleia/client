using Dalamud.Interface.ImGuiNotification;
using K4os.Compression.LZ4.Legacy;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using SundouleiaAPI.Data;
using System.Globalization;

namespace Sundouleia.ModFiles;

/// <summary>
///     Manages the contents of the FileCache.csv file
/// </summary>
public sealed class FileCacheManager : IHostedService
{
    private readonly ILogger<FileCacheManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly IpcManager _ipc;
    private readonly ConfigFileProvider _fileNames;

    // File cache entities of current files stored in the cache.
    private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _fileCaches = new(StringComparer.Ordinal);
    private readonly Lock _getCachesByPathsLock = new();
    private readonly Lock _fileWriteLock = new();

    public FileCacheManager(ILogger<FileCacheManager> logger, SundouleiaMediator mediator,
        MainConfig config, IpcManager ipc, ConfigFileProvider fileNames)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _ipc = ipc;
        _fileNames = fileNames;
    }

    public int TotalCacheEntities => _fileCaches.Sum(k => k.Value.Count);
    private string CsvBakPath => _fileNames.FileCacheCsv + ".bak";

    public bool CacheFolderIsValid() => _config.HasValidCacheFolderSetup();

    /// <summary>
    ///     Creates a new entity to reflect the provided sundouleia file path, if it exists. <para />
    ///     The created entity is then returned, or null if invalid.
    /// </summary>
    public FileCacheEntity? CreateCacheEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace($"Creating cache entry for {path}", LoggerType.FileCache);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_config.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(_config.Current.CacheFolder.ToLowerInvariant(), Constants.PrefixCache + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    /// <summary>
    ///     Creates a new entity to reflect the provided penumbra file path, if the file exists. <para />
    ///     The created entity is then returned, or null if invalid.
    /// </summary>
    public FileCacheEntity? CreateFileEntry(string path)
    {
        FileInfo fi = new(path);
        if (!fi.Exists) return null;
        _logger.LogTrace($"Creating file entry for {path}", LoggerType.FileCache);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(IpcCallerPenumbra.ModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;
        string prefixedPath = fullName.Replace(IpcCallerPenumbra.ModDirectory!.ToLowerInvariant(), Constants.PrefixPenumbra + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    /// <summary>
    ///     Retrieves all current file caches in the concurrent dictionary.
    /// </summary>
    public List<FileCacheEntity> GetAllFileCaches()
        => _fileCaches.Values.SelectMany(v => v).ToList();

    public List<FileCacheEntity> GetAllFileCachesByHash(string hash, bool ignoreCacheEntries = false, bool validate = true)
    {
        List<FileCacheEntity> output = [];
        if (_fileCaches.TryGetValue(hash, out var fileCacheEntities))
        {
            foreach (var fileCache in fileCacheEntities.Where(c => ignoreCacheEntries ? !c.IsCacheEntry : true).ToList())
            {
                if (!validate) output.Add(fileCache);
                else
                {
                    var validated = GetValidatedFileCache(fileCache);
                    if (validated != null) output.Add(validated);
                }
            }
        }

        return output;
    }

    // may not need later. (Primarily used to validate cache integrity but we can also just add a helper method for this).
    public Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
    {
        // _mediator.Publish(new HaltScanMessage(nameof(ValidateLocalIntegrity)));
        _logger.LogInformation("Validating local storage");
        var cacheEntries = _fileCaches.SelectMany(v => v.Value).Where(v => v.IsCacheEntry).ToList();
        List<FileCacheEntity> brokenEntities = [];
        int i = 0;
        foreach (var fileCache in cacheEntries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger.LogInformation("Validating {file}", fileCache.ResolvedFilepath);

            progress.Report((i, cacheEntries.Count, fileCache));
            i++;
            if (!File.Exists(fileCache.ResolvedFilepath))
            {
                brokenEntities.Add(fileCache);
                continue;
            }

            try
            {
                var computedHash = SundouleiaSecurity.GetFileHash(fileCache.ResolvedFilepath);
                if (!string.Equals(computedHash, fileCache.Hash, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Failed to validate {file}, got hash {hash}, expected hash {hash}", fileCache.ResolvedFilepath, computedHash, fileCache.Hash);
                    brokenEntities.Add(fileCache);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error during validation of {file}", fileCache.ResolvedFilepath);
                brokenEntities.Add(fileCache);
            }
        }

        foreach (var brokenEntity in brokenEntities)
        {
            RemoveHashedFile(brokenEntity.Hash, brokenEntity.PrefixedFilePath);

            try
            {
                File.Delete(brokenEntity.ResolvedFilepath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {file}", brokenEntity.ResolvedFilepath);
            }
        }

        //_mediator.Publish(new ResumeScanMessage(nameof(ValidateLocalIntegrity)));
        return Task.FromResult(brokenEntities);
    }

    public long GetFileSizeByPath(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    public long GetFileSizeByHash(string hash)
    {
        if (GetFileCacheByHash(hash) is not { } entity) return 0;
        return File.Exists(entity.ResolvedFilepath) ? new FileInfo(entity.ResolvedFilepath).Length : 0;
    }


    // Used mainly for download management.
    public string GetCacheFilePath(string hash, string extension)
        => Path.Combine(_config.Current.CacheFolder, hash + "." + extension);

    // Used mainly for download management.
    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        var fileCache = GetFileCacheByHash(fileHash)!.ResolvedFilepath;
        return (fileHash, LZ4Wrapper.WrapHC(await File.ReadAllBytesAsync(fileCache, uploadToken).ConfigureAwait(false), 0,
            (int)new FileInfo(fileCache).Length));
    }

    /// <returns>
    ///     Returns the verified mod files who's hashes are not currently cached in the manager.
    /// </returns>
    public IEnumerable<ValidFileHash> MissingHashes(IEnumerable<ValidFileHash> modFiles)
    {
        foreach (var f in modFiles)
        {
            if (!_fileCaches.ContainsKey(f.Hash))
                yield return f;
        }
    }

    /// <summary>
    ///     Obtain the cached file entity in the managers concurrent dictionary via lookup by hash.
    /// </summary>
    public FileCacheEntity? GetFileCacheByHash(string hash)
    {
        // Locate it from the concurrent dictionary
        if (!_fileCaches.TryGetValue(hash, out var hashes))
            return null;
        // File was found for the hash, but it could represent multiple cached file entities.
        // Prioritize the ones from penumbra first, then the internal cache.
        var item = hashes.OrderBy(p => p.PrefixedFilePath.Contains(Constants.PrefixPenumbra) ? 0 : 1).FirstOrDefault();
        // If there is a valid item, return the validated entity.
        if (item is not null)
            return GetValidatedFileCache(item);
        // Otherwise null return.
        return null;
    }


    private FileCacheEntity? GetFileCacheByPath(string path)
    {
        var cleanedPath = path.Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()
            .Replace(IpcCallerPenumbra.ModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);

        var entry = _fileCaches.SelectMany(v => v.Value).FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            _logger.LogDebug($"Found no entries for {cleanedPath}", LoggerType.FileCache);
            return CreateFileEntry(path);
        }

        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    // Returns the dictionary of file catches by the paths passed in.
    // This should in theory retrieve the respective files from our penumbra and/or sundouleia cache files.
    // -- Dont entirely understand why we do this yet but i'm sure I will as time goes on.
    public Dictionary<string, FileCacheEntity?> GetFileCachesByPaths(string[] paths)
    {
        lock( _getCachesByPathsLock)
        {
            // retrieved the cleansed paths via the penumbra mod directory and cache folder (not sure why we would need this, but whatever)
            var cleanedPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p,
                p => p.Replace("/", "\\", StringComparison.OrdinalIgnoreCase)
                    .Replace(IpcCallerPenumbra.ModDirectory!, IpcCallerPenumbra.ModDirectory!.EndsWith('\\') ? Constants.PrefixPenumbra + '\\' : Constants.PrefixPenumbra, StringComparison.OrdinalIgnoreCase)
                    .Replace(_config.Current.CacheFolder, _config.Current.CacheFolder.EndsWith('\\') ? Constants.PrefixCache + '\\' : Constants.PrefixCache, StringComparison.OrdinalIgnoreCase)
                    .Replace("\\\\", "\\", StringComparison.Ordinal),
                StringComparer.OrdinalIgnoreCase);

            _logger.LogDebug($"== Fetching FileCaches by Paths Cleaned Paths ==", LoggerType.FileCsv);
            foreach ( var entry in cleanedPaths)
                _logger.LogDebug($"Key: {entry.Key}, Value: {entry.Value}", LoggerType.FileCsv);

            // not sure why this isn't merged with the line below, but whatever, basically just constructs the result.
            Dictionary<string, FileCacheEntity?> result = new(StringComparer.OrdinalIgnoreCase);

            var dict = _fileCaches.SelectMany(f => f.Value)
                .ToDictionary(d => d.PrefixedFilePath, d => d, StringComparer.OrdinalIgnoreCase);

            // adds the cleansed paths to the result and whatever.
            foreach (var entry in cleanedPaths)
            {
                _logger.LogTrace($"Checking {entry.Value}", LoggerType.FileCsv);

                if (dict.TryGetValue(entry.Value, out var entity))
                {
                    var validatedCache = GetValidatedFileCache(entity);
                    result.Add(entry.Key, validatedCache);
                }
                else
                {
                    if (!entry.Value.Contains(Constants.PrefixCache, StringComparison.Ordinal))
                        result.Add(entry.Key, CreateFileEntry(entry.Key));
                    else
                        result.Add(entry.Key, CreateCacheEntry(entry.Key));
                }
            }

            return result;
        }
    }

    public void RemoveHashedFile(string hash, string prefixedFilePath)
    {
        if (_fileCaches.TryGetValue(hash, out var caches))
        {
            var removedCount = caches?.RemoveAll(c => string.Equals(c.PrefixedFilePath, prefixedFilePath, StringComparison.Ordinal));
            _logger.LogTrace($"Removed from DB: {removedCount} file(s) with hash {hash} and file cache {prefixedFilePath}", LoggerType.FileCache);

            if (caches?.Count == 0)
            {
                _fileCaches.Remove(hash, out var entity);
            }
        }
    }

    // Seems to update the file-cache data that is stored within the concurrent dictionary data, and not the data in the sundouleia-cache.
    // This might explain why the sundouleia-cache grows overtime without any cleanup system.
    public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
    {
        _logger.LogTrace($"Updating hash for {fileCache.ResolvedFilepath}", LoggerType.FileCache);
        var oldHash = fileCache.Hash;
        var prefixedPath = fileCache.PrefixedFilePath;
        if (computeProperties)
        {
            var fi = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Size = fi.Length;
            fileCache.CompressedSize = null;
            fileCache.Hash = SundouleiaSecurity.GetFileHash(fileCache.ResolvedFilepath);
            fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        // remove the old hash file
        RemoveHashedFile(oldHash, prefixedPath);
        // update it by adding in the new hashed file.
        AddHashedFile(fileCache);
    }

    public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }
        if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    public void WriteOutFullCsv()
    {
        lock (_fileWriteLock)
        {
            StringBuilder sb = new();
            // hellfire, but optimal i guess.
            foreach (var entry in _fileCaches.SelectMany(k => k.Value).OrderBy(f => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine(entry.CsvEntry);

            // Make a backup!
            if (File.Exists(_fileNames.FileCacheCsv))
                File.Copy(_fileNames.FileCacheCsv, CsvBakPath, overwrite: true);

            // Write all text to the main one, and then delete the backup if successful.
            try
            {
                File.WriteAllText(_fileNames.FileCacheCsv, sb.ToString());
                File.Delete(CsvBakPath);
            }
            catch
            {
                // Otherwise, write all text to the backup path so we dont lose everything.
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    // something something pair handler voodoo magic wowie crazy world.
    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        try
        {
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
            var extensionPath = fileCache.ResolvedFilepath.ToUpper(CultureInfo.InvariantCulture) + "." + ext;
            File.Move(fileCache.ResolvedFilepath, extensionPath, overwrite: true);
            var newHashedEntity = new FileCacheEntity(fileCache.Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            newHashedEntity.SetResolvedFilePath(extensionPath);
            AddHashedFile(newHashedEntity);
            _logger.LogTrace($"Migrated from {fileCache.ResolvedFilepath} to {newHashedEntity.ResolvedFilepath}", LoggerType.FileCache);
            return newHashedEntity;
        }
        catch (Exception ex)
        {
            AddHashedFile(fileCache);
            _logger.LogWarning($"Failed to migrate entity {fileCache.PrefixedFilePath}: {ex}");
            return fileCache;
        }
    }

    private void AddHashedFile(FileCacheEntity fileCache)
    {
        if (!_fileCaches.TryGetValue(fileCache.Hash, out var entries) || entries is null)
        {
            _fileCaches[fileCache.Hash] = entries = [];
        }

        if (!entries.Exists(u => string.Equals(u.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
        {
            //_logger.LogTrace("Adding to DB: {hash} => {path}", fileCache.Hash, fileCache.PrefixedFilePath);
            entries.Add(fileCache);
        }
    }

    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        hash ??= SundouleiaSecurity.GetFileHash(fileInfo.FullName);
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
        entity = ReplacePathPrefixes(entity);
        AddHashedFile(entity);
        lock (_fileWriteLock)
        {
            File.AppendAllLines(_fileNames.FileCacheCsv, new[] { entity.CsvEntry });
        }
        var result = GetFileCacheByPath(fileInfo.FullName);
        _logger.LogTrace($"Creating cache entity for {fileInfo.FullName} success: {(result != null)}", LoggerType.FileCache);
        return result;
    }

    // Retrieves the validated file cache entity from a specified file cache entity.
    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        // unsure why we need to do all of this but it may make since later, i dont really know
        // since i dont have any concrete examples to go off here, or anything to really log.
        var resultingFileCache = ReplacePathPrefixes(fileCache);
        //_logger.LogTrace("Validating {path}", fileCache.PrefixedFilePath);
        resultingFileCache = Validate(resultingFileCache);
        return resultingFileCache;
    }

    /// <summary>
    ///     Updates any prefixed file paths from their static placeholders to the 
    ///     local directories are your PC.
    /// </summary>
    /// <returns> The FileCacheEntity with the updated path. </returns>
    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        if (fileCache.PrefixedFilePath.StartsWith(Constants.PrefixPenumbra, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(Constants.PrefixPenumbra, IpcCallerPenumbra.ModDirectory, StringComparison.Ordinal));
        }
        else if (fileCache.PrefixedFilePath.StartsWith(Constants.PrefixCache, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(Constants.PrefixCache, _config.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    // Validates the FileCacheEntity.
    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        // construct a new file using the resolved file path.
        var file = new FileInfo(fileCache.ResolvedFilepath);
        // If the file we are trying to locate is not found on our system, remove the hashed file from the concurrent file-cache dict.
        if (!file.Exists)
        {
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
            return null;
        }

        // Otherwise, if the last write time in ticks does not match the one we have stored, update the hashed file.
        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            // Updates the hashed file. **(Recommend scoping within to see what exactly is 'updated')
            UpdateHashedFile(fileCache);
        }
        // ret the file cache.
        return fileCache;
    }

    // I like your funny words, magic man. (look at later lol)
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting FileCacheManager");

        lock (_fileWriteLock)
        {
            try
            {
                _logger.LogInformation($"Checking for {CsvBakPath}", LoggerType.FileCsv);

                if (File.Exists(CsvBakPath))
                {
                    _logger.LogInformation($"{CsvBakPath} found, moving to {_fileNames.FileCacheCsv}", LoggerType.FileCsv);
                    File.Move(CsvBakPath, _fileNames.FileCacheCsv, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
                try
                {
                    if (File.Exists(CsvBakPath))
                        File.Delete(CsvBakPath);
                }
                catch (Exception ex1)
                {
                    _logger.LogWarning(ex1, "Could not delete bak file");
                }
            }
        }

        if (File.Exists(_fileNames.FileCacheCsv))
        {
            if (IpcCallerPenumbra.APIAvailable && string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory))
                _ipc.Penumbra.CheckModDirectory();

            if (!IpcCallerPenumbra.APIAvailable || string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory))
            {
                _mediator.Publish(new NotificationMessage("Penumbra not connected", "Could not load local file cache data. " +
                    "Penumbra is not connected or not properly set up. Please enable and/or configure Penumbra properly to use Sundeouleia." +
                    "After, reload Sundeouleia in the Plugin installer.", NotificationType.Error));
            }

            _logger.LogInformation($"{_fileNames.FileCacheCsv} found, parsing");

            bool success = false;
            string[] entries = [];
            int attempts = 0;
            while (!success && attempts < 10)
            {
                try
                {
                    _logger.LogInformation($"Attempting to read {_fileNames.FileCacheCsv}", LoggerType.FileCsv);
                    entries = File.ReadAllLines(_fileNames.FileCacheCsv);
                    success = true;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.LogWarning($"Could not open {_fileNames.FileCacheCsv}, trying again: {ex}");
                    Thread.Sleep(100);
                }
            }

            if (!entries.Any())
            {
                _logger.LogWarning($"Could not load entries from {_fileNames.FileCacheCsv}, continuing with empty file cache");
            }

            _logger.LogInformation($"Found {entries.Length} files in {_fileNames.FileCacheCsv}", LoggerType.FileCsv);

            Dictionary<string, bool> processedFiles = new(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                var splittedEntry = entry.Split(Constants.CsvSplit, StringSplitOptions.None);
                try
                {
                    var hash = splittedEntry[0];
                    if (hash.Length != Constants.Blake3HashLength) 
                        throw new InvalidOperationException($"Expected Hash length of {Constants.Blake3HashLength}, received {hash.Length}");
                    
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];

                    if (processedFiles.ContainsKey(path))
                    {
                        _logger.LogWarning($"Already processed {path}, ignoring");
                        continue;
                    }

                    processedFiles.Add(path, value: true);

                    long size = -1;
                    long compressed = -1;
                    if (splittedEntry.Length > 3)
                    {
                        if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out long result))
                        {
                            size = result;
                        }
                        if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out long resultCompressed))
                        {
                            compressed = resultCompressed;
                        }
                    }
                    AddHashedFile(ReplacePathPrefixes(new FileCacheEntity(hash, path, time, size, compressed)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to initialize entry {entry}, ignoring: {ex}");
                }
            }

            if (processedFiles.Count != entries.Length)
            {
                WriteOutFullCsv();
            }
        }

        _logger.LogInformation("Started FileCacheManager");
        return Task.CompletedTask;
    }

    // Upon the plugin stopping, write out the full CSV for the current state.
    public Task StopAsync(CancellationToken cancellationToken)
    {
        WriteOutFullCsv();
        return Task.CompletedTask;
    }
}