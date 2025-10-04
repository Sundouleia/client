using CkCommons;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.BannerHelper.Delegates;

namespace Sundouleia.Services;

/// <summary> 
///     Tracks when sundesmos go online/offline, and visible/invisible. <para />
///     Reliably tracks when offline/unrendered sundesmos are fully timed out or
///     experiencing a breif reconnection / timeout, to prevent continuously redrawing data. <para />
///     This additionally handles updates regarding when we send out changes to other sundesmos.
/// </summary>
public sealed class DistributionService : DisposableMediatorSubscriberBase
{
    // likely file sending somewhere in here.
    private readonly MainHub _hub;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    // Management for the task involving making an update to our latest data.
    // If this is ever processing, we should await it prior to distributing data.
    // This way we make sure that when we do distribute the data, it has the latest information.
    private CancellationTokenSource _latestDataUpdateCTS = new();
    private Task? _latestDataUpdateTask;

    // Task runs the distribution of our data to other sundesmos.
    // should always await the above task, if active, before firing.
    private CancellationTokenSource _distributeDataCTS = new();
    private Task? _distributeDataTask;

    // It is possible that using a semaphore slim would allow us to push updates as-is even if mid-dataUpdate,
    // however this would also push sundesmos out of sync and would need to be updated later anyways, so do not think it's worth.

    // Latest private data state.
    private List<string> _lastSentModHashes = [];
    private IpcDataPlayerCache _lastOwnIpc = new();
    private IpcDataCache _lastMinionMountIpc = new();
    private IpcDataCache _lastPetIpc = new();
    private IpcDataCache _lastBuddyIpc = new();

    public DistributionService(ILogger<DistributionService> logger, SundouleiaMediator mediator,
        MainHub hub, IpcManager ipc, SundesmoManager pairs, CharaObjectWatcher ownedObjects)
        : base(logger, mediator)
    {
        _hub = hub;
        _ipc = ipc;
        _sundesmos = pairs;
        _watcher = ownedObjects;

        Mediator.Subscribe<SundesmoOffline>(this, msg => OnSundesmoDisconnected(msg.Sundesmo));
        Mediator.Subscribe<SundesmoPlayerRendered>(this, msg => OnSundesmoRendered(msg.Handler));
        Mediator.Subscribe<SundesmoPlayerUnrendered>(this, msg => OnSundesmoUnrendered(msg.Handler));
        Mediator.Subscribe<SundesmoTimedOut>(this, msg => OnSundesmoTimedOut(msg.Handler));

        // Process connections.
        Mediator.Subscribe<ConnectedMessage>(this, _ => OnHubConnected());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => NewVisible.Clear());

        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, _ => UpdateCheck());
    }

    public List<UserData> NewVisibleNoLimbo => _sundesmos.GetVisibleConnected().Except(NewVisible).ToList();
    public List<UserData> SundesmosForUpdatePush => _sundesmos.GetVisibleConnected().Except([.. InLimbo, .. NewVisible]).ToList();

    public HashSet<UserData> NewVisible { get; private set; } = new();
    public HashSet<UserData> InLimbo { get; private set; } = new();
    public bool ProcessingCacheUpdate => _latestDataUpdateTask is not null && !_latestDataUpdateTask.IsCompleted;

    // can use if we have edge cases to deal with.
    private void OnSundesmoConnected(Sundesmo sundesmo)
    {
        // if a sundesmo connects, there is a change that they reconnected but their rendered state was valid or was timed out,
        // this edge case can be handled here if it ever comes up.
    }

    // If in limbo, then they should leave limbo upon DC.
    private void OnSundesmoDisconnected(Sundesmo sundesmo)
        => InLimbo.Remove(sundesmo.UserData);

    // If they were in limbo, they still have the latest data, so do nothing. Otherwise, add to new visible.
    private void OnSundesmoRendered(PlayerHandler handler)
    {
        if (InLimbo.Remove(handler.Sundesmo.UserData)) return;
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} rendered, adding to new visible.", LoggerType.PairVisibility);
        NewVisible.Add(handler.Sundesmo.UserData);
    }

    private void OnSundesmoUnrendered(PlayerHandler handler)
    {
        if (!handler.Sundesmo.IsOnline) return;
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} unrendered but is still online. Adding to limbo.", LoggerType.PairVisibility);
        InLimbo.Add(handler.Sundesmo.UserData);
    }

    // Remove the sundesmo from the limbo hashset, so we send them a full update next time.
    private void OnSundesmoTimedOut(PlayerHandler handler)
    {
        InLimbo.Remove(handler.Sundesmo.UserData);
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} timed out, removing from limbo.", LoggerType.PairVisibility);
    }

    // Only entry point where we ignore timeout states.
    // If this gets abused through we can very easily add timeout functionality here too.
    private async void OnHubConnected()
    {
        // Cancel any current data update tasks.
        _latestDataUpdateCTS = _latestDataUpdateCTS.SafeCancelRecreate();
        _latestDataUpdateTask = Task.Run(SetInitialCache, _latestDataUpdateCTS.Token);
        // await the task.
        await _latestDataUpdateTask.ConfigureAwait(false);
        Logger.LogInformation("Initial Ipc Cache fetched after reconnection. Sending off to visible users.");
        // Send off to all visible users.

        // cancel any current update task as this always takes priority.
        _distributeDataCTS = _distributeDataCTS.SafeCancelRecreate();
        _distributeDataTask = Task.Run(async () =>
        {
            var modData = new SentModUpdate(new List<ModFileInfo>(), new List<string>()); // placeholder.
            var appearance = new VisualUpdate()
            {
                PlayerChanges = _lastOwnIpc.ToUpdateApi(),
                MinionMountChanges = _lastMinionMountIpc.ToUpdateApi(),
                PetChanges = _lastPetIpc.ToUpdateApi(),
                CompanionChanges = _lastBuddyIpc.ToUpdateApi(),
            };
            // bomb the other data such as new users and limbo users.
            InLimbo.Clear();
            NewVisible.Clear();
            var visible = _sundesmos.GetVisibleConnected();
            await _hub.UserPushIpcFull(new(visible, modData, appearance)).ConfigureAwait(false);
            Logger.LogInformation($"Sent initial Ipc Cache to {visible.Count} users after reconnection. 0 Files needed uploading.");
        }, _distributeDataCTS.Token);
    }

    // Note that we are going to need some kind of logic for handling the edge cases where user A is receiving a new update and that 
    private void UpdateCheck()
    {
        // If there is anyone to push out updates to, do so.
        if (NewVisible.Count is 0)
            return;

        // If we are zoning or not available, do not process any updates from us.
        if (PlayerData.IsZoning || !PlayerData.Available || MainHub.IsConnected)
            return;

        // Do not process the task if we are currently updating our latest data.
        if (_latestDataUpdateTask is not null && !_latestDataUpdateTask.IsCompleted)
            return;

        // If we are already distributing data, do not start another distribution.
        if (_distributeDataTask is not null && !_distributeDataTask.IsCompleted)
            return;

        // Process a distribution of full data to the newly visible users and then clear the update.
        // (we could use a semaphore here but can forget it for now.)
        _distributeDataTask = Task.Run(async () =>
        {
            var modData = new SentModUpdate(new List<ModFileInfo>(), new List<string>()); // placeholder.
            var appearance = new VisualUpdate()
            {
                PlayerChanges = _lastOwnIpc.ToUpdateApi(),
                MinionMountChanges = _lastMinionMountIpc.ToUpdateApi(),
                PetChanges = _lastPetIpc.ToUpdateApi(),
                CompanionChanges = _lastBuddyIpc.ToUpdateApi(),
            };
            // grab the new visible sundesmos not in limbo state.
            var toSend = NewVisibleNoLimbo.ToList();
            // await the full send.
            await _hub.UserPushIpcFull(new(toSend, modData, appearance)).ConfigureAwait(false);
            Logger.LogInformation($"Full Ipc Cache sent to {toSend.Count} newly visible users. 0 Files needed uploading.");
            // by this point we will have which files needed to be uploaded and we can handle that here.\
            // best to as well, since we have the users to update.
            // But if we do need to, we can file it off this task as fire-and-forget since it is just for these sundesmos.

            // Remove the ones we sent the data to from the new visible list.
            NewVisible.ExceptWith(toSend);
        }, _distributeDataCTS.Token);
    }

    private async Task SetInitialCache()
    {
        // Collect all data from all sources about all activities to get our current state.
        var playerCache = new IpcDataPlayerCache();
        playerCache.Data[IpcKind.ModManips] = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
        playerCache.Data[IpcKind.Glamourer] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
        playerCache.Data[IpcKind.CPlus] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
        playerCache.Data[IpcKind.Heels] = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
        playerCache.Data[IpcKind.Moodles] = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
        playerCache.Data[IpcKind.Honorific] = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
        playerCache.Data[IpcKind.PetNames] = _ipc.PetNames.GetPetNicknames() ?? string.Empty;

        var minionMountCache = new IpcDataCache();
        minionMountCache.Data[IpcKind.Glamourer] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
        minionMountCache.Data[IpcKind.CPlus] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;

        var petCache = new IpcDataCache();
        petCache.Data[IpcKind.Glamourer] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
        petCache.Data[IpcKind.CPlus] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
       
        var buddyCache = new IpcDataCache();
        buddyCache.Data[IpcKind.Glamourer] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
        buddyCache.Data[IpcKind.CPlus] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;

        _lastOwnIpc = playerCache;
        _lastMinionMountIpc = minionMountCache;
        _lastPetIpc = petCache;
        _lastBuddyIpc = buddyCache;
        _lastSentModHashes = new List<string>();
    }


    #region Cache Updates
    // if it is a full update we do not care about the changes,
    // only that the cache is updated prior to the change.
    public void UpdateIpcCacheFull(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        if (ProcessingCacheUpdate)
            throw new Exception("Update already Processing!");

        // Assign the update task with the token. (not sure yet how we will handling cancellation if we do)
        _latestDataUpdateTask = Task.Run(async () =>
        {
            // Should process both the mod and ipc updates within this method.
            // If feeling slow can always run this together but might be best to run seperately.
            var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcFull(new(SundesmosForUpdatePush, modChanges, visualChanges)).ConfigureAwait(false);
            Logger.LogInformation($"Ipc Cache Full Update completed.");
            // Clear the limbo list as they will need a full update next time.
            InLimbo.Clear();
        }, _latestDataUpdateCTS.Token);
    }

    public void UpdateModCache()
    {
        if (ProcessingCacheUpdate)
            throw new Exception("Update already Processing!");
        // Assign the update task with the token. (not sure yet how we will handling cancellation if we do)
        _latestDataUpdateTask = Task.Run(async () =>
        {
            var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcMods(new(SundesmosForUpdatePush, modChanges)).ConfigureAwait(false);
            // Clear the limbo list as they will need a full update next time.
            InLimbo.Clear();
            Logger.LogInformation($"Ipc Cache Mod Update completed.");
        }, _latestDataUpdateCTS.Token);
    }

    public void UpdateIpcCache(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        if (ProcessingCacheUpdate)
            throw new Exception("Update already Processing!");
        // Assign the update task with the token. (not sure yet how we will handling cancellation if we do)
        _latestDataUpdateTask = Task.Run(async () =>
        {
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);
            // If no change, do not push.
            if (!visualChanges.HasData())
                return;

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcOther(new(SundesmosForUpdatePush, visualChanges)).ConfigureAwait(false);
            Logger.LogInformation($"Ipc Cache Visuals Update completed.");
            // Clear the limbo list as they will need a full update next time.
            InLimbo.Clear();
        }, _latestDataUpdateCTS.Token);
    }

    public void UpdateIpcCacheSingle(OwnedObject obj, IpcKind type)
    {
        if (ProcessingCacheUpdate)
            throw new Exception("Update already Processing!");
        // Assign the update task with the token. (not sure yet how we will handling cancellation if we do)
        _latestDataUpdateTask = Task.Run(async () =>
        {
            (bool changed, string? data) = await UpdateIpcCacheInternal(obj, type).ConfigureAwait(false);
            if (!changed || data is null)
                return;
            // Things changed, inform of update.

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcSingle(new(SundesmosForUpdatePush, obj, type, data)).ConfigureAwait(false);
            Logger.LogInformation($"Ipc Cache Single Update completed for {obj} - {type}.");
            // Clear the limbo list as they will need a full update next time.
            InLimbo.Clear();
        }, _latestDataUpdateCTS.Token);
    }

    private async Task<SentModUpdate> UpdateModCacheInternal()
    {
        // do some kind of file scan voodoo with our db and transient resource handler to grab the
        // latest active hashes for our character.
        return new SentModUpdate(new List<ModFileInfo>(), new List<string>());
    }

    // This task should not in any way be cancelled as it is important we finish it.
    private async Task<VisualUpdate> UpdateIpcCacheInternal(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        var finalChanges = new VisualUpdate();
        // process the tasks for each object in parallel.
        var tasks = new List<Task>();
        foreach (var (obj, kinds) in newChanges)
            tasks.Add(obj switch
            {
                OwnedObject.Player => TryUpdatePlayerCache(kinds, _lastOwnIpc, finalChanges),
                OwnedObject.MinionOrMount => UpdateNonPlayerCache(obj, kinds, _lastMinionMountIpc, finalChanges),
                OwnedObject.Pet => UpdateNonPlayerCache(obj, kinds, _lastPetIpc, finalChanges),
                OwnedObject.Companion => UpdateNonPlayerCache(obj, kinds, _lastBuddyIpc, finalChanges),
                _ => Task.FromResult(false)
            });
        // Execute in parallel.
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return finalChanges;
    }

    private async Task<(bool, string?)> UpdateIpcCacheInternal(OwnedObject obj, IpcKind type)
    {
        var dataStr = type switch
        {
            IpcKind.ModManips => _ipc.Penumbra.GetMetaManipulationsString(),
            IpcKind.Glamourer => await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.CPlus => await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.Heels => await _ipc.Heels.GetClientOffset().ConfigureAwait(false),
            IpcKind.Moodles => await _ipc.Moodles.GetOwn().ConfigureAwait(false),
            IpcKind.Honorific => await _ipc.Honorific.GetTitle().ConfigureAwait(false),
            IpcKind.PetNames => _ipc.PetNames.GetPetNicknames(),
            _ => string.Empty,
        };
        // Update accordingly.
        if (obj is OwnedObject.Player)
            return (_lastOwnIpc.UpdateCacheSingle(type, dataStr), dataStr);
        else
        {
            var cache = obj switch
            {
                OwnedObject.MinionOrMount => _lastMinionMountIpc,
                OwnedObject.Pet => _lastPetIpc,
                OwnedObject.Companion => _lastBuddyIpc,
                _ => null
            };
            if (cache is null)
                return (false, null);
            // update and return if changed.
            return (cache.UpdateCacheSingle(type, dataStr), dataStr);
        }
    }

    #endregion Cache Updates

    #region Cache Update Helpers
    private async Task TryUpdatePlayerCache(IpcKind toUpdate, IpcDataPlayerCache current, VisualUpdate compiledData)
    {
        var newData = new IpcDataPlayerUpdate(IpcKind.None);
        var applied = IpcKind.None;
        if (toUpdate.HasAny(IpcKind.ModManips))
        {
            var manipStr = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.ModManips, manipStr) ? IpcKind.ModManips : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.Glamourer))
        {
            var glamStr = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.Glamourer, glamStr) ? IpcKind.Glamourer : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.CPlus))
        {
            var cplusStr = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.CPlus, cplusStr) ? IpcKind.CPlus : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.Heels))
        {
            var heelsStr = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.Heels, heelsStr) ? IpcKind.Heels : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.Honorific))
        {
            var titleStr = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.Honorific, titleStr) ? IpcKind.Honorific : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.Moodles))
        {
            var moodleStr = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.Moodles, moodleStr) ? IpcKind.Moodles : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.PetNames))
        {
            var petStr = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.PetNames, petStr) ? IpcKind.PetNames : IpcKind.None;
        }
        // current cache is already updated, so return the the new data.
        if (applied != IpcKind.None)
        {
            newData = newData with { Updates = applied };
            compiledData.PlayerChanges = newData;
        }
    }

    private async Task UpdateNonPlayerCache(OwnedObject obj, IpcKind toUpdate, IpcDataCache current, VisualUpdate compiledData)
    {
        var newData = new IpcDataUpdate(IpcKind.None);
        var applied = IpcKind.None;
        if (toUpdate.HasAny(IpcKind.Glamourer))
        {
            var glamStr = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.Glamourer, glamStr) ? IpcKind.Glamourer : IpcKind.None;
        }
        if (toUpdate.HasAny(IpcKind.CPlus))
        {
            var cplusStr = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;
            applied |= current.UpdateCacheSingle(IpcKind.CPlus, cplusStr) ? IpcKind.CPlus : IpcKind.None;
        }
        // if anything changed, add to the compiled data.
        if (applied != IpcKind.None)
        {
            newData = newData with { Updates = applied };
            switch (obj)
            {
                case OwnedObject.MinionOrMount: compiledData.MinionMountChanges = newData; break;
                case OwnedObject.Pet: compiledData.PetChanges = newData; break;
                case OwnedObject.Companion: compiledData.CompanionChanges = newData; break;
            }
        }
    }
    #endregion Cache Update Helpers
}
