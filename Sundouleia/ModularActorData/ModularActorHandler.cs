using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.ModularActorData;

/// <summary>
///     Stores and handles the currently loaded ModularActorData 
///     objects, and their rendered entries, removing them on object 
///     destruction, and monitoring GPose state for cleanup as needed.
/// </summary> 
public class ModularActorHandler : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly CharaObjectWatcher _watcher;

    // Data we have loaded in that can be applied in GPose.
    private readonly HashSet<ModularActorData> _loadedActorData = [];

    // Data that is currently applied to a character in GPose, keyed by their pointer.
    private readonly Dictionary<nint, HandledActorDataEntry> _handledActors = [];

    public ModularActorHandler(ILogger<ModularActorHandler> logger, SundouleiaMediator mediator,
        MainConfig mainConfig, ModularActorsConfig smaConfig, FileCacheManager cacheManager, 
        SMAFileCacheManager smaFileCache, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _watcher = watcher;

        Mediator.Subscribe<GPoseEndMessage>(this, _ =>
        {
            // Revert all handled actors.
            foreach (var (actorAddr, actorData) in _handledActors)
                RevertHandledActor(actorData).ConfigureAwait(false);
            // bomb the loaded actor data.
            _loadedActorData.Clear();
            // Cleanup 
        });

        // We likely dont need this if we properly handle gpose objects in the watcher.
        // Svc.Framework.Update += OnTick;

        // Otherwise should do a check to ensure disposed gpose actors are cleaned up properly!!!
    }

    public IEnumerable<ModularActorData> ProcessedData => _loadedActorData;
    public IReadOnlyDictionary<nint, HandledActorDataEntry> HandledGPoseActors => _handledActors;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var (handledAddr, handledActor) in _handledActors)
            RevertHandledActor(handledActor).ConfigureAwait(false);
        // clear all loaded data.
        _loadedActorData.Clear();
    }

    public async Task RevertHandledActor(HandledActorDataEntry handledEntry)
    {
        // Do all the fancy revert voodoo here and stuff.
    }

    // Do a revert by name or whatever idk.
    public async Task<bool> RevertHandledActor(string actorName)
    {
        // Find entry with matching name?
        return false;
    }

    // For actors loaded in but not yet applied.
    internal bool AddProcessedActorData(ModularActorData actorData)
        => _loadedActorData.Add(actorData);

    // For actors that were loaded in and have now applied themselves to an actor.
    internal bool AddHandledActor(nint actorPtr, HandledActorDataEntry handledEntry)
        => _handledActors.TryAdd(actorPtr, handledEntry);
}