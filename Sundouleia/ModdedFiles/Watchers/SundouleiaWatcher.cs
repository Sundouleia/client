using Sundouleia.PlayerClient;

namespace Sundouleia.ModFiles.Cache;

// Might be possible to split this up into different files but we will see.
// I personally would prefer it since this looks a bit messy.
public sealed class SundouleiaWatcher : IDisposable
{
    private readonly ILogger<SundouleiaWatcher> _logger;
    private readonly MainConfig _config;
    private readonly FileCacheManager _fileDbManager;

    private readonly ConcurrentQueue<KeyValuePair<string, WatcherChange>> _changeQueue = new();
    private readonly CancellationTokenSource _processingCts = new();
    private Task? _processingTask;
    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);

    public SundouleiaWatcher(ILogger<SundouleiaWatcher> logger, MainConfig config, FileCacheManager fileDbManager)
    {
        _logger = logger;
        _config = config;
        _fileDbManager = fileDbManager;
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
        _logger.LogInformation("Stopping monitoring of the Sundeouleia storage folder", LoggerType.FileMonitor);
        Watcher?.Dispose();
        Watcher = null;
    }

    public void StartWatcher(string? sundeouleiaPath)
    {
        // Stop the current file system watcher regardless of if valid or not.
        Watcher?.Dispose();
        // If it is an invalid directory, null the watcher and return.
        if (string.IsNullOrEmpty(sundeouleiaPath) || !Directory.Exists(sundeouleiaPath))
        {
            Watcher = null;
            _logger.LogWarning("Sundeouleia file path is not set, cannot start the FSW for Sundeouleia.", LoggerType.FileMonitor);
            return;
        }

        // Otherwise get the drive information on the defined cache folder. (not the one we passed in)
        DriveInfo di = new(new DirectoryInfo(_config.Current.CacheFolder).Root.FullName);

        // Check if the storage is NTFS drive format and log it.
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation($"Storage is on NTFS drive: {StorageisNTFS}", LoggerType.FileMonitor);

        // Begin the FileSystemWatcher for the defined path we have passed in.
        _logger.LogDebug($"Initializing Sundeouleia FileSystemWatcher for: {sundeouleiaPath}", LoggerType.FileMonitor);
        Watcher = new()
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
        // Only want created and deleted here, should not track date modified things.
        Watcher.Deleted += File_Changed;
        Watcher.Created += File_Changed;
        Watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    ///     If a file was deleted, created, or changed within the penumbra directory.
    /// </summary>
    private void File_Changed(object sender, FileSystemEventArgs e)
    {
        // Only need to filter by valid extensions, as we know nothing else is in this folder.
        if (!Constants.ValidExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return;
        // Enqueue the change for processing. (Avoid fire-and-forget)
        _changeQueue.Enqueue(new(e.FullPath, new WatcherChange(e.ChangeType)));
        _logger.LogTrace($"FS-Watcher Event: {e.ChangeType} on {e.FullPath}", LoggerType.FileMonitor);
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

            // If we are currently running an invoked scan, await for it to complete.
            // TODO: Add logic for this here.
            // (we COULD try to see if we blow up when processing a change during a scan but idk if it just screenshots or not)

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
        lock (_fileDbManager)
        {
            var deletedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Deleted).Select(c => c.Key);
            var renamedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Renamed);
            var remainingEntries = changes.Where(c => c.Value.ChangeType != WatcherChangeTypes.Deleted).Select(c => c.Key);

            foreach (var entry in deletedEntries)
                _logger.LogDebug($"FSW Change: Deletion - {entry}", LoggerType.FileMonitor);

            foreach (var entry in renamedEntries)
                _logger.LogDebug($"FSW Change: Renamed - {entry.Value.OldPath} => {entry.Key}", LoggerType.FileMonitor);

            foreach (var entry in remainingEntries)
                _logger.LogDebug($"FSW Change: Creation or Change - {entry}", LoggerType.FileMonitor);

            var allChanges = deletedEntries
                .Concat(renamedEntries.Select(c => c.Value.OldPath!))
                .Concat(renamedEntries.Select(c => c.Key))
                .Concat(remainingEntries)
                .ToArray();

            _ = _fileDbManager.GetFileCachesByPaths(allChanges);
            _fileDbManager.WriteOutFullCsv();
        }
    }
}