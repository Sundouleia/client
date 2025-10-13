using CkCommons;
using K4os.Compression.LZ4.Legacy;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files;

// Handles downloading files off the sundouleia servers, provided a valid authorized download link.
// I hope to god i get help digesting how to get this working with progressable file streams and appropriate integration with the new file host because
// as of right now my brain is fried, and I am tired of making all of this code just to make something a reality for people.
public partial class FileDownloader : DisposableMediatorSubscriberBase
{
    private readonly FileCompactor _compactor;
    private readonly FileCacheManager _dbManager;
    private readonly FileTransferService _transferService;

    private readonly List<ThrottledStream> _activeDownloadStreams;
    private readonly ConcurrentDictionary<string, FileTransferProgress> _downloadStatus;

    public FileDownloader(ILogger<FileDownloader> logger, SundouleiaMediator mediator,
        FileCompactor compactor, FileCacheManager manager, FileTransferService service)
        : base(logger, mediator)
    {
        _compactor = compactor;
        _dbManager = manager;
        _transferService = service;

        _activeDownloadStreams = [];
        _downloadStatus = new ConcurrentDictionary<string, FileTransferProgress>(StringComparer.Ordinal);

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any())
                return;

            var newLimit = _transferService.DownloadLimitPerSlot();
            Logger.LogTrace($"Setting new Download Speed Limit to {newLimit}");
            foreach (var stream in _activeDownloadStreams)
                stream.BandwidthLimit = newLimit;
        });
    }

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
        foreach (var stream in _activeDownloadStreams.ToList())
            Generic.Safe(stream.Dispose);
        base.Dispose(disposing);
    }

    public void ClearDownload() => _downloadStatus.Clear();

    /// <summary>
    ///     I hate the parameters in this, try to simplify this file to a degree.
    /// </summary>
    public List<VerifiedModFile> GetExistingFromCache(Dictionary<string, VerifiedModFile> replacements, out Dictionary<(string GamePath, string? Hash), string> validDict, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var missing = new List<VerifiedModFile>();

        validDict = [];

        // Something about migration idk find out later.
        bool hasMigrationChanges = false;

        try
        {
            // Grab the current expected mod files. For these, we should check in parallel if cached, then place them in the outputDict or missing bag respectively.
            foreach (var item in replacements.Values.Where(vmf => string.IsNullOrEmpty(vmf.SwappedPath)))
            {
                ct.ThrowIfCancellationRequested();
                // Attempt to locate the path of this file hash in our personal cache
                if (_dbManager.GetFileCacheByHash(item.Hash) is { } fileCache)
                {
                    // Then attempt to fetch the file information via the resolved FilePath+Extension.
                    // If the FileHash matched, but there was no FileInfo with the extension, we need to migrate it.
                    if (string.IsNullOrEmpty(new FileInfo(fileCache.ResolvedFilepath).Extension))
                    {
                        hasMigrationChanges = true;
                        fileCache = _dbManager.MigrateFileHashToExtension(fileCache, item.GamePaths[0].Split(".")[^1]);
                    }

                    // Otherwise append the resolved paths into the modded dictionary. (maybe revise later)
                    foreach (var gamePath in item.GamePaths)
                        validDict[(gamePath, item.Hash)] = fileCache.ResolvedFilepath;
                }
                else
                {
                    Logger.LogTrace($"Missing file: {item.Hash}", LoggerType.PairFileCache);
                    missing.Add(item);
                }
            }

            // Run a second check but for all file swap paths?... Idk honestly, the file swap -vs- no file swap is confusing because it seems to remove all before sending.
            foreach (var item in replacements.Values.Where(vmf => !string.IsNullOrEmpty(vmf.SwappedPath)).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace($"Adding file swap for {gamePath}: {item.SwappedPath}");
                    validDict[(gamePath, item.Hash)] = item.SwappedPath;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error while grabbing existing files from the cache: {ex}");
        }

        // If there were migration changes we should re-write out the full csv.
        if (hasMigrationChanges)
            _dbManager.WriteOutFullCsv();

        sw.Stop();
        Logger.LogDebug($"ModdedPaths calculated in {sw.ElapsedMilliseconds}ms, missing files: {missing.Count}, total files: {validDict.Count}");
        return missing;
    }

    /// <summary>
    ///     Downloads a batch of files using authorized download links provided by Sundouleia's FileHost.
    /// </summary>
    public async Task DownloadFiles(PlayerHandler handler, List<VerifiedModFile> fileReplacementDto, CancellationToken ct)
    {
        try
        {
            await DownloadFilesInternal(handler, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            ClearDownload();
        }
        finally
        {
            Mediator.Publish(new FileDownloadComplete(handler));
        }
    }

    private async Task DownloadFilesInternal(PlayerHandler handler, List<VerifiedModFile> moddedFiles, CancellationToken ct)
    {
        // Init the download status for each of the files.
        foreach (var v in moddedFiles)
            _downloadStatus[v.Hash] = new FileTransferProgress(0, 0);

        // Inform the download UI that there are download(s) starting, passing in the status dictionary.
        Mediator.Publish(new FileDownloadStarted(handler, _downloadStatus));

        // Begin the parallel downloads, managed via the download slots.
        await Parallel.ForEachAsync(moddedFiles, new ParallelOptions()
        {
            MaxDegreeOfParallelism = Math.Min(moddedFiles.Count, 10), // 1-10 parallel downloads
            CancellationToken = ct,
        },
        async (modFile, cancelToken) =>
        {
            try
            {
                // await for an available download slot
                await _transferService.WaitForDownloadSlotAsync(cancelToken).ConfigureAwait(false);
                // create progress reporter for this file
                Progress<long> progress = CreateProgressReporter(modFile.Hash);

                // download the file via transfer service
                var downloadResponse = await _transferService.SendRequestAsync(HttpMethod.Get, new Uri(modFile.Link), cancelToken, HttpCompletionOption.ResponseHeadersRead);
                downloadResponse.EnsureSuccessStatusCode();

                // get total size for progress tracking
                var totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
                _downloadStatus[modFile.Hash] = new FileTransferProgress(0, totalSize);

                // set up throttled stream for download
                using var downloadStream = await downloadResponse.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
                using var throttledStream = new ThrottledStream(downloadStream, _transferService.DownloadLimitPerSlot());
                _activeDownloadStreams.Add(throttledStream);

                try
                {
                    // download to temp file first, then move to final location
                    var fileExtension = modFile.GamePaths[0].Split(".")[^1]; // maybe pass extension in the (Verified)ModFile dto:s?
                    var filePath = _dbManager.GetCacheFilePath(modFile.Hash, fileExtension);
                    var tempFilePath = filePath + ".part";
                    using var fileStream = File.Create(tempFilePath);

                    // copy from the http stream to temp file with progress tracking
                    await throttledStream.CopyToAsync(fileStream, progress, cancelToken);
                    await fileStream.FlushAsync(cancelToken).ConfigureAwait(false);

                    // move temp file to final location
                    File.Move(tempFilePath, filePath, true);
                    PersistFileToStorage(modFile.Hash, filePath);
                    Logger.LogDebug($"Downloaded file {modFile.Hash} to {filePath}", LoggerType.FileDownloads);
                }
                finally
                {
                    _activeDownloadStreams.Remove(throttledStream);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug($"Detected cancellation of download for {modFile.Hash}", LoggerType.FileDownloads);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during download of {modFile.Hash}: {ex}");
                return;
            }
            finally
            {
                _transferService.ReleaseDownloadSlot();
                ClearDownload();
            }
        }).ConfigureAwait(false);

        Logger.LogDebug($"Download end: {handler}", LoggerType.FileDownloads);

        ClearDownload();
    }

    private Progress<long> CreateProgressReporter(string fileHash)
    {
        return new((bytesDownloaded) =>
        {
            try
            {
                if (!_downloadStatus.TryGetValue(fileHash, out FileTransferProgress? value)) return;
                _downloadStatus[fileHash] = value with { Transferred = value.Transferred + bytesDownloaded };
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not set download progress: {ex}");
            }
        });
    }

    private void PersistFileToStorage(string fileHash, string filePath)
    {
        try
        {
            var entry = _dbManager.CreateCacheEntry(filePath);
            if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogError($"Hash mismatch after extracting, got {entry.Hash}, expected {fileHash}, deleting file");
                File.Delete(filePath);
                _dbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error creating cache entry: {ex}");
        }
    }
}