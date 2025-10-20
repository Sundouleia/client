using CkCommons;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.ModFiles.Cache;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModFiles;

public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly FileCompactor _compactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly SundouleiaWatcher _mainWatcher;
    private readonly PenumbraWatcher _penumbraWatcher;

    private long _currentFileProgress = 0;
    private readonly CancellationTokenSource _cacheSizeCheckCTS = new();
    private CancellationTokenSource _cacheScanCTS = new();
    private TimeSpan _lastScanTime = TimeSpan.Zero;
    private TimeSpan _lastCacheWriteTime = TimeSpan.Zero;

    public CacheMonitor(ILogger<CacheMonitor> logger, SundouleiaMediator mediator, MainConfig config,
        FileCompactor fileCompactor, FileCacheManager fileDbManager,
        SundouleiaWatcher mainWatcher, PenumbraWatcher penumbraWatcher)
        : base(logger, mediator)
    {
        _config = config;
        _compactor = fileCompactor;
        _fileDbManager = fileDbManager;
        _mainWatcher = mainWatcher;
        _penumbraWatcher = penumbraWatcher;

        // Make sure we start our watchers when 
        Mediator.Subscribe<PenumbraInitialized>(this, (_) =>
        {
            _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);
            _mainWatcher.StartWatcher(config.Current.CacheFolder);
            InvokeScan();
        });
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _mainWatcher.StartWatcher(config.Current.CacheFolder);
            _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);
            InvokeScan();
        });
        Mediator.Subscribe<PenumbraDirectoryChanged>(this, (msg) =>
        {
            _penumbraWatcher.StartWatcher(msg.NewDirectory);
            InvokeScan();
        });

        // If penumbra's API is available and has a valid mod directory by the time this constructor runs, start the watcher.
        if (IpcCallerPenumbra.APIAvailable && !string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory))
            _penumbraWatcher.StartWatcher(IpcCallerPenumbra.ModDirectory);

        // If the cache is valid we can start the watcher immidiately.
        if (config.HasValidCacheFolderSetup())
        {
            _mainWatcher.StartWatcher(config.Current.CacheFolder);
            InvokeScan();
        }

        // Run a periodic check on the sundouleia cache size.
        _ = Task.Run(CacheSizeCheckTask, _cacheSizeCheckCTS.Token);
    }

    // various helper properties. (Mostly stuff to grab for the UI Display)
    public int ScannedCacheEntities => _fileDbManager.TotalCacheEntities;
    public long FileCacheSize { get; set; }
    public long FileCacheDriveFree { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new(StringComparer.Ordinal); // Fizzle this out.
    public bool IsScanRunning => _currentFileProgress > 0 || TotalFiles > 0;
    public string ScanProgressString => IsScanRunning ? $"{_currentFileProgress}/{TotalFiles}" : string.Empty;
    public long TotalFiles { get; private set; }
    public long TotalFilesStorage { get; private set; }
    public bool StorageIsNTFS => _mainWatcher.StorageisNTFS;
    public string? PenumbraPath => _penumbraWatcher.Watcher?.Path;
    public string? SundeouleiaPath => _mainWatcher.Watcher?.Path;
    public string LastScanReadStr => _lastScanTime.ToString(@"s's 'fff'ms'");
    public string LastScanWriteStr => _lastCacheWriteTime.ToString(@"s's 'fff'ms'");

    /// <summary>
    ///     Preiodically checks the size of the cache. <para />
    ///     Ensure that recalculation occurs off the main update thread.
    /// </summary>
    private async Task CacheSizeCheckTask()
    {
        Logger.LogInformation("Starting Periodic Storage Directory Calculation Task");
        var ct = _cacheSizeCheckCTS.Token;
        while (!ct.IsCancellationRequested)
        {
            await Generic.Safe(async () =>
            {
                // Wait for the framework update thread to occur,
                while (Svc.Framework.IsInFrameworkUpdateThread && !ct.IsCancellationRequested)
                    await Task.Delay(1).ConfigureAwait(false);
                
                // then calculate the size outside of it.
                RecalculateFileCacheSize(ct);

            }, true);
            // Await the next check for another minute.
            await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     The inner task called by <see cref="InvokeScan"/> that processes
    ///     Sundouleia's current file cache directory size.
    /// </summary>
    public void RecalculateFileCacheSize(CancellationToken ct)
    {
        // Ensure that the cache folder if valid, if it is not, then set size to 0 and return.
        var cacheDir = _config.Current.CacheFolder;
        if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir))
        {
            FileCacheSize = 0;
            return;
        }
        // Assume invalid / failure, -1
        FileCacheSize = -1;
        // Attempt to grab the available free space on the drive the cache folder in on.
        Generic.Safe(() =>
        {
            DriveInfo di = new(new DirectoryInfo(cacheDir).Root.FullName);
            FileCacheDriveFree = di.AvailableFreeSpace;
        });
        // Regardless, enumerate all files within the cache folder, sorted by the last access time.
        // (for mare, this was just the creation time, since they never modified access time, but we
        // will do the same even for our new method)
        var files = Directory.EnumerateFiles(cacheDir)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastAccessTime)
            .ToList();
        // Calculate the file cache's current size by getting the summation of all files on disk.
        FileCacheSize = files
            .Sum(f =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return _compactor.GetFileSizeOnDisk(f, StorageIsNTFS);
                }
                catch
                {
                    return 0;
                }
            });
        // obtain the maximum cache size that we wanted to allow.
        // Calculation 2GiB -> 2000MiB -> 2000000KiB -> 2000000000Bytes
        var maxCacheInBytes = (long)(_config.Current.MaxCacheInGiB * 1024d * 1024d * 1024d);
        
        // If the caches size is less than the maximum, complete update.
        if (FileCacheSize < maxCacheInBytes)
            return;

        // Otherwise, calculate what the max cache size would be at 95% full.
        var maxCacheBuffer = maxCacheInBytes * 0.05d;
        // Remove out oldest files until we reach this ammount.
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = files[0];
            FileCacheSize -= _compactor.GetFileSizeOnDisk(oldestFile);
            File.Delete(oldestFile.FullName);
            files.Remove(oldestFile);
        }
    }

    /// <summary>
    ///     Performs a full scan of the Penumbra mod directory and Sundouleia cache directory. <para />
    ///     Once off the game's framework update thread, executes <see cref="FullFileScan"/> on a background thread.
    /// </summary>
    public void InvokeScan()
    {
        // Track the progress of the scan.
        TotalFiles = 0;
        _currentFileProgress = 0;
        _cacheScanCTS = _cacheScanCTS.SafeCancelRecreate();
        var token = _cacheScanCTS.Token;
        // Run as fire-and-forget, with the cancellation token, to not block the main thread.
        _ = Task.Run(async () =>
        {
            Logger.LogDebug("=== Starting Full File Scan ===", LoggerType.FileMonitor);
            // Wait to leave the game's framework update thread before we start.
            while (Svc.Framework.IsInFrameworkUpdateThread)
            {
                Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                await Task.Delay(250, token).ConfigureAwait(false);
            }

            // Isolate the scan into its own background thread to avoid blocking, at lowest priority for performance.
            Thread scanThread = new(() => Generic.Safe(() => FullFileScan(token)))
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };

            // Begin the thread, and await for the duration that it is alive until completion.
            // (as it is not a 'task', we await in increments)
            scanThread.Start();
            while (scanThread.IsAlive)
                await Task.Delay(250).ConfigureAwait(false);

            // Reset the progress tracking.
            TotalFiles = 0;
            _currentFileProgress = 0;
        }, token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _cacheScanCTS?.Cancel();
        _cacheSizeCheckCTS.SafeCancelDispose();
    }

    /// <summary>
    ///     Very resource intensive operation that scans the entire Penumbra mod directory, and
    ///     then the Sundouleia cache directory, validating all files within the database. <para />
    ///     Intended to be used primarily on first launch or at login screen if possible to avoid performance impact. <para />
    ///     However it ocassionally is ran on critical changes to the watcher or file system structure.
    /// </summary>
    /// <param name="ct"> token that can cancel this operation at any time. </param>
    private void FullFileScan(CancellationToken ct)
    {
        TotalFiles = 1;
        var sw = new Stopwatch();
        sw.Start();
        var penumbraDir = IpcCallerPenumbra.ModDirectory;
        // Assume both watcher directories are valid.
        bool penDirExists = true;
        bool cacheDirExists = true;
        // Validate the directories.
        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            penDirExists = false;
            Logger.LogWarning("Penumbra directory is not set or does not exist.");
        }
        if (string.IsNullOrEmpty(_config.Current.CacheFolder) || !Directory.Exists(_config.Current.CacheFolder))
        {
            cacheDirExists = false;
            Logger.LogWarning("Sundeouleia Cache directory is not set or does not exist.");
        }
        // If either is invalid, do not perform the scan.
        if (!penDirExists || !cacheDirExists)
            return;

        // grab the priority of the thread we have be assigned (should be lowest) and set to lowest if not.
        // this will help lighten the burden on the client's PC and prevent impact of gameplay.
        var previousThreadPriority = Thread.CurrentThread.Priority;
        Logger.LogDebug($"Current Thread Priority is: {previousThreadPriority}", LoggerType.FileMonitor);
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;

        // Retrieve all paths from penumbra's directory.
        Logger.LogDebug($"Getting files from {penumbraDir} and {_config.Current.CacheFolder}", LoggerType.FileMonitor);
        Dictionary<string, string[]> penumbraFiles = new(StringComparer.Ordinal);
        // split by all top level directories within penumbra's mod directory.
        foreach (var folder in Directory.EnumerateDirectories(penumbraDir!))
        {
            try
            {
                // grab all files within the directory in parallel, filtering out invalid extentions and folders.
                penumbraFiles[folder] = 
                [
                    .. Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .AsParallel()
                        .Where(f => Constants.ValidExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                        && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase)),
                ];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
            }
            // dont make pc explotano.
            Thread.Sleep(50);
            if (ct.IsCancellationRequested)
                return;
        }

        // Now that penumbra is done being scanned, retrieve all files from within our defined cache folder.
        // Search via top directory only as there should not be any subdirectories here.
        var allCacheFiles = Directory.GetFiles(_config.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
            .AsParallel()
            .Where(f =>
            {
                // Ensure that our files have the exact 64 character hash in the filename. Any other files should not be accepted.
                var val = f.Split('\\')[^1];
                return val.Length == Constants.Blake3HashLength || (val.Split('.').FirstOrDefault()?.Length ?? 0) == Constants.Blake3HashLength;
            });

        if (ct.IsCancellationRequested)
            return;

        // Concatinate the files together from both penumbra and our sundouleia cache into one dictionary.
        // Run agaisnt the disinct filter incase for some god forbidden reason people place sundouleia cache files inside penumbra
        // mod folders.
        var allScannedFiles = (penumbraFiles.SelectMany(k => k.Value))
            .Concat(allCacheFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.ToLowerInvariant(), t => false, StringComparer.OrdinalIgnoreCase);

        // Determine the new total files and restore the threads current priority.
        TotalFiles = allScannedFiles.Count;
        Thread.CurrentThread.Priority = previousThreadPriority;

        sw.Stop();
        _lastScanTime = sw.Elapsed;
        Logger.LogDebug($"FileScan Complete (took {_lastScanTime.ToString(@"s's 'fff'ms'")})", LoggerType.FileMonitor);

        // make thread take eepy for a bit before we scan the database.
        Thread.Sleep(TimeSpan.FromSeconds(2));

        if (ct.IsCancellationRequested)
            return;

        // clamped between 2 and 8, use half of the available threads for the database scan.
        var threadCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);
        // track which entities should be removed or updated, and then perform the scan.
        List<FileCacheEntity> entitiesToRemove = [];
        List<FileCacheEntity> entitiesToUpdate = [];
        object sync = new();

        // assign all worker threads.
        Thread[] workerThreads = new Thread[threadCount];

        // Process the concurrentQueue for all file caches.
        ConcurrentQueue<FileCacheEntity> fileCaches = new(_fileDbManager.GetAllFileCaches());
        // Mark results.
        TotalFilesStorage = fileCaches.Count;
        sw.Restart();

        // Spawn all worker threads to process the file caches queue in parallel.
        // spawn all threads on the lowest priority as a background task to avoid performance impact.
        for (int i = 0; i < threadCount; i++)
        {
            Logger.LogTrace($"Creating Thread {i}", LoggerType.FileMonitor);
            workerThreads[i] = new Thread((tcounter) =>
            {
                var threadNr = (int)tcounter!;
                Logger.LogTrace($"Spawning Worker Thread {threadNr}", LoggerType.FileMonitor);
                // While we can still dequeue concurrently from the queue in this thread, process the workload.
                while (!ct.IsCancellationRequested && fileCaches.TryDequeue(out var workload))
                {
                    try
                    {
                        if (ct.IsCancellationRequested)
                            return;

                        // Halt processing if penumbra becomes no longer available midway through.
                        if (!IpcCallerPenumbra.APIAvailable)
                        {
                            Logger.LogWarning("Penumbra not available");
                            return;
                        }

                        // Validate the file cache entity for the workload provided.
                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(workload);
                        // If it doesnt require deletion, mark it as scanned.
                        if (validatedCacheResult.State != FileState.RequireDeletion)
                        {
                            lock (sync) { allScannedFiles[validatedCacheResult.FileCache.ResolvedFilepath] = true; }
                        }
                        // If it requires update or deletion, add to the respective lists.
                        if (validatedCacheResult.State == FileState.RequireUpdate)
                        {
                            Logger.LogTrace($"To update: {validatedCacheResult.FileCache.ResolvedFilepath}", LoggerType.FileMonitor);
                            lock (sync) { entitiesToUpdate.Add(validatedCacheResult.FileCache); }
                        }
                        // if it requires deletion, add to the removal list.
                        else if (validatedCacheResult.State == FileState.RequireDeletion)
                        {
                            Logger.LogTrace("To delete: {validatedCacheResult.FileCache.ResolvedFilepath}", LoggerType.FileMonitor);
                            lock (sync) { entitiesToRemove.Add(validatedCacheResult.FileCache); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", workload.ResolvedFilepath);
                    }
                    // Safely increament the current file progress.
                    Interlocked.Increment(ref _currentFileProgress);
                }
                // Once this thread is finished log its finalization.
                Logger.LogTrace($"Ending Worker Thread {threadNr}", LoggerType.FileMonitor);
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            // Start all threads.
            workerThreads[i].Start(i);
        }

        // await this thread until all worker threads are completed.
        while (!ct.IsCancellationRequested && workerThreads.Any(u => u.IsAlive))
            Thread.Sleep(1000);

        Logger.LogTrace("Threads exited", LoggerType.FileMonitor);
        // Guess what its another token & penumbra validation check 
        if (ct.IsCancellationRequested) return;
        if (!IpcCallerPenumbra.APIAvailable) return;

        sw.Restart();
        // Now that we have the entities to update, and the ones to remove, handle them.
        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
                _fileDbManager.UpdateHashedFile(entity);

            foreach (var entity in entitiesToRemove)
                _fileDbManager.RemoveHashedFile(entity.Hash, entity.PrefixedFilePath);

            // Then update the CSV to reflect these changes.
            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files", LoggerType.FileMonitor);
        // Guess what its another token & penumbra validation check 
        if (ct.IsCancellationRequested) return;
        if (!IpcCallerPenumbra.APIAvailable) return;

        // scan new files, and in parallel add them to the database.
        if (allScannedFiles.Any(c => !c.Value))
        {
            Parallel.ForEach(allScannedFiles.Where(c => !c.Value).Select(c => c.Key),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = threadCount,
                    CancellationToken = ct
                }, (cachePath) =>
                {
                    // Guess what its another token & penumbra validation check 
                    if (ct.IsCancellationRequested) return;
                    if (!IpcCallerPenumbra.APIAvailable) return;

                    try
                    {
                        // Attempt to create the entry using the cached penumbra path (we assume it is from penumbra)
                        var entry = _fileDbManager.CreateFileEntry(cachePath);
                        // If it fails then we should make it a sundouleia cache entry instead (if possible)
                        if (entry is null)
                            _ = _fileDbManager.CreateCacheEntry(cachePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed adding {file}", cachePath);
                    }
                    // Safely increament the current file progress.
                    Interlocked.Increment(ref _currentFileProgress);
                });

            Logger.LogTrace($"Scanner added {allScannedFiles.Count(c => !c.Value)} new files to db", LoggerType.FileMonitor);
        }

        // oh my god we made it out alive.
        sw.Stop();
        _lastCacheWriteTime = sw.Elapsed;
        Logger.LogDebug($"Scan Write complete (took {_lastCacheWriteTime.ToString(@"s's 'fff'ms'")})", LoggerType.FileMonitor);
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        allScannedFiles.Clear();

        // Update initial scan complete if it was not already.
        if (!_config.Current.InitialScanComplete)
        {
            _config.Current.InitialScanComplete = true;
            _config.Save();
            // start the respective watchers up again.
            _mainWatcher.StartWatcher(_config.Current.CacheFolder);
            _penumbraWatcher.StartWatcher(penumbraDir);
        }
    }
}