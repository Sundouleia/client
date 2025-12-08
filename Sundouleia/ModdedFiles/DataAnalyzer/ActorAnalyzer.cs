using CkCommons;
using Lumina.Data.Files;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

namespace Sundouleia.ModFiles;

public sealed class ActorAnalyzer : MediatorSubscriberBase, IDisposable
{
    private readonly FileCacheManager _manager;
    private readonly PlzNoCrashFrens _triCalculator;

    private CancellationTokenSource? _analysisCTS;
    private CancellationTokenSource _baseAnalysisCTS = new();

    // Might need to store the dictionary instead tbh.
    private ClientDataCache _prevClientCache = new();

    public ActorAnalyzer(ILogger<ActorAnalyzer> logger, SundouleiaMediator mediator, 
        FileCacheManager manager, PlzNoCrashFrens triCalculator)
        : base(logger, mediator)
    {
        _manager = manager;
        _triCalculator = triCalculator;
    }

    public int CurrentFile { get; internal set; }
    public bool AnalyzingActor => _analysisCTS is not null;
    public int TotalFiles { get; internal set; }

    // We dont filter AppliedMods by OwnedObjects,
    // as they are all shared under the same modded update dictionary.
    // (If we REALLLLY feel the need to we could but I see 0 point in doing so right now.)
    internal Dictionary<string, FileDataEntry> LastAnalysis { get; private set; } = [];

    public void Halt()
    {
        _analysisCTS.SafeCancelDispose();
        _analysisCTS = null;
    }

    // Runs this off the main thread to avoid blocking.
    public void UpdatedOwnedActorsMods()
    {
        _baseAnalysisCTS = _baseAnalysisCTS.SafeCancelRecreate();
        var token = _baseAnalysisCTS.Token;
        _ = Task.Run(() => BaseAnalysis(DistributionService.LastCreatedData, token));
    }

    // Computer an actor's full analysis.
    public async Task Compute(bool print = true, bool recalculate = false)
    {
        Logger.LogDebug("=== Calculating Character Analysis ===");
        _analysisCTS = _analysisCTS.SafeCancelRecreate();
        var token = _analysisCTS.Token;

        // Identify all files associated with the current actor based on the last analysis known.
        var allFiles = LastAnalysis.Values.ToList();
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

    public void Dispose()
    {
        _analysisCTS.SafeCancelDispose();
    }

    private void BaseAnalysis(ClientDataCache clientActorData, CancellationToken token)
    {
        // Avoid a recalculation if it contains the same data.
        if (_prevClientCache.AppliedMods.SequenceEqual(clientActorData.AppliedMods))
        {
            Logger.LogError("Actor analysis skipped as no changes were detected.");
            return;
        }

        // Bomb the previous analysis results.
        LastAnalysis.Clear();

        // Sundouleia does not split the modded files between each OwnedObject.
        // As such, simply iterate through all moddedFiles in the appliedMods.
        var data = new Dictionary<string, FileDataEntry>(StringComparer.OrdinalIgnoreCase); 
        foreach (var (fileHash, moddedFile) in clientActorData.AppliedMods)
        {
            // Ensure if we request to abort at any point that we do so.
            token.ThrowIfCancellationRequested();
            // Obtain all file entries associated with the hash.
            var entries = _manager.GetAllFileCachesByHash(fileHash, ignoreCacheEntries: true, validate: false).ToList();
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
            var triCount = _triCalculator.GetTrianglesByHash(fileHash);
            
            // Not process over all the entries and construct a FileDataEntry to store information about the file.
            foreach (var entry in entries)
            {
                data[fileHash] = new FileDataEntry(
                    fileHash, 
                    extension,
                    [.. moddedFile.GamePaths],
                    entries.Select(c => c.ResolvedFilepath).Distinct().ToList(),
                    entry.Size > 0 ? entry.Size.Value : 0,
                    entry.CompressedSize > 0 ? entry.CompressedSize.Value : 0,
                    triCount
                );
            }

            // Update the last analysis for our client actor data.
            LastAnalysis = data;
        }

        // Inform other locations in sundouleia that we have finished analyzing this actor's data.
        Mediator.Publish(new ActorAnalyzedMessage());
        // Also update the last scanned data.
        _prevClientCache = clientActorData;
    }

    // If we want to visually see the results of our analyzed actor, we can view it within the logger.
    private void PrintAnalysis()
    {
        if (LastAnalysis.Count is 0)
            return;

        int fileCounter = 1;
        int totalFiles = LastAnalysis.Count;
        // Overview
        Logger.LogInformation($"=== Analysis for Client Actor(s) ===");
        foreach (var (fileHash, fileData) in LastAnalysis.OrderBy(b => b.Value.GamePaths.OrderBy(p => p, StringComparer.Ordinal).First(), StringComparer.Ordinal))
        {
            Logger.LogInformation($"File {fileCounter++}/{totalFiles}: {fileHash}");
            foreach (var path in fileData.GamePaths)
                Logger.LogInformation($" ♦ Game Path: {path}");

            if (fileData.FilePaths.Count > 1) 
                Logger.LogInformation($"  Multiple fitting files detected for {fileHash}");
            
            foreach (var filePath in fileData.FilePaths)
                Logger.LogInformation($" ♦ File Path: {filePath}");

            // Output final data of the fileHash.
            Logger.LogInformation($"  Size: {SundouleiaEx.ByteToString(fileData.OriginalSize)}, Compressed: {SundouleiaEx.ByteToString(fileData.CompressedSize)}");
        }

        // Include a more in-depth summary the modded files of the client actor by type.
        Logger.LogInformation("=== Detailed summary by file type for Client Actor(s) ===");
        foreach (var entry in LastAnalysis.Values.GroupBy(v => v.FileType, StringComparer.Ordinal))
            Logger.LogInformation($"{entry.Key} | Files: {entry.Count()} | Extracted: {SundouleiaEx.ByteToString(entry.Sum(v => v.OriginalSize))} | Compressed: {SundouleiaEx.ByteToString(entry.Sum(v => v.CompressedSize))}");

        // Finally log the overall summary of the analysis.
        Logger.LogInformation("=== Total summary for Client Actor(s) ===");
        Logger.LogInformation($"Files: {totalFiles} | Extracted Size: {SundouleiaEx.ByteToString(LastAnalysis.Sum(v => v.Value.OriginalSize))} | Compressed Size: {SundouleiaEx.ByteToString(LastAnalysis.Sum(v => v.Value.CompressedSize))}");
    }

    internal sealed record FileDataEntry(string Hash, string FileType, List<string> GamePaths, List<string> FilePaths, long OriginalSize, long CompressedSize, long Triangles)
    {
        public bool IsComputed => OriginalSize > 0 && CompressedSize > 0;
        public async Task ComputeSizes(FileCacheManager fileCacheManager, CancellationToken token)
        {
            var compressedSize = await fileCacheManager.GetCompressedFileData(Hash, token).ConfigureAwait(false);
            var normalSize = new FileInfo(FilePaths[0]).Length;
            var entries = fileCacheManager.GetAllFileCachesByHash(Hash, ignoreCacheEntries: true, validate: false);
            foreach (var entry in entries)
            {
                entry.Size = normalSize;
                entry.CompressedSize = compressedSize.Item2.LongLength;
            }
            OriginalSize = normalSize;
            CompressedSize = compressedSize.Item2.LongLength;
        }
        public long OriginalSize { get; private set; } = OriginalSize;
        public long CompressedSize { get; private set; } = CompressedSize;
        public long Triangles { get; private set; } = Triangles;

        public Lazy<string> Format = new(() =>
        {
            switch (FileType)
            {
                case "tex":
                    {
                        try
                        {
                            using var stream = new FileStream(FilePaths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var reader = new BinaryReader(stream);
                            reader.BaseStream.Position = 4;
                            var format = (TexFile.TextureFormat)reader.ReadInt32();
                            return format.ToString();
                        }
                        catch
                        {
                            return "Unknown";
                        }
                    }
                default:
                    return string.Empty;
            }
        });
    }
}