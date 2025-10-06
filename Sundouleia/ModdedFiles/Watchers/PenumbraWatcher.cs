namespace Sundouleia.ModFiles.Cache;

// Watches the penumbra mod directory for its relative files and any changes to said files for updates.
public sealed class PenumbraWatcher : IDisposable
{
    private readonly ILogger<PenumbraWatcher> _logger;
    private readonly FileCacheManager _fileDbManager;

    private readonly ConcurrentQueue<KeyValuePair<string, WatcherChange>> _changeQueue = new();
    private readonly CancellationTokenSource _processingCts = new();
    private Task? _processingTask;
    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);

    public PenumbraWatcher(ILogger<PenumbraWatcher> logger, FileCacheManager fileDbManager)
    {
        _logger = logger;
        _fileDbManager = fileDbManager;
        // Start the background processing task.
        _processingTask = Task.Run(ProcessChangesAsync, _processingCts.Token);
    }

    // Expose watcher for the cache monitor
    public FileSystemWatcher? Watcher { get; private set; }

    public void Dispose()
    {
        _processingCts.Cancel();
        _changeQueue.Clear();
        StopMonitoring();
    }

    public void StopMonitoring()
    {
        _logger.LogInformation("Stopping monitoring of Penumbra and Sundeouleia storage folders");
        Watcher?.Dispose();
        Watcher = null;
    }

    public void StartWatcher(string? penumbraPath)
    {
        // dispose of any current watcher and restart a new one for the updated path.
        Watcher?.Dispose();
        if (string.IsNullOrEmpty(penumbraPath))
        {
            // Or just null it if the new path is null/invalid.
            Watcher = null;
            _logger.LogWarning("Penumbra is not connected or the path is not set, cannot start FSW for Penumbra.");
            return;
        }

        _logger.LogDebug($"Initializing Penumbra FSW on: {penumbraPath}");
        Watcher = new()
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

        Watcher.Deleted += File_Changed;
        Watcher.Created += File_Changed;
        Watcher.Changed += File_Changed;
        Watcher.Renamed += File_Renamed;
        Watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    ///     If a file was deleted, created, or changed within the penumbra directory.
    /// </summary>
    private void File_Changed(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath)) return; // Ignore Directories.
        if (!Constants.ValidExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;
        if (e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Deleted or WatcherChangeTypes.Created)) return;

        // Enqueue the change for processing. (Avoid fire-and-forget)
        _changeQueue.Enqueue(new(e.FullPath, new WatcherChange(e.ChangeType)));
        _logger.LogTrace($"FS-Watcher Event: {e.ChangeType} on {e.FullPath}");
    }

    /// <summary>
    ///     If a file in the penumbra directory was renamed to a different name and needs to be updated. <para />
    ///     When comparing against the file cache, we know if it contains different contents after as the hash will be different.
    /// </summary>
    private void File_Renamed(object sender, RenamedEventArgs e)
    {
        // Ensure the directory exists if it was for a directory rename.
        if (Directory.Exists(e.FullPath))
        {
            // grab all files within the directory.
            var directoryFiles = Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in directoryFiles)
            {
                // Ignore non-relevant file extensions.
                if (!Constants.ValidExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    continue;
                // Determine the old path by replacing the new directory path with the old directory path.
                var oldPath = file.Replace(e.FullPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase);
                // Enqueue the change for processing. (Avoid fire-and-forget)
                _changeQueue.Enqueue(new(file, new WatcherChange(WatcherChangeTypes.Renamed, oldPath)));
                _logger.LogTrace($"FSW Renamed: {oldPath} -> {file}");
            }
        }
        else
        {
            // Ignore non-relevant file extensions.
            if (!Constants.ValidExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return;
            // Enqueue the change for processing. (Avoid fire-and-forget)
            _changeQueue.Enqueue(new(e.FullPath, new WatcherChange(WatcherChangeTypes.Renamed, e.OldFullPath)));
            _logger.LogTrace($"FSW Renamed: {e.OldFullPath} -> {e.FullPath}");
        }
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
            // Allow delay before processing changes. (Also writing out the full csv is costly)
            await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
            
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

            _ = _fileDbManager.GetFileCachesByPaths(allChanges);
            _fileDbManager.WriteOutFullCsv();
        }
    }
}