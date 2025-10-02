using CkCommons;
using Sundouleia.Interop;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModFiles;

// Might be possible to split this up into different files but we will see.
// I personally would prefer it since this looks a bit messy.
public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly FileCompactor _compactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly IpcManager _ipc;

    private long _currentFileProgress = 0;
    private CancellationTokenSource _scanCancellationTokenSource = new();
    private readonly CancellationTokenSource _periodicCalculationTokenSource = new();
    public static readonly IImmutableList<string> AllowedFileExtensions = [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];

    public CacheMonitor(ILogger<CacheMonitor> logger, SundouleiaMediator mediator, IpcManager ipc,
        MainConfig config, FileCacheManager fileDbManager, FileCompactor fileCompactor) 
        : base(logger, mediator)
    {
        _config = config;
        _fileDbManager = fileDbManager;
        _compactor = fileCompactor;
        _ipc = ipc;

        // Make sure we start our watchers when 

        Mediator.Subscribe<PenumbraInitialized>(this, (_) =>
        {
            StartPenumbraWatcher(IpcCallerPenumbra.ModDirectory);
            StartSundeouleiaWatcher(config.Current.CacheFolder);
            InvokeScan();
        });
        //Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        //Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            StartSundeouleiaWatcher(config.Current.CacheFolder);
            StartPenumbraWatcher(IpcCallerPenumbra.ModDirectory);
            InvokeScan();
        });
        Mediator.Subscribe<PenumbraDirectoryChanged>(this, (msg) =>
        {
            StartPenumbraWatcher(msg.NewDirectory);
            InvokeScan();
        });

        // If penumbra's API is available and has a valid mod directory by the time this constructor runs, start the watcher.
        if (IpcCallerPenumbra.APIAvailable && !string.IsNullOrEmpty(IpcCallerPenumbra.ModDirectory))
            StartPenumbraWatcher(IpcCallerPenumbra.ModDirectory);

        // If our config has a valid cache folder setup then start the sundouleia watcher and invoke the scan.
        if (config.HasValidCacheFolderSetup())
        {
            StartSundeouleiaWatcher(config.Current.CacheFolder);
            InvokeScan();
        }
    }

    // various helper properties. (Mostly stuff to grab for the UI Display)
    public long FileCacheSize { get; set; }
    public long FileCacheDriveFree { get; set; }
    public ConcurrentDictionary<string, int> HaltScanLocks { get; set; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => _currentFileProgress > 0 || TotalFiles > 0;
    public string ScanProgressString => IsScanRunning ? $"{_currentFileProgress}/{TotalFiles}" : string.Empty;
    public long TotalFiles { get; private set; }
    public long TotalFilesStorage { get; private set; }

    /// <summary>
    ///     Preiodically checks the size of the cache.
    /// </summary>
    private async Task FileCacheSizeCheckTask()
    {
        Logger.LogInformation("Starting Periodic Storage Directory Calculation Task");
        var token = _periodicCalculationTokenSource.Token;
        // While the cache size calculation is not cancelled, recalculate every minute.
        while (!token.IsCancellationRequested)
        {
            await Generic.Safe(async () =>
            {
                // Wait for the framework update thread to occur,
                while (Svc.Framework.IsInFrameworkUpdateThread && !token.IsCancellationRequested)
                    await Task.Delay(1).ConfigureAwait(false);
                // then calculate the size outside of it.
                RecalculateFileCacheSize(token);
            }, true);
            // Await the next check for another minute.
            await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Halts any scans that would occur next until all sources are freed.
    /// </summary>
    public void HaltScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;
        HaltScanLocks[source]++;
    }

    // Internal variables for the watchers.
    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);
    private readonly Dictionary<string, WatcherChange> _watcherChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherChange> _sundeouleiaChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

    public void StopMonitoring()
    {
        Logger.LogInformation("Stopping monitoring of Penumbra and Sundeouleia storage folders");
        SundeouleiaWatcher?.Dispose();
        PenumbraWatcher?.Dispose();
        SundeouleiaWatcher = null;
        PenumbraWatcher = null;
    }

    // Again, mostly only public for the UI.
    public bool StorageisNTFS { get; private set; } = false;

    private CancellationTokenSource _sundeouleiaFswCts = new();
    public FileSystemWatcher? SundeouleiaWatcher { get; private set; }

    /// <summary>
    ///     Begins the watcher on the Sundouleia File Cache Folder.
    /// </summary>
    public void StartSundeouleiaWatcher(string? sundeouleiaPath)
    {
        // Stop the current file system watcher regardless of if valid or not.
        SundeouleiaWatcher?.Dispose();
        // If it is an invalid directory, null the watcher and return.
        if (string.IsNullOrEmpty(sundeouleiaPath) || !Directory.Exists(sundeouleiaPath))
        {
            SundeouleiaWatcher = null;
            Logger.LogWarning("Sundeouleia file path is not set, cannot start the FSW for Sundeouleia.");
            return;
        }

        // Otherwise get the drive information on the defined cache folder. (not the one we passed in)
        DriveInfo di = new(new DirectoryInfo(_config.Current.CacheFolder).Root.FullName);

        // Check if the storage is NTFS drive format and log it.
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        Logger.LogInformation($"Storage is on NTFS drive: {StorageisNTFS}");

        // Begin the FileSystemWatcher for the defined path we have passed in.
        Logger.LogDebug($"Initializing Sundeouleia FileSystemWatcher for: {sundeouleiaPath}");
        SundeouleiaWatcher = new()
        {
            Path = sundeouleiaPath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false, // should only ever be one large folder.
        };
        
        // Detect deletions and creations. Listen to raising events, but dont listen to modified files.
        SundeouleiaWatcher.Deleted += SundeouleiaWatcher_FileChanged;
        SundeouleiaWatcher.Created += SundeouleiaWatcher_FileChanged;
        SundeouleiaWatcher.EnableRaisingEvents = true;
    }

    // Perform the following whenever a file is added or removed from the FileCache
    private void SundeouleiaWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        Logger.LogTrace($"Watcher detected File Change: {{{e.ChangeType}}} => {{{e.FullPath}}}");
        // Ignore it if the extension is not one we care about.
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return;

        // Lock the dictionary we are interacting with to prevent concurrency issues when setting the value.
        lock (_watcherChanges)
            _sundeouleiaChanges[e.FullPath] = new(e.ChangeType);

        // Process a task via fire-and-forget to handle the file change.
        _ = SundeouleiaWatcherExecution();
    }

    /// <summary>
    ///     Funs in the form of a task, called via fire-and-forget (could be optimized in theory)
    ///     
    ///     This really feels like it should be re-purposed as it feels very overboard for a simple update check.
    /// </summary>
    private async Task SundeouleiaWatcherExecution()
    {
        // Recreate the CTS and grab initial values.
        _sundeouleiaFswCts = _sundeouleiaFswCts.SafeCancelRecreate();
        var token = _sundeouleiaFswCts.Token;
        var delay = TimeSpan.FromSeconds(5);

        // Construct what changes occured via thread locking to compile the dictionary of values together.
        Dictionary<string, WatcherChange> changes;
        lock (_sundeouleiaChanges)
            changes = _sundeouleiaChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        // Continue to wait for any further action until all haltScanLocks are finished. (do-while = always wait 5s first)
        try
        {
            do
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            } while (HaltScanLocks.Any(f => f.Value > 0));
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Once done waiting, take the changes obtained at the time of the action and remove them from the main dictionary.
        lock (_sundeouleiaChanges)
        {
            foreach (var key in changes.Keys)
                _sundeouleiaChanges.Remove(key);
        }
        // Then handle the remaining changes.
        HandleChanges(changes);
    }

    /// <summary>
    ///     Handle all changes called upon by the watcher execution, which is called off thread.. idk lol.    
    /// </summary>
    private void HandleChanges(Dictionary<string, WatcherChange> changes)
    {
        // Ensure that each threaded action to handle the changes is performing the updates via locking.
        lock (_fileDbManager)
        {
            var deletedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Deleted).Select(c => c.Key);
            var renamedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Renamed);
            var remainingEntries = changes.Where(c => c.Value.ChangeType != WatcherChangeTypes.Deleted).Select(c => c.Key);

            // Log and concat all current updates.
            foreach (var entry in deletedEntries)
                Logger.LogDebug($"FSW Change: Deletion - {entry}");

            foreach (var entry in renamedEntries)
                Logger.LogDebug($"FSW Change: Renamed - {entry.Value.OldPath} => {entry.Key}");

            foreach (var entry in remainingEntries)
                Logger.LogDebug($"FSW Change: Creation or Change - {entry}");

            var allChanges = deletedEntries
                .Concat(renamedEntries.Select(c => c.Value.OldPath!))
                .Concat(renamedEntries.Select(c => c.Key))
                .Concat(remainingEntries)
                .ToArray();

            // pass the changes into a function that grabs the file caches via their paths for some reason before writing out the CSV?
            _ = _fileDbManager.GetFileCachesByPaths(allChanges);

            // then write out the CSV?
            _fileDbManager.WriteOutFullCsv();
        }
    }


    public void StartPenumbraWatcher(string? penumbraPath)
    {
        PenumbraWatcher?.Dispose();
        if (string.IsNullOrEmpty(penumbraPath))
        {
            PenumbraWatcher = null;
            Logger.LogWarning("Penumbra is not connected or the path is not set, cannot start FSW for Penumbra.");
            return;
        }

        Logger.LogDebug("Initializing Penumbra FSW on {path}", penumbraPath);
        PenumbraWatcher = new()
        {
            Path = penumbraPath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = true
        };

        PenumbraWatcher.Deleted += Fs_Changed;
        PenumbraWatcher.Created += Fs_Changed;
        PenumbraWatcher.Changed += Fs_Changed;
        PenumbraWatcher.Renamed += Fs_Renamed;
        PenumbraWatcher.EnableRaisingEvents = true;
    }

    private void Fs_Changed(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return;
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        if (e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Deleted or WatcherChangeTypes.Created))
            return;

        lock (_watcherChanges)
        {
            _watcherChanges[e.FullPath] = new(e.ChangeType);
        }

        Logger.LogTrace("FSW {event}: {path}", e.ChangeType, e.FullPath);

        _ = PenumbraWatcherExecution();
    }

    private void Fs_Renamed(object sender, RenamedEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            var directoryFiles = Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories);
            lock (_watcherChanges)
            {
                foreach (var file in directoryFiles)
                {
                    if (!AllowedFileExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
                    var oldPath = file.Replace(e.FullPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase);

                    _watcherChanges.Remove(oldPath);
                    _watcherChanges[file] = new(WatcherChangeTypes.Renamed, oldPath);
                    Logger.LogTrace("FSW Renamed: {path} -> {new}", oldPath, file);

                }
            }
        }
        else
        {
            if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

            lock (_watcherChanges)
            {
                _watcherChanges.Remove(e.OldFullPath);
                _watcherChanges[e.FullPath] = new(WatcherChangeTypes.Renamed, e.OldFullPath);
            }

            Logger.LogTrace("FSW Renamed: {path} -> {new}", e.OldFullPath, e.FullPath);
        }

        _ = PenumbraWatcherExecution();
    }

    private CancellationTokenSource _penumbraFswCts = new();
    public FileSystemWatcher? PenumbraWatcher { get; private set; }

    private async Task PenumbraWatcherExecution()
    {
        _penumbraFswCts = _penumbraFswCts.SafeCancelRecreate();
        var token = _penumbraFswCts.Token;
        Dictionary<string, WatcherChange> changes;
        lock (_watcherChanges)
            changes = _watcherChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var delay = TimeSpan.FromSeconds(10);
        try
        {
            do
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            } while (HaltScanLocks.Any(f => f.Value > 0));
        }
        catch (TaskCanceledException)
        {
            return;
        }

        lock (_watcherChanges)
        {
            foreach (var key in changes.Keys)
            {
                _watcherChanges.Remove(key);
            }
        }

        HandleChanges(changes);
    }

    public void InvokeScan()
    {
        TotalFiles = 0;
        _currentFileProgress = 0;
        _scanCancellationTokenSource = _scanCancellationTokenSource.SafeCancelRecreate();
        var token = _scanCancellationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            Logger.LogDebug("Starting Full File Scan");
            TotalFiles = 0;
            _currentFileProgress = 0;
            while (Svc.Framework.IsInFrameworkUpdateThread)
            {
                Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                await Task.Delay(250, token).ConfigureAwait(false);
            }

            Thread scanThread = new(() =>
            {
                try
                {
                    FullFileScan(token);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error during Full File Scan");
                }
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            scanThread.Start();
            while (scanThread.IsAlive)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            TotalFiles = 0;
            _currentFileProgress = 0;
        }, token);
    }

    public void RecalculateFileCacheSize(CancellationToken token)
    {
        if (string.IsNullOrEmpty(_config.Current.CacheFolder) || !Directory.Exists(_config.Current.CacheFolder))
        {
            FileCacheSize = 0;
            return;
        }

        FileCacheSize = -1;
        DriveInfo di = new(new DirectoryInfo(_config.Current.CacheFolder).Root.FullName);
        try
        {
            FileCacheDriveFree = di.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not determine drive size for Storage Folder {folder}", _config.Current.CacheFolder);
        }

        var files = Directory.EnumerateFiles(_config.Current.CacheFolder).Select(f => new FileInfo(f))
            .OrderBy(f => f.LastAccessTime).ToList();
        FileCacheSize = files
            .Sum(f =>
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    return _compactor.GetFileSizeOnDisk(f, StorageisNTFS);
                }
                catch
                {
                    return 0;
                }
            });

        var maxCacheInBytes = (long)(_config.Current.MaxCacheInGiB * 1024d * 1024d * 1024d);

        if (FileCacheSize < maxCacheInBytes) return;

        var maxCacheBuffer = maxCacheInBytes * 0.05d;
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = files[0];
            FileCacheSize -= _compactor.GetFileSizeOnDisk(oldestFile);
            File.Delete(oldestFile.FullName);
            files.Remove(oldestFile);
        }
    }

    public void ResetLocks()
    {
        HaltScanLocks.Clear();
    }

    public void ResumeScan(string source)
    {
        if (!HaltScanLocks.ContainsKey(source)) HaltScanLocks[source] = 0;

        HaltScanLocks[source]--;
        if (HaltScanLocks[source] < 0) HaltScanLocks[source] = 0;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scanCancellationTokenSource?.Cancel();
        PenumbraWatcher?.Dispose();
        SundeouleiaWatcher?.Dispose();
        _penumbraFswCts.SafeCancelDispose();
        _sundeouleiaFswCts.SafeCancelDispose();
        _periodicCalculationTokenSource.SafeCancelDispose();
    }

    private void FullFileScan(CancellationToken ct)
    {
        TotalFiles = 1;
        var penumbraDir = IpcCallerPenumbra.ModDirectory;
        bool penDirExists = true;
        bool cacheDirExists = true;
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
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        var previousThreadPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _config.Current.CacheFolder);

        Dictionary<string, string[]> penumbraFiles = new(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(penumbraDir!))
        {
            try
            {
                penumbraFiles[folder] =
                [
                    .. Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                                            .AsParallel()
                                            .Where(f => AllowedFileExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase)),
                ];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
            }
            Thread.Sleep(50);
            if (ct.IsCancellationRequested) return;
        }

        var allCacheFiles = Directory.GetFiles(_config.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .AsParallel()
                                .Where(f =>
                                {
                                    var val = f.Split('\\')[^1];
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40;
                                });

        if (ct.IsCancellationRequested) return;

        var allScannedFiles = (penumbraFiles.SelectMany(k => k.Value))
            .Concat(allCacheFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.ToLowerInvariant(), t => false, StringComparer.OrdinalIgnoreCase);

        TotalFiles = allScannedFiles.Count;
        Thread.CurrentThread.Priority = previousThreadPriority;

        Thread.Sleep(TimeSpan.FromSeconds(2));

        if (ct.IsCancellationRequested) return;

        // scan files from database
        var threadCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);

        List<FileCacheEntity> entitiesToRemove = [];
        List<FileCacheEntity> entitiesToUpdate = [];
        object sync = new();
        Thread[] workerThreads = new Thread[threadCount];

        ConcurrentQueue<FileCacheEntity> fileCaches = new(_fileDbManager.GetAllFileCaches());

        TotalFilesStorage = fileCaches.Count;

        for (int i = 0; i < threadCount; i++)
        {
            Logger.LogTrace("Creating Thread {i}", i);
            workerThreads[i] = new((tcounter) =>
            {
                var threadNr = (int)tcounter!;
                Logger.LogTrace("Spawning Worker Thread {i}", threadNr);
                while (!ct.IsCancellationRequested && fileCaches.TryDequeue(out var workload))
                {
                    try
                    {
                        if (ct.IsCancellationRequested) return;

                        if (!IpcCallerPenumbra.APIAvailable)
                        {
                            Logger.LogWarning("Penumbra not available");
                            return;
                        }

                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(workload);
                        if (validatedCacheResult.State != FileState.RequireDeletion)
                        {
                            lock (sync) { allScannedFiles[validatedCacheResult.FileCache.ResolvedFilepath] = true; }
                        }
                        if (validatedCacheResult.State == FileState.RequireUpdate)
                        {
                            Logger.LogTrace("To update: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToUpdate.Add(validatedCacheResult.FileCache); }
                        }
                        else if (validatedCacheResult.State == FileState.RequireDeletion)
                        {
                            Logger.LogTrace("To delete: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToRemove.Add(validatedCacheResult.FileCache); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", workload.ResolvedFilepath);
                    }
                    Interlocked.Increment(ref _currentFileProgress);
                }

                Logger.LogTrace("Ending Worker Thread {i}", threadNr);
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            workerThreads[i].Start(i);
        }

        while (!ct.IsCancellationRequested && workerThreads.Any(u => u.IsAlive))
        {
            Thread.Sleep(1000);
        }

        if (ct.IsCancellationRequested) return;

        Logger.LogTrace("Threads exited");

        if (!IpcCallerPenumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
            {
                _fileDbManager.UpdateHashedFile(entity);
            }

            foreach (var entity in entitiesToRemove)
            {
                _fileDbManager.RemoveHashedFile(entity.Hash, entity.PrefixedFilePath);
            }

            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files");

        if (!IpcCallerPenumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // scan new files
        if (allScannedFiles.Any(c => !c.Value))
        {
            Parallel.ForEach(allScannedFiles.Where(c => !c.Value).Select(c => c.Key),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = threadCount,
                    CancellationToken = ct
                }, (cachePath) =>
                {
                    if (ct.IsCancellationRequested) return;

                    if (!IpcCallerPenumbra.APIAvailable)
                    {
                        Logger.LogWarning("Penumbra not available");
                        return;
                    }

                    try
                    {
                        var entry = _fileDbManager.CreateFileEntry(cachePath);
                        if (entry == null) _ = _fileDbManager.CreateCacheEntry(cachePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed adding {file}", cachePath);
                    }

                    Interlocked.Increment(ref _currentFileProgress);
                });

            Logger.LogTrace("Scanner added {notScanned} new files to db", allScannedFiles.Count(c => !c.Value));
        }

        Logger.LogDebug("Scan complete");
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        allScannedFiles.Clear();

        if (!_config.Current.InitialScanComplete)
        {
            _config.Current.InitialScanComplete = true;
            _config.Save();
            StartSundeouleiaWatcher(_config.Current.CacheFolder);
            StartPenumbraWatcher(penumbraDir);
        }
    }
}