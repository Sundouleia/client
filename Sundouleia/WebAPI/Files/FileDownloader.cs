using CkCommons;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;

namespace Sundouleia.WebAPI.Files;

// Handles downloading files off the sundouleia servers, provided a valid authorized download link.
// I hope to god i get help digesting how to get this working with progressable file streams and appropriate integration with the new file host because
// as of right now my brain is fried, and I am tired of making all of this code just to make something a reality for people.
public class FileDownloader : DisposableMediatorSubscriberBase
{
    private readonly FileCompactor _compactor;
    private readonly FileCacheManager _dbManager;
    private readonly FileTransferService _transferService;

    private readonly Lock _activeDownloadStreamsLock = new();
    private readonly List<ThrottledStream> _activeDownloadStreams;
    private readonly ConcurrentDictionary<PlayerHandler, FileTransferProgress> _downloadStatus;
    private readonly ConcurrentDictionary<PlayerHandler, ConcurrentQueue<Task>> _downloadTasks = new();
    private readonly TaskDeduplicator<string> _downloadDeduplicator = new();
    private readonly TaskDeduplicator<PlayerHandler> _waiterDeduplicator = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();


    // TODO:
    // ---------
    // Structure needs to be migrated from a task awaited downloader to some kind of task worker or enqueued downloader.
    // ---------
    // 1. Needs a method for sundesmos to enqueue verified mod files for downloads,
    // this could be awaitable, but should not await the download itself.
    // 2. Needs a way for the pair to know when all hashes they are currently processing for downloading, are finished downloading.
    // 2b. This task should be awaitable, with a cancellation token passed in if possible. The token is to help cancel any awaits if multiple
    // ApplyMods() functions are called in the PlayerHandler, so that we can ensure we are only applying the mods once all downloads are complete.
    // 2c. Alternatively, we could also remove files from the fileTransferProgress as they are completed or have a way to easily fetch which
    // hashes are finished downloading and which are not, so we know which mods to add and which to not add when applying, but this could lead to extra work.
    // 3. DownloadFinished mediator call should update only once all files are downloaded, and should DownloadBegin fired whenever creating a new FileTransferProgress item.
    // ---------
    // This can be handled via a ConcurrentQueue or Task worker ConcurrentDictionary, but should account for handling downloads that could be
    // requested by multiple people (with the same download links to the same data) to avoid duplicate downloads.

    public FileDownloader(ILogger<FileDownloader> logger, SundouleiaMediator mediator,
        FileCompactor compactor, FileCacheManager manager, FileTransferService service)
        : base(logger, mediator)
    {
        _compactor = compactor;
        _dbManager = manager;
        _transferService = service;

        _activeDownloadStreams = [];
        _downloadStatus = new ConcurrentDictionary<PlayerHandler, FileTransferProgress>();

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any())
                return;

            var newLimit = _transferService.DownloadLimitPerSlot();
            Logger.LogTrace($"Setting new Download Speed Limit to {newLimit}");
            lock (_activeDownloadStreamsLock)
            {
                foreach (var stream in _activeDownloadStreams)
                    stream.BandwidthLimit = newLimit;
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        _cancellationTokenSource.Cancel();
        lock (_activeDownloadStreamsLock)
        {
            foreach (var stream in _activeDownloadStreams.ToList())
                Generic.Safe(stream.Dispose);
        }
        base.Dispose(disposing);
    }

    private void ClearDownloadStatusForPlayer(PlayerHandler handler, List<VerifiedModFile> files)
    {
        foreach (var file in files)
            _downloadStatus[handler].RemoveFile(file.Hash);
    }

    /// <summary>
    ///     Downloads a batch of files using authorized download links provided by Sundouleia's FileHost.
    /// </summary>
    public void BeginDownloads(PlayerHandler handler, List<VerifiedModFile> moddedFiles)
    {
        // create the progress tracking data for this player
        var dlStatus = _downloadStatus.GetOrAdd(handler, new FileTransferProgress());

        Logger.LogDebug($"Download begin for: {handler.NameString}", LoggerType.FileDownloads);

        var taskQueue = _downloadTasks.GetOrAdd(handler, new ConcurrentQueue<Task>());
        foreach (var modFile in moddedFiles)
        {
            // register the file for progress tracking
            dlStatus.AddOrUpdateFile(modFile.Hash, 0);

            // begin or get existing download task
            Logger.LogDebug($"Enqueuing download task for: {modFile.Hash} for player {handler.NameString}", LoggerType.FileDownloads);
            taskQueue.Enqueue(_downloadDeduplicator.GetOrBeginTask(modFile.Hash, () =>
                DownloadFileInternal(dlStatus, modFile, _cancellationTokenSource.Token)));
        }

        // Inform the download UI that there are download(s) starting, passing in the status dictionary.
        Mediator.Publish(new FileDownloadStarted(handler, dlStatus));

        // start a waiter task to await all downloads for this player
        _waiterDeduplicator.GetOrBeginTask(handler, async () =>
        {
            Logger.LogDebug($"Starting download waiter for: {handler.NameString}", LoggerType.FileDownloads);

            // This is the only place we dequeue tasks (thanks to _waiterDeduplicator), which makes this combination of IsEmpty + TryDequeue safe.
            while (!taskQueue.IsEmpty)
            {
                if (taskQueue.TryDequeue(out var downloadTask))
                {
                    try
                    {
                        await downloadTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error in download task for {handler.NameString}: {ex}", LoggerType.FileDownloads);
                    }
                }
                else
                {
                    Logger.LogWarning($"Failed to dequeue download task for: {handler.NameString}", LoggerType.FileDownloads);
                }
            }

            Logger.LogDebug($"All downloads completed for: {handler.NameString}", LoggerType.FileDownloads);
            ClearDownloadStatusForPlayer(handler, moddedFiles);
            Mediator.Publish(new FileDownloadComplete(handler));
        });
    }

    /// <summary>
	///    Waits for all downloads for the specified player to complete.
	/// </summary>
	/// <param name="handler"></param>
	/// <returns></returns>
    public Task WaitForDownloadsToComplete(PlayerHandler handler)
    {
        if (_waiterDeduplicator.TryGetTask(handler, out var task))
            return task;
        return Task.CompletedTask;
    }

    private async Task DownloadFileInternal(FileTransferProgress dlStatus, VerifiedModFile modFile, CancellationToken cancelToken)
    {
        try
        {
            // await for an available download slot
            await _transferService.WaitForDownloadSlotAsync(cancelToken).ConfigureAwait(false);

            // check for cancellation, as download slot wait could be long depending on the number and size of requested downloads
            cancelToken.ThrowIfCancellationRequested();
            Logger.LogDebug($"Download start: {modFile.Hash}", LoggerType.FileDownloads);

            // download the file via transfer service
            var downloadResponse = await _transferService.SendRequestAsync(HttpMethod.Get, new Uri(modFile.Link), cancelToken, HttpCompletionOption.ResponseHeadersRead);
            downloadResponse.EnsureSuccessStatusCode();

            // get total size for progress tracking
            if (downloadResponse.Headers.Contains("X-File-Size") && long.TryParse(downloadResponse.Headers.GetValues("X-File-Size").FirstOrDefault(), out var size))
            {
                dlStatus.AddOrUpdateFile(modFile.Hash, size);
            }

            // create progress reporter for this player
            Progress<long> progress = CreateProgressReporter(dlStatus, modFile.Hash);

            // set up throttled stream for download
            using var downloadStream = await downloadResponse.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
            using var throttledStream = new ThrottledStream(downloadStream, _transferService.DownloadLimitPerSlot());
            lock (_activeDownloadStreamsLock)
                _activeDownloadStreams.Add(throttledStream);

            // this try block exists to ensure we always remove the stream from active downloads
            try
            {
                // download to temp file first, then move to final location
                var fileExtension = modFile.GamePaths[0].Split(".")[^1]; // maybe pass extension in the (Verified)ModFile dto:s?
                var filePath = _dbManager.GetCacheFilePath(modFile.Hash, fileExtension);
                var tempFilePath = filePath + ".part";

                // clean up any existing temp file that could be left over, if the game crashed or was closed mid-download
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);

                using (var fileStream = File.Create(tempFilePath))
                {
                    // copy from the http stream to temp file with progress tracking
                    await throttledStream.CopyToAsync(fileStream, progress, cancelToken);
                    await fileStream.FlushAsync(cancelToken).ConfigureAwait(false);
                }

                // move temp file to final location
                File.Move(tempFilePath, filePath, true);
                PersistFileToStorage(modFile.Hash, filePath);

                // compact the file if needed
                _compactor.CompactFileSafe(filePath);

                // mark file download as completed
                dlStatus.MarkFileCompleted(modFile.Hash);
                Logger.LogDebug($"Downloaded file {modFile.Hash} to {filePath}", LoggerType.FileDownloads);
            }
            finally
            {
                lock (_activeDownloadStreamsLock)
                    _activeDownloadStreams.Remove(throttledStream);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogDebug($"Detected cancellation of download for {modFile.Hash}", LoggerType.FileDownloads);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during download of {modFile.Hash}: {ex}", LoggerType.FileDownloads);
            return;
        }
        finally
        {
            _transferService.ReleaseDownloadSlot();
        }

        Logger.LogDebug($"Download end: {modFile.Hash}", LoggerType.FileDownloads);
    }

    private Progress<long> CreateProgressReporter(FileTransferProgress dlStatus, string hash)
    {
        return new((bytesDownloaded) =>
        {
            try
            {
                dlStatus.AddFileProgress(hash, bytesDownloaded);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not set download progress: {ex}", LoggerType.FileDownloads);
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

    /// <summary>
    ///     I hate the parameters in this, try to simplify this file to a degree.
    /// </summary>
    public List<VerifiedModFile> GetExistingFromCache(Dictionary<string, VerifiedModFile> replacements, out Dictionary<string, string> moddedDict, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var missing = new ConcurrentBag<VerifiedModFile>();
        var outputDict = new ConcurrentDictionary<string, string>();
        moddedDict = [];
        // Update the csv if the FilePaths extension has changed.
        bool hasMigrationChanges = false;

        try
        {
            // Flatten the current replacements that do not have a file replacement path.
            var replacementList = replacements.Values.Where(vmf => string.IsNullOrEmpty(vmf.SwappedPath)).ToList();
            // Run in parallel to speed up download preparation and lookup time.
            Parallel.ForEach(replacementList, new ParallelOptions()
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = 4,
            },
            (modItem) =>
            {
                ct.ThrowIfCancellationRequested();
                // Attempt to locate the path of this file hash in our personal cache
                if (_dbManager.GetFileCacheByHash(modItem.Hash) is { } fileCache)
                {
                    Logger.LogTrace($"Found file in cache: {modItem.Hash} -> {fileCache.ResolvedFilepath}", LoggerType.PairMods);
                    // Then attempt to fetch the file information via the resolved FilePath+Extension.
                    // If the FileHash matched, but there was no FileInfo with the extension, we need to migrate it.
                    if (string.IsNullOrEmpty(new FileInfo(fileCache.ResolvedFilepath).Extension))
                    {
                        hasMigrationChanges = true;
                        fileCache = _dbManager.MigrateFileHashToExtension(fileCache, modItem.GamePaths[0].Split(".")[^1]);
                    }

                    // Otherwise, append the game paths to the modded dictionary.
                    foreach (var gamePath in modItem.GamePaths)
                        outputDict[gamePath] = fileCache.ResolvedFilepath;
                }
                else
                {
                    Logger.LogTrace($"Missing file: {modItem.Hash}", LoggerType.PairMods);
                    missing.Add(modItem);
                }
            });

            // Convert the output dictionary into a modded dictionary.
            moddedDict = outputDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
            // Iterate again to check for any file swaps. These should take precedence in path replacement.
            foreach (var item in replacements.Values.Where(vmf => !string.IsNullOrEmpty(vmf.SwappedPath)).ToList())
            {
                foreach (var gamePath in item.GamePaths)
                {
                    Logger.LogTrace($"Adding file swap for {gamePath}: {item.SwappedPath}", LoggerType.PairMods);
                    moddedDict[gamePath] = item.SwappedPath;
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
        Logger.LogDebug($"ModdedPaths calculated in {sw.ElapsedMilliseconds}ms, missing files: {missing.Count}, total files: {moddedDict.Count}", LoggerType.PairMods);
        return [.. missing];
    }

}