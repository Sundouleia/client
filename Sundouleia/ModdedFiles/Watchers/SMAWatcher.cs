using Sundouleia.PlayerClient;

namespace Sundouleia.ModFiles.Cache;

// Monitors the cached files of loaded Sundouleia Modular Actors while inside GPose.
public sealed class ModularActorWatcher : IDisposable
{
    private readonly ILogger<ModularActorWatcher> _logger;
    private readonly MainConfig _config;
    private readonly SMAFileCacheManager _smaCacheManager;

    private readonly ConcurrentQueue<KeyValuePair<string, WatcherChange>> _changeQueue = new();
    private readonly CancellationTokenSource _processingCts = new();
    private Task? _processingTask;
    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);

    public ModularActorWatcher(ILogger<ModularActorWatcher> logger, MainConfig config, SMAFileCacheManager smaCacheManager)
    {
        _logger = logger;
        _config = config;
        _smaCacheManager = smaCacheManager;
        // Start the background processing task.
        _processingTask = Task.Run(ProcessChangesAsync, _processingCts.Token);
    }

    // Expose watcher for the cache monitor
    public bool StorageisNTFS { get; private set; } = false;
    public FileSystemWatcher? Watcher { get; private set; }

    public void Dispose()
    {
        _processingCts.Cancel();
        _changeQueue.Clear();
        StopMonitoring();
    }

    public void StopMonitoring()
    {
        _logger.LogInformation("Stopping monitoring of the SMA storage folders");
        Watcher?.Dispose();
        Watcher = null;
    }

    public void StartWatcher(string? smaCachePath)
    {
        // Stop the current file system watcher regardless of if valid or not.
        Watcher?.Dispose();
        // If it is an invalid directory, null the watcher and return.
        if (string.IsNullOrEmpty(smaCachePath))
        {
            Watcher = null;
            _logger.LogWarning("Invalid SMA path provided. Cannot start watcher.");
            return;
        }

        // If the directory does not yet exist, create it.
        if (!Directory.Exists(smaCachePath))
        {
            Directory.CreateDirectory(smaCachePath);
            _logger.LogInformation($"Created missing SMA cache directory at: {smaCachePath}");
        }

        // Otherwise get the drive information on the defined cache folder. (not the one we passed in)
        DriveInfo di = new(new DirectoryInfo(_config.Current.CacheFolder).Root.FullName);

        // Check if the storage is NTFS drive format and log it.
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation($"Storage is on NTFS drive: {StorageisNTFS}");

        // Begin the FileSystemWatcher for the defined path we have passed in.
        _logger.LogDebug($"Initializing SMA FileSystemWatcher for: {smaCachePath}");
        Watcher = new()
        {
            Path = smaCachePath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
        };
        // Only want created and deleted here, should not track date modified things.
        Watcher.Deleted += File_Changed;
        Watcher.Created += File_Changed;
        Watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    ///     If a file was deleted or created in the SMA directory.
    /// </summary>
    private void File_Changed(object sender, FileSystemEventArgs e)
    {
        // Workaround to allow filtering for multiple extension types.
        if (!Constants.SMAExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return;
        // Enqueue the change for processing. (Avoid fire-and-forget)
        _changeQueue.Enqueue(new(e.FullPath, new WatcherChange(e.ChangeType)));
        _logger.LogDebug($"FS-Watcher Event: {e.ChangeType} on {e.FullPath}");
    }

    /// <summary>
    ///     Periodically process changes from the queue in batches to avoid excessive handling.
    /// </summary>
    private async Task ProcessChangesAsync()
    {
        // Define the token, and changebatch we will use.
        var token = _processingCts.Token;
        var changeBatch = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

        while (!token.IsCancellationRequested)
        {
            // Allow delay before processing changes. (Slightly faster than penumbras folder)
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);

            // Dequeue all changes (this also ensures they are distinct)
            while (_changeQueue.TryDequeue(out var change))
                changeBatch[change.Key] = change.Value;
            // Process them if any are present.
            if (changeBatch.Count > 0)
            {
                HandleChanges(changeBatch);
                changeBatch.Clear();
            }
        }
    }

    /// <summary>
    ///     Handle all changes called upon by the watcher execution from the dequeue.
    /// </summary>
    private void HandleChanges(Dictionary<string, WatcherChange> changes)
    {
        lock (_smaCacheManager)
        {
            var deletedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Deleted).Select(c => c.Key);
            var renamedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Renamed);
            var remainingEntries = changes.Where(c => c.Value.ChangeType != WatcherChangeTypes.Deleted).Select(c => c.Key);

            foreach (var entry in deletedEntries)
                _logger.LogDebug($"FSW Change: Deletion - {entry}");

            foreach (var entry in renamedEntries)
                _logger.LogDebug($"FSW Change: Renamed - {entry.Value.OldPath} => {entry.Key}");

            foreach (var entry in remainingEntries)
                _logger.LogDebug($"FSW Change: Creation or Change - {entry}");

            var allChanges = deletedEntries
                .Concat(renamedEntries.Select(c => c.Value.OldPath!))
                .Concat(renamedEntries.Select(c => c.Key))
                .Concat(remainingEntries)
                .ToArray();

            _ = _smaCacheManager.GetFileCachesByPaths(allChanges);
            // Do not write out the csv, we have no use for it as this is relatively temporary.
        }
    }
}