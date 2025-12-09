using CkCommons;
using Sundouleia.Services.Mediator;

namespace Sundouleia.ModFiles;

public sealed class ActorAnalyzer : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _manager;
    private readonly PlzNoCrashFrens _triCalculator;

    private CancellationTokenSource? _analysisCTS;
    private CancellationTokenSource _baseAnalysisCTS = new();

    private ModdedState _prevModdedState = new();

    // If we wanted to we could make an internal analysis object with a entry comparer for the dictionary,
    // But this will do for now.
    internal Dictionary<OwnedObject, Dictionary<string, FileDataEntry>> LastAnalysis { get; } = [];

    public ActorAnalyzer(ILogger<ActorAnalyzer> logger, SundouleiaMediator mediator,
        FileCacheManager manager, PlzNoCrashFrens triCalculator)
        : base(logger, mediator)
    {
        Mediator.Subscribe<ModdedStateCollected>(this, msg =>
        {
            _baseAnalysisCTS = _baseAnalysisCTS.SafeCancelRecreate();
            var token = _baseAnalysisCTS.Token;
            _ = Task.Run(() => BaseAnalysis(msg.ModdedState, token));
        });

        _manager = manager;
        _triCalculator = triCalculator;
    }

    public int CurrentFile { get; internal set; }
    public bool AnalyzingActor => _analysisCTS is not null;
    public int TotalFiles { get; internal set; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _analysisCTS.SafeCancelDispose();
    }

    public void Halt()
    {
        _analysisCTS.SafeCancelDispose();
        _analysisCTS = null;
    }

    // Computer an actor's full analysis.
    public async Task Compute(bool print = true, bool recalculate = false)
    {
        Logger.LogDebug("=== Calculating Character Analysis ===");
        _analysisCTS = _analysisCTS.SafeCancelRecreate();
        var token = _analysisCTS.Token;

        // Identify all files associated with the current actor based on the last analysis known.
        var allFiles = LastAnalysis.SelectMany(v => v.Value.Select(d => d.Value)).ToList();
        // If there exist any files that are not yet computed, or we desired a recalculation, compute the files.
        if (allFiles.Exists(c => !c.IsComputed || recalculate))
        {
            // Store which files require a scan.
            var remaining = allFiles.Where(c => !c.IsComputed || recalculate).ToList();
            // Cache the current progress, and log as it occurs.
            TotalFiles = remaining.Count;
            CurrentFile = 1;
            Logger.LogDebug($"=== Computing {remaining.Count} remaining files ===");

            try
            {
                foreach (var file in remaining)
                {
                    Logger.LogDebug($"Computing file {file.FilePaths[0]}");
                    await file.ComputeSizes(_manager, token).ConfigureAwait(false);
                    CurrentFile++;
                }

                // After all files are computed, write out the full CSV.
                _manager.WriteOutFullCsv();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Summoned Bagagwa while analyzing actor files: {ex}");
            }
        }

        // Inform other locations in sundouleia that we have finished analyzing this actor's data.
        Mediator.Publish(new ActorAnalyzedMessage());
        _analysisCTS.SafeCancelDispose();
        _analysisCTS = null;

        // Display the results of the analysis if desired.
        if (print)
            PrintAnalysis();
    }

    private void BaseAnalysis(ModdedState moddedState, CancellationToken token)
    {
        // Avoid a recalculation if it contains the same data.
        if (_prevModdedState.AllFiles.SequenceEqual(moddedState.AllFiles))
            return;

        // Bomb the previous analysis results.
        LastAnalysis.Clear();

        foreach (var (ownedObj, moddedFiles) in moddedState.FilesByObject)
        {
            // collect all analyzed data for each object
            var analyzedData = new Dictionary<string, FileDataEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var moddedFile in moddedFiles)
            {
                // Ensure if we request to abort at any point that we do so.
                token.ThrowIfCancellationRequested();
                // Obtain all file entries associated with the hash.
                var entries = _manager.GetAllFileCachesByHash(moddedFile.Hash, ignoreCacheEntries: true, validate: false).ToList();
                // Skip to the next file if no entries not yet cached are present.
                if (entries.Count is 0)
                    continue;

                var filePath = entries[0].ResolvedFilepath;
                var fileInfo = new FileInfo(filePath);
                string extension = "unk?";
                try
                {
                    extension = fileInfo.Extension[1..];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Extension for {filePath} couldn't be verified. ({ex})");
                }

                // Attempt to read the tri-count of the file.
                var triCount = _triCalculator.GetTrianglesByHash(moddedFile.Hash);

                // Not process over all the entries and construct a FileDataEntry to store information about the file.
                foreach (var entry in entries)
                {
                    analyzedData[moddedFile.Hash] = new FileDataEntry(
                        moddedFile.Hash,
                        extension,
                        [.. moddedFile.GamePaths],
                        entries.Select(c => c.ResolvedFilepath).Distinct().ToList(),
                        entry.Size > 0 ? entry.Size.Value : 0,
                        entry.CompressedSize > 0 ? entry.CompressedSize.Value : 0,
                        triCount
                    );
                }
            }

            // Update the last analysis for our client actor data.
            LastAnalysis[ownedObj] = analyzedData;
        }

        // Inform other locations in sundouleia that we have finished analyzing this actor's data.
        Mediator.Publish(new ActorAnalyzedMessage());
        // Update the last scanned data.
        _prevModdedState = moddedState;
    }

    // If we want to visually see the results of our analyzed actor, we can view it within the logger.
    private void PrintAnalysis()
    {
        if (LastAnalysis.Count is 0)
            return;

        // Need to print the analysis for each owned object now.
        foreach (var (ownedObject, analyzedData) in LastAnalysis)
        {
            int fileCounter = 1;
            int totalFiles = LastAnalysis.Count;
            // Overview
            Logger.LogInformation($"=== Analysis for {ownedObject} ===");
            foreach (var (fileHash, dataEntry) in analyzedData.OrderBy(b => b.Value.GamePaths.OrderBy(p => p, StringComparer.Ordinal).First(), StringComparer.Ordinal))
            {
                Logger.LogInformation($"File {fileCounter++}/{totalFiles}: {fileHash}");
                foreach (var path in dataEntry.GamePaths)
                    Logger.LogInformation($" ♦ Game Path: {path}");

                if (dataEntry.FilePaths.Count > 1)
                    Logger.LogInformation($"  Multiple fitting files detected for {fileHash}");

                foreach (var filePath in dataEntry.FilePaths)
                    Logger.LogInformation($" ♦ File Path: {filePath}");

                // Output final data of the fileHash.
                Logger.LogInformation($"  Size: {SundouleiaEx.ByteToString(dataEntry.OriginalSize)}, Compressed: {SundouleiaEx.ByteToString(dataEntry.CompressedSize)}");
            }
        }

        // Include a more in-depth summary the modded files of the client actor by type.
        foreach (var (ownedObject, analyzedData) in LastAnalysis)
        {
            Logger.LogInformation($"=== Detailed summary by file type for {ownedObject} ===");
            foreach (var entry in analyzedData.Values.GroupBy(v => v.FileType, StringComparer.Ordinal))
                Logger.LogInformation($"{entry.Key} | Files: {entry.Count()} | " +
                    $"Extracted: {SundouleiaEx.ByteToString(entry.Sum(v => v.OriginalSize))} | " +
                    $"Compressed: {SundouleiaEx.ByteToString(entry.Sum(v => v.CompressedSize))}");

            Logger.LogInformation($"=== Total summary for {ownedObject} ===");
            Logger.LogInformation($"FileCount: {analyzedData.Count} | " +
                $"Extracted Size: {SundouleiaEx.ByteToString(analyzedData.Sum(v => v.Value.OriginalSize))} |" +
                $"Compressed Size: {SundouleiaEx.ByteToString(analyzedData.Sum(v => v.Value.CompressedSize))}");
        }

        // Finally log the overall summary of the analysis.
        Logger.LogInformation($"=== Total summary for Owned Actor(s) ===");
        Logger.LogInformation($"Files: {LastAnalysis.Values.Sum(v => v.Values.Count)} | " +
            $"Extracted Size: {SundouleiaEx.ByteToString(LastAnalysis.Values.Sum(v => v.Values.Sum(v => v.OriginalSize)))} | " +
            $"Compressed Size: {SundouleiaEx.ByteToString(LastAnalysis.Values.Sum(v => v.Values.Sum(v => v.CompressedSize)))}");
    }
}