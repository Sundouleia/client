using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;

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
    private readonly MainConfig _config;
    private readonly MainHub _hub;
    private readonly PlzNoCrashFrens _noCrashPlz;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly FileCacheManager _cacheManager;
    private readonly TransientResourceManager _transients;
    private readonly CharaObjectWatcher _watcher;

    // Management for the task involving making an update to our latest data.
    // If this is ever processing, we should await it prior to distributing data.
    // This way we make sure that when we do distribute the data, it has the latest information.
    private readonly SemaphoreSlim _dataUpdateLock = new(1, 1);

    // Task runs the distribution of our data to other sundesmos.
    // should always await the above task, if active, before firing.
    private readonly SemaphoreSlim _distributionLock = new(1, 1);
    private CancellationTokenSource _distributeDataCTS = new();
    private Task? _distributeDataTask;

    // It is possible that using a semaphore slim would allow us to push updates as-is even if mid-dataUpdate,
    // however this would also push sundesmos out of sync and would need to be updated later anyways, so do not think it's worth.
    private ClientDataCache? _lastCreatedData = null;

    // Latest private data state.
    private List<string> _lastSentModHashes = [];
    private IpcDataPlayerCache _lastOwnIpc = new();
    private IpcDataCache _lastMinionMountIpc = new();
    private IpcDataCache _lastPetIpc = new();
    private IpcDataCache _lastBuddyIpc = new();

    public bool UpdatingData => _dataUpdateLock.CurrentCount is 0;
    public bool DistributingData => _distributionLock.CurrentCount is 0; // only use if we dont want to cancel distributions.

    public DistributionService(ILogger<DistributionService> logger, SundouleiaMediator mediator,
        MainConfig config, MainHub hub, PlzNoCrashFrens noCrashPlz, IpcManager ipc, SundesmoManager pairs,
        FileCacheManager cacheManager, TransientResourceManager transients, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _hub = hub;
        _noCrashPlz = noCrashPlz;
        _ipc = ipc;
        _sundesmos = pairs;
        _cacheManager = cacheManager;
        _transients = transients;
        _watcher = watcher;

        Mediator.Subscribe<SundesmoOnline>(this, msg => OnSundesmoConnected(msg.Sundesmo));
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


    // When a sundesmo connects there is a chance that they are already rendered. If so we should update them here.
    private void OnSundesmoConnected(Sundesmo sundesmo)
    {
        if (!sundesmo.PlayerRendered)
            return;
        Logger.LogDebug($"Sundesmo ({sundesmo.PlayerName}) connected and is already rendered. Adding to new visible.", LoggerType.PairVisibility);
        // we only do this for when going online and are visible, it will not work the other way around though.
        // when we connect we would only send it to the new visible users who are not part of the existing ones.
        // (by this i mean when we dc we effectively place all visible sundesmos in limbo, so that on reconnect we only send back
        // out to the new users not in the old users if no change was made.)
        NewVisible.Add(sundesmo.UserData);
    }

    // If in limbo, then they should leave limbo upon DC.
    private void OnSundesmoDisconnected(Sundesmo sundesmo)
    {
        // If this is true it means they were in limbo due to being unrendered but still online, and went offline, satisfying both conditions.
        if (InLimbo.Remove(sundesmo.UserData))
            return;
        // Otherwise they have disconnected but are not in limbo, so we should add them into limbo.
        Logger.LogDebug($"Sundesmo ({sundesmo.PlayerName}) disconnected while visible. Adding to limbo.", LoggerType.PairVisibility);
        InLimbo.Add(sundesmo.UserData);
    }
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
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} timed out, removing from limbo.", LoggerType.PairVisibility);
        InLimbo.Remove(handler.Sundesmo.UserData);
    }

    // Only entry point where we ignore timeout states.
    // If this gets abused through we can very easily add timeout functionality here too.
    private async void OnHubConnected()
    {
        // Ensure we wait for the previous update to finish if it is running.
        await _dataUpdateLock.WaitAsync();
        try
        {
            // Not set the initialCache.
            Logger.LogInformation("Hub connected, fetching initial Ipc Cache.");
            await SetInitialCache().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during OnHubConnected: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }

        // Send off to all visible users after awaiting for any other distribution task to process.
        Logger.LogInformation("Distributing initial Ipc Cache to visible sundesmos.");
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
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
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
        if (PlayerData.IsZoning || !PlayerData.Available || !MainHub.IsConnected)
            return;

        // Do not process the task if we are currently updating our latest data.
        if (UpdatingData)
            return;

        // If we are already distributing data, do not start another distribution.
        if (_distributeDataTask is not null && !_distributeDataTask.IsCompleted)
            return;

        // Process a distribution of full data to the newly visible users and then clear the update.
        // (we could use a semaphore here but can forget it for now.)
        _distributeDataTask = Task.Run(async () =>
        {
            using var ct = new CancellationTokenSource();
            ct.CancelAfter(10000);
            await CollectModdedState(ct.Token).ConfigureAwait(false);
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
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
            NewVisible.Clear();
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

        using var ct = new CancellationTokenSource();
        ct.CancelAfter(10000);
        await CollectModdedState(ct.Token).ConfigureAwait(false);

        _lastOwnIpc = playerCache;
        _lastMinionMountIpc = minionMountCache;
        _lastPetIpc = petCache;
        _lastBuddyIpc = buddyCache;
        _lastSentModHashes = new List<string>();
    }

    private async Task CollectModdedState(CancellationToken ct)
    {
        if (!_config.HasValidCacheFolderSetup())
        {
            Logger.LogWarning("Cache Folder not setup! Cannot process mod files!");
            return;
        }

        // await until we are present and visible.
        await SundouleiaEx.WaitForPlayerLoading().ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // A lot of unessisary calculation work that needs to be done simply because people like the idea of custom skeletons,
        // and.... they tend to crash other people :D
        Dictionary<string, List<ushort>>? boneIndices;
        unsafe { boneIndices = _noCrashPlz.GetSkeletonBoneIndices((GameObject*)_watcher.WatchedPlayerAddr); }

        // Obtain our current on-screen actor state.
        if (await _ipc.Penumbra.GetCharacterResourcePathData(0).ConfigureAwait(false) is not { } onScreenPaths)
            throw new InvalidOperationException("Penumbra returned null data");

        // Set the file replacements to all currently visible paths on our player data, where they are modded.
        var moddedPaths = new HashSet<ModdedFile>(onScreenPaths.Select(c => new ModdedFile([.. c.Value], c.Key)), ModdedFileComparer.Instance).Where(p => p.HasFileReplacement).ToHashSet();
        // Remove unsupported filetypes.
        moddedPaths.RemoveWhere(c => c.GamePaths.Any(g => !Constants.ValidExtensions.Any(e => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));

        // If we wished to abort then abort.
        ct.ThrowIfCancellationRequested();

        // Log the remaining files. (without checking for transients.
        Logger.LogDebug("== Static Replacements ==");
        foreach (var replacement in moddedPaths.OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            Logger.LogDebug($"=> {replacement}");
            ct.ThrowIfCancellationRequested();
        }

        // At this point we would want to add any pet resources as transient resources,
        // then clear them from this list (if we even need to)


        // Removes any resources caught from fetching the on-screen actor resources from that which loaded as transient (glowing armor vfx ext)
        // any remaining transients for this OwnedObject are marked as PersistantTransients and returned.
        var persistants = _transients.ClearTransientsAndGetPersistants(OwnedObject.Player, moddedPaths.SelectMany(c => c.GamePaths).ToList());

        // For these paths, get their file replacement objects.
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(persistants).ConfigureAwait(false);
        Logger.LogDebug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new ModdedFile([.. c.Value], c.Key)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            Logger.LogDebug("=> {repl}", replacement);
            moddedPaths.Add(replacement);
        }

        _transients.RemoveUnmoddedPersistantTransients(OwnedObject.Player, [.. moddedPaths]);
        // obtain the final moddedFiles to send that is the result.
        moddedPaths = new HashSet<ModdedFile>(moddedPaths.Where(p => p.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), ModdedFileComparer.Instance);
        ct.ThrowIfCancellationRequested();

        // All remaining paths that are not fileswaps come from modded game files that need to be sent over sundouleia servers.
        // To authorize them we need their 40 character SHA1 computed hashes from their file data.
        var toCompute = moddedPaths.Where(f => !f.IsFileSwap).ToArray();
        Logger.LogDebug($"Computing hashes for {toCompute.Length} files.");
        // Grab these hashes via the FileCacheEntity .
        var computedPaths = _cacheManager.GetFileCachesByPaths(toCompute.Select(c => c.ResolvedPath).ToArray());
        // Ensure we set and log said computed hashes.
        foreach (var file in toCompute)
        {
            ct.ThrowIfCancellationRequested();
            file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
            Logger.LogDebug($"=> {file} (Hash: {file.Hash})");
        }

        // Finally as a santity check, remove any invalid file hashes for files that are no longer valid.
        var removed = moddedPaths.RemoveWhere(f => !f.IsFileSwap && string.IsNullOrEmpty(f.Hash));
        if (removed > 0) 
            Logger.LogDebug($"=> Removed {removed} invalid file hashes.");

        // Final throw check.
        ct.ThrowIfCancellationRequested();
        // This is original if (OwnedObject is OwnedObject.Player), can fix later.
        if (true)
        {
            // Helps ensure we are not going to send files that will crash people yippee
            await Generic.Safe(async () => await _noCrashPlz.VerifyPlayerAnimationBones(boneIndices, moddedPaths, ct).ConfigureAwait(false));
        }
    }

    // Obtains the file replacements for the set of given paths that have forward and reverse resolve.
    private async Task<IReadOnlyDictionary<string, string[]>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve)
    {
        var forwardPaths = forwardResolve.ToArray();
        string[] reversePaths = Array.Empty<string>();

        // track our resolved paths.
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);

        // grab the reverse paths for the forward paths.
        Logger.LogDebug("== Resolving Forward Paths ==");
        // grab them from penumbra. (causes a 20ms delay becuz ipc life)
        var (forward, reverse) = await _ipc.Penumbra.ResolveModPaths(forwardPaths, reversePaths).ConfigureAwait(false);
        for (int i = 0; i < forwardPaths.Length; i++)
        {
            var filePath = forward[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
                list.Add(forwardPaths[i].ToLowerInvariant());
            else
                resolvedPaths[filePath] = [forwardPaths[i].ToLowerInvariant()];
        }

        Logger.LogDebug("== Resolving Reverse Paths ==");
        for (int i = 0; i < reversePaths.Length; i++)
        {
            var filePath = reversePaths[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
                list.AddRange(reverse[i].Select(c => c.ToLowerInvariant()));
            else
                resolvedPaths[filePath] = reverse[i].Select(c => c.ToLowerInvariant()).ToList();
        }
        Logger.LogDebug("== Resolved Paths ==");
        foreach (var kvp in resolvedPaths.OrderBy(k => k.Key, StringComparer.Ordinal))
            Logger.LogDebug($"=> {kvp.Key} : {string.Join(", ", kvp.Value)}");
        return resolvedPaths.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase).AsReadOnly();
    }

    #region Cache Updates
    // if it is a full update we do not care about the changes,
    // only that the cache is updated prior to the change.
    public async Task UpdateIpcCacheFull(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        // await for the next process to become available.
        await _dataUpdateLock.WaitAsync();
        // perform the update.
        try
        {
            // Should process both the mod and ipc updates within this method.
            // If feeling slow can always run this together but might be best to run seperately.
            var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcFull(new(SundesmosForUpdatePush, modChanges, visualChanges)).ConfigureAwait(false);
            Logger.LogInformation($"Ipc Cache Full Update completed.");
            // Clear the limbo list as they will need a full update next time.
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateIpcCacheFull: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }
    }

    public async Task UpdateModCache()
    {
        // await for the next process to become available.
        await _dataUpdateLock.WaitAsync();
        // perform the update.
        try
        {
            // var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            //// Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            //await _hub.UserPushIpcMods(new(SundesmosForUpdatePush, modChanges)).ConfigureAwait(false);
            //Logger.LogInformation($"Ipc Cache Mod Update completed.");
            //// Clear the limbo list as they will need a full update next time.
            //if (InLimbo.Count is not 0)
            //{
            //    Logger.LogDebug("Clearing limbo as we have a new state.");
            //    InLimbo.Clear();
            //}
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateModCache: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }
    }

    public async Task UpdateIpcCache(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        // await for the next process to become available.
        await _dataUpdateLock.WaitAsync();
        // perform the update.
        try
        {
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);
            if (!visualChanges.HasData())
            {
                Logger.LogInformation($"Ipc Cache Visuals Update found no changes, skipping push.");
                return;
            }

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            var toSend = SundesmosForUpdatePush;
            await _hub.UserPushIpcOther(new(toSend, visualChanges)).ConfigureAwait(false);
            Logger.LogInformation($"Pushed Visual IpcCache update to {toSend.Count} sundesmos. ({visualChanges.ToChangesString()})");
            // Clear the limbo list as they will need a full update next time.
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateIpcCache: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }
    }

    public async Task UpdateIpcCacheSingle(OwnedObject obj, IpcKind type)
    {
        // await for the next process to become available.
        await _dataUpdateLock.WaitAsync();
        // perform the update.
        try
        {
            (bool changed, string? data) = await UpdateIpcCacheInternal(obj, type).ConfigureAwait(false);
            if (!changed || data is null)
            {
                Logger.LogInformation($"IpcCacheSingle ({obj})({type}) had no changes, skipping.");
                return;
            }
            // Things changed, inform of update.

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            var toSend = SundesmosForUpdatePush;
            await _hub.UserPushIpcSingle(new(toSend, obj, type, data)).ConfigureAwait(false);
            Logger.LogInformation($"Pushed IpcCacheSingle update to {toSend.Count} sundesmos. ({obj})({type})");
            // Clear the limbo list as they will need a full update next time.
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateIpcCacheSingle: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }
    }

    private async Task<SentModUpdate> UpdateModCacheInternal()
    {
        // await CollectModdedState(CancellationToken.None).ConfigureAwait(false);
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
        {
            if (kinds == IpcKind.None)
                continue;

            tasks.Add(obj switch
            {
                OwnedObject.Player => TryUpdatePlayerCache(kinds, _lastOwnIpc, finalChanges),
                OwnedObject.MinionOrMount => UpdateNonPlayerCache(obj, kinds, _lastMinionMountIpc, finalChanges),
                OwnedObject.Pet => UpdateNonPlayerCache(obj, kinds, _lastPetIpc, finalChanges),
                OwnedObject.Companion => UpdateNonPlayerCache(obj, kinds, _lastBuddyIpc, finalChanges),
                _ => Task.FromResult(false)
            });
        }
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
            Logger.LogDebug($"IpcPlayerCache had changes: {applied}");
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
