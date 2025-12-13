using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.ModularActor;

/// <summary>
///     Handles the lifetime of imported modular actor data 
///     and their application state on GPose actors.
/// </summary> 
public class GPoseActorHandler : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _mainConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    // Data that is currently applied to a character in GPose, keyed by their pointer.
    private readonly Dictionary<nint, HandledActorDataEntry> _handledActors = new();

    public GPoseActorHandler(ILogger<GPoseActorHandler> logger, SundouleiaMediator mediator,
        MainConfig mainConfig, FileCacheManager cacheManager, SMAFileCacheManager smaFileCache, 
        IpcManager ipc, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _mainConfig = mainConfig;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _ipc = ipc;
        _watcher = watcher;

        Mediator.Subscribe<GPoseObjectDestroyed>(this, _ =>
        {
            Logger.LogInformation($"Handled Addresses at time of GPoseObjectDestroyed: " +
                $"{string.Join("\n", _handledActors.Select(kvp => $"{kvp.Key:X} ({kvp.Value.DisplayName})"))}");

            if (_handledActors.Remove(_.Address, out var entry))
            {
                Logger.LogInformation($"GPoseObject at address {_.Address:X} destroyed, removing {entry.DisplayName}.");
                RevertInternal(entry).ConfigureAwait(false);
            }
            else
            {
                Logger.LogInformation($"GPoseObject at address {_.Address:X} destroyed, no handled actor found.");
            }
        });
    }

    public unsafe GameObject* GPoseTarget
    {
        get => TargetSystem.Instance()->GPoseTarget;
        set => TargetSystem.Instance()->GPoseTarget = value;
    }

    public unsafe bool HasGPoseTarget => GPoseTarget != null;
    public unsafe int GPoseTargetIdx => !HasGPoseTarget ? -1 : GPoseTarget->ObjectIndex;

    public IReadOnlyDictionary<nint, HandledActorDataEntry> HandledGPoseActors => _handledActors;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        RemoveAll();
    }

    internal bool AddActor(HandledActorDataEntry newEntry)
        => _handledActors.TryAdd(newEntry.ObjectAddress, newEntry);

    internal async Task<bool> RemoveActor(HandledActorDataEntry toRemove)
    {
        if (!_handledActors.Remove(toRemove.ObjectAddress, out var removed))
        {
            Logger.LogWarning($"Tried to remove actor {toRemove.DisplayName} that was not handled.");
            return false;
        }
        // Revert the actor.
        await RevertInternal(removed).ConfigureAwait(false);
        return true;
    }

    // Do a revert by name or whatever idk.
    public async Task<bool> RemoveActor(string actorName)
    {
        if (_handledActors.Values.FirstOrDefault(e => e.ActorName == actorName) is not { } entry)
            return false;

        await RemoveActor(entry).ConfigureAwait(false);
        return true;
    }

    private void RemoveAll()
    {
        Logger.LogInformation("Removing all handled GPose actors.");
        foreach (var (handledAddr, handledActor) in _handledActors)
            RemoveActor(handledActor).ConfigureAwait(false);
    }

    private async Task RevertInternal(HandledActorDataEntry entry)
    {
        // Revert the glamourer.
        Logger.LogInformation($"Reverting Glamourer for actor {entry.DisplayName}.");
        await _ipc.Glamourer.ReleaseByName(entry.ActorName).ConfigureAwait(false);
        // If CPlus is valid, revert it.
        if (entry.CPlusId != null)
        {
            Logger.LogInformation($"Reverting CustomizePlus for actor {entry.DisplayName}.");
            await _ipc.CustomizePlus.RevertTempProfile(entry.CPlusId).ConfigureAwait(false);
        }

        // Bomb the temporary collection.
        Logger.LogInformation($"Removing Sundesmo collection for actor {entry.DisplayName}.");
        await _ipc.Penumbra.RemoveSundesmoCollection(entry.CollectionId).ConfigureAwait(false);
        // Redraw the actor.
        if (entry.IsValid)
            unsafe { _ipc.Penumbra.RedrawGameObject(entry.GPoseObject->ObjectIndex); }
    }
}