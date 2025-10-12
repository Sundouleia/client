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
    private readonly FileTransferService _service;

    private readonly List<ThrottledStream> _activeDownloadStreams;
    private readonly Dictionary<string, FileDownloadStatus> _downloadStatus;

    public FileDownloader(ILogger<FileDownloader> logger, SundouleiaMediator mediator,
        FileCompactor compactor, FileCacheManager manager, FileTransferService service)
        : base(logger, mediator)
    {
        _compactor = compactor;
        _dbManager = manager;
        _service = service;

        _activeDownloadStreams = [];
        _downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any())
                return;

            var newLimit = _service.DownloadLimitPerSlot();
            Logger.LogTrace($"Setting new Download Speed Limit to {newLimit}");
            foreach (var stream in _activeDownloadStreams)
                stream.BandwidthLimit = newLimit;
        });
    }

    // was DownloadFileTransfer
    public List<string> CurrentDownloads { get; private set; } = [];
    public bool IsDownloading => CurrentDownloads.Count > 0;

    public void ClearDownload()
    {
        CurrentDownloads.Clear();
        _downloadStatus.Clear();
    }

    // I hate the parameters in this, try to simplify this file to a degree.
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

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
        foreach (var stream in _activeDownloadStreams.ToList())
            Generic.Safe(stream.Dispose);
        base.Dispose(disposing);
    }

    //private static (string fileHash, long fileLengthBytes) ReadBlockFileHeader(FileStream fileBlockStream)
    //{
    //    List<char> hashName = [];
    //    List<char> fileLength = [];
    //    var separator = (char)MungeByte(fileBlockStream.ReadByte());
    //    if (separator != '#') throw new InvalidDataException("Data is invalid, first char is not #");

    //    bool readHash = false;
    //    while (true)
    //    {
    //        int readByte = fileBlockStream.ReadByte();
    //        if (readByte == -1)
    //            throw new EndOfStreamException();

    //        var readChar = (char)MungeByte(readByte);
    //        if (readChar == ':')
    //        {
    //            readHash = true;
    //            continue;
    //        }
    //        if (readChar == '#') break;
    //        if (!readHash) hashName.Add(readChar);
    //        else fileLength.Add(readChar);
    //    }
    //    return (string.Join("", hashName), long.Parse(string.Join("", fileLength)));
    //}

    //public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    //{
    //    Logger.LogDebug("Download start: {id}", gameObjectHandler.Name);

    //    List<DownloadFileDto> downloadFileInfoFromService =
    //    [
    //        .. await FilesGetSizes(fileReplacement.Select(f => f.Hash).Distinct(StringComparer.Ordinal).ToList(), ct).ConfigureAwait(false),
    //    ];

    //    Logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

    //    foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
    //    {
    //        if (!_transferService.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
    //        {
    //            _transferService.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
    //        }
    //    }

    //    CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
    //        .Where(d => d.CanBeTransferred).ToList();

    //    return CurrentDownloads;
    //}

    // I give up trying to digest this, my battery is fucking spent.
    private async Task DownloadFilesInternal(PlayerHandler handler, List<VerifiedModFile> moddedFiles, CancellationToken ct)
    {
        var downloadGroups = CurrentDownloads.GroupBy(f => f.DownloadUri.Host + ":" + f.DownloadUri.Port, StringComparer.Ordinal);

        foreach (var downloadGroup in downloadGroups)
        {
            _downloadStatus[downloadGroup.Key] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = downloadGroup.Sum(c => c.Total),
                TotalFiles = 1,
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

        await Parallel.ForEachAsync(downloadGroups, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadGroups.Count(),
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            // let server predownload files
            var requestIdResponse = await _transferService.SendRequestAsync(HttpMethod.Post, SundeouleiaFiles.RequestEnqueueFullPath(fileGroup.First().DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);
            Logger.LogDebug("Sent request for {n} files on server {uri} with result {result}", fileGroup.Count(), fileGroup.First().DownloadUri,
                await requestIdResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));

            Guid requestId = Guid.Parse((await requestIdResponse.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim('"'));

            Logger.LogDebug("GUID {requestId} for {n} files on server {uri}", requestId, fileGroup.Count(), fileGroup.First().DownloadUri);

            var blockFile = _fileDbManager.GetCacheFilePath(requestId.ToString("N"), "blk");
            FileInfo fi = new(blockFile);
            try
            {
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
                await _transferService.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForQueue;
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    try
                    {
                        if (!_downloadStatus.TryGetValue(fileGroup.Key, out FileDownloadStatus? value)) return;
                        value.TransferredBytes += bytesDownloaded;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Could not set download progress");
                    }
                });
                await DownloadAndMungeFileHttpClient(fileGroup.Key, requestId, [.. fileGroup], blockFile, progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("{dlName}: Detected cancellation of download, partially extracting files for {id}", fi.Name, gameObjectHandler);
            }
            catch (Exception ex)
            {
                _transferService.ReleaseDownloadSlot();
                File.Delete(blockFile);
                Logger.LogError(ex, "{dlName}: Error during download of {id}", fi.Name, requestId);
                ClearDownload();
                return;
            }

            FileStream? fileBlockStream = null;
            try
            {
                if (_downloadStatus.TryGetValue(fileGroup.Key, out var status))
                {
                    status.TransferredFiles = 1;
                    status.DownloadStatus = DownloadStatus.Decompressing;
                }
                fileBlockStream = File.OpenRead(blockFile);
                while (fileBlockStream.Position < fileBlockStream.Length)
                {
                    (string fileHash, long fileLengthBytes) = ReadBlockFileHeader(fileBlockStream);

                    try
                    {
                        var fileExtension = fileReplacement.First(f => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
                        var filePath = _fileDbManager.GetCacheFilePath(fileHash, fileExtension);
                        Logger.LogDebug("{dlName}: Decompressing {file}:{le} => {dest}", fi.Name, fileHash, fileLengthBytes, filePath);

                        byte[] compressedFileContent = new byte[fileLengthBytes];
                        var readBytes = await fileBlockStream.ReadAsync(compressedFileContent, CancellationToken.None).ConfigureAwait(false);
                        if (readBytes != fileLengthBytes)
                        {
                            throw new EndOfStreamException();
                        }
                        MungeBuffer(compressedFileContent);

                        var decompressedFile = LZ4Wrapper.Unwrap(compressedFileContent);
                        await _fileCompactor.WriteAllBytesAsync(filePath, decompressedFile, CancellationToken.None).ConfigureAwait(false);

                        PersistFileToStorage(fileHash, filePath);
                    }
                    catch (EndOfStreamException)
                    {
                        Logger.LogWarning("{dlName}: Failure to extract file {fileHash}, stream ended prematurely", fi.Name, fileHash);
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning(e, "{dlName}: Error during decompression", fi.Name);
                    }
                }
            }
            catch (EndOfStreamException)
            {
                Logger.LogDebug("{dlName}: Failure to extract file header data, stream ended", fi.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{dlName}: Error during block file read", fi.Name);
            }
            finally
            {
                _transferService.ReleaseDownloadSlot();
                if (fileBlockStream != null)
                    await fileBlockStream.DisposeAsync().ConfigureAwait(false);
                File.Delete(blockFile);
            }
        }).ConfigureAwait(false);

        Logger.LogDebug("Download end: {id}", gameObjectHandler);

        ClearDownload();
    }

    //private async Task WaitForDownloadReady(List<DownloadFileTransfer> downloadFileTransfer, CancellationToken downloadCt)
    //{
    //    bool alreadyCancelled = false;
    //    try
    //    {
    //        CancellationTokenSource localTimeoutCts = new();
    //        localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
    //        CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);

    //        while (!_transferService.IsDownloadReady(requestId))
    //        {
    //            try
    //            {
    //                await Task.Delay(250, composite.Token).ConfigureAwait(false);
    //            }
    //            catch (TaskCanceledException)
    //            {
    //                if (downloadCt.IsCancellationRequested) throw;

    //                var req = await _transferService.SendRequestAsync(HttpMethod.Get, SundeouleiaFiles.RequestCheckQueueFullPath(downloadFileTransfer[0].DownloadUri, requestId),
    //                    downloadFileTransfer.Select(c => c.Hash).ToList(), downloadCt).ConfigureAwait(false);
    //                req.EnsureSuccessStatusCode();
    //                localTimeoutCts.Dispose();
    //                composite.Dispose();
    //                localTimeoutCts = new();
    //                localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
    //                composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
    //            }
    //        }

    //        localTimeoutCts.Dispose();
    //        composite.Dispose();

    //        Logger.LogDebug("Download {requestId} ready", requestId);
    //    }
    //    catch (TaskCanceledException)
    //    {
    //        try
    //        {
    //            await _transferService.SendRequestAsync(HttpMethod.Get, SundeouleiaFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
    //            alreadyCancelled = true;
    //        }
    //        catch
    //        {
    //            // ignore whatever happens here
    //        }

    //        throw;
    //    }
    //    finally
    //    {
    //        if (downloadCt.IsCancellationRequested && !alreadyCancelled)
    //        {
    //            try
    //            {
    //                await _transferService.SendRequestAsync(HttpMethod.Get, SundeouleiaFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
    //            }
    //            catch
    //            {
    //                // ignore whatever happens here
    //            }
    //        }
    //        _transferService.ClearDownloadRequest(requestId);
    //    }
    //}

    //private async Task DownloadAndMungeFileHttpClient(string downloadGroup, Guid requestId, List<DownloadFileTransfer> fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    //{
    //    Logger.LogDebug("GUID {requestId} on server {uri} for files {files}", requestId, fileTransfer[0].DownloadUri, string.Join(", ", fileTransfer.Select(c => c.Hash).ToList()));

    //    await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

    //    _downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;

    //    HttpResponseMessage response = null!;
    //    var requestUrl = SundeouleiaFiles.CacheGetFullPath(fileTransfer[0].DownloadUri, requestId);

    //    Logger.LogDebug("Downloading {requestUrl} for request {id}", requestUrl, requestId);
    //    try
    //    {
    //        response = await _transferService.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    //        response.EnsureSuccessStatusCode();
    //    }
    //    catch (HttpRequestException ex)
    //    {
    //        Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
    //        if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
    //        {
    //            throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
    //        }
    //    }

    //    ThrottledStream? stream = null;
    //    try
    //    {
    //        var fileStream = File.Create(tempPath);
    //        await using (fileStream.ConfigureAwait(false))
    //        {
    //            var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 65536 : 8196;
    //            var buffer = new byte[bufferSize];

    //            var bytesRead = 0;
    //            var limit = _transferService.DownloadLimitPerSlot();
    //            Logger.LogTrace("Starting Download of {id} with a speed limit of {limit} to {tempPath}", requestId, limit, tempPath);
    //            stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);
    //            _activeDownloadStreams.Add(stream);
    //            while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
    //            {
    //                ct.ThrowIfCancellationRequested();

    //                MungeBuffer(buffer.AsSpan(0, bytesRead));

    //                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

    //                progress.Report(bytesRead);
    //            }

    //            Logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, tempPath);
    //        }
    //    }
    //    catch (OperationCanceledException)
    //    {
    //        throw;
    //    }
    //    catch (Exception ex)
    //    {
    //        try
    //        {
    //            if (!tempPath.IsNullOrEmpty())
    //                File.Delete(tempPath);
    //        }
    //        catch
    //        {
    //            // ignore if file deletion fails
    //        }
    //        throw;
    //    }
    //    finally
    //    {
    //        if (stream != null)
    //        {
    //            _activeDownloadStreams.Remove(stream);
    //            await stream.DisposeAsync().ConfigureAwait(false);
    //        }
    //    }
    //}

    //private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    //{
    //    if (!_transferService.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
    //    var response = await _transferService.SendRequestAsync(HttpMethod.Get, SundeouleiaFiles.ServerFilesGetSizesFullPath(_transferService.FilesCdnUri!), hashes, ct).ConfigureAwait(false);
    //    return await response.Content.ReadFromJsonAsync<List<DownloadFileDto>>(cancellationToken: ct).ConfigureAwait(false) ?? [];
    //}

    //private void PersistFileToStorage(string fileHash, string filePath)
    //{
    //    var fi = new FileInfo(filePath);
    //    Func<DateTime> RandomDayInThePast()
    //    {
    //        DateTime start = new(1995, 1, 1, 1, 1, 1, DateTimeKind.Local);
    //        Random gen = new();
    //        int range = (DateTime.Today - start).Days;
    //        return () => start.AddDays(gen.Next(range));
    //    }

    //    fi.CreationTime = RandomDayInThePast().Invoke();
    //    fi.LastAccessTime = DateTime.Today;
    //    fi.LastWriteTime = RandomDayInThePast().Invoke();
    //    try
    //    {
    //        var entry = _fileDbManager.CreateCacheEntry(filePath);
    //        if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
    //        {
    //            Logger.LogError("Hash mismatch after extracting, got {hash}, expected {expectedHash}, deleting file", entry.Hash, fileHash);
    //            File.Delete(filePath);
    //            _fileDbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Logger.LogWarning(ex, "Error creating cache entry");
    //    }
    //}
}