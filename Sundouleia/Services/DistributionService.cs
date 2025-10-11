using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.Services;

/// <summary> 
///     Tracks when sundesmos go online/offline, and visible/invisible. <para />
///     Reliably tracks when offline/unrendered sundesmos are fully timed out or
///     experiencing a brief reconnection / timeout, to prevent continuously redrawing data. <para />
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
    private readonly FileUploader _fileUploader;
    private readonly TransientResourceManager _transients;
    private readonly CharaObjectWatcher _watcher;

    // Task runs the distribution of our data to other sundesmos.
    // should always await the above task, if active, before firing.
    private readonly SemaphoreSlim _distributionLock = new(1, 1);
    private CancellationTokenSource _distributeDataCTS = new();
    private Task? _distributeDataTask;

    // Management for the task involving making an update to our latest data.
    // If this is ever processing, we should await it prior to distributing data.
    // This way we make sure that when we do distribute the data, it has the latest information.
    private readonly SemaphoreSlim _dataUpdateLock = new(1, 1);
    // Should only be modified while the dataUpdateLock is active.
    private ClientDataCache _lastCreatedData = new();

    // Accessors for the ClientUpdateService
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

        // Process sundesmo state changes.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, msg => NewVisibleUsers.Add(msg.Handler.Sundesmo.UserData));
        Mediator.Subscribe<SundesmoEnteredLimbo>(this, msg => InLimbo.Add(msg.Sundesmo.UserData));
        Mediator.Subscribe<SundesmoLeftLimbo>(this, msg => InLimbo.Remove(msg.Sundesmo.UserData));
        // Process connections.
        Mediator.Subscribe<ConnectedMessage>(this, _ => OnHubConnected());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => NewVisibleUsers.Clear());
        // Process Update Checking.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, _ => UpdateCheck());
    }

    public HashSet<UserData> NewVisibleUsers { get; private set; } = new();
    public HashSet<UserData> InLimbo { get; private set; } = new();

    public List<UserData> SundesmosForUpdatePush => _sundesmos.GetVisibleConnected().Except([.. InLimbo, .. NewVisibleUsers]).ToList();

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
            // Set everything!!!!
            _lastCreatedData.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
            _lastCreatedData.GlamourerState[OwnedObject.Player] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.CPlusState[OwnedObject.Player] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.Moodles = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;

            _lastCreatedData.GlamourerState[OwnedObject.MinionOrMount] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.CPlusState[OwnedObject.MinionOrMount] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;

            _lastCreatedData.GlamourerState[OwnedObject.Pet] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.CPlusState[OwnedObject.Pet] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;

            _lastCreatedData.GlamourerState[OwnedObject.Companion] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            _lastCreatedData.CPlusState[OwnedObject.Companion] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            // TODO: Set modded state (could even run these tasks side by side to complete calculations faster).
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
            var modData = new ModUpdates(new List<ModFile>(), new List<string>());
            var appearance = _lastCreatedData.ToFullUpdate();

            // bomb the other data such as new users and limbo users.
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
            NewVisibleUsers.Clear();
            var visible = _sundesmos.GetVisibleConnected();
            await _hub.UserPushIpcFull(new(visible, modData, appearance)).ConfigureAwait(false);
            Logger.LogInformation($"Sent initial Ipc Cache to {visible.Count} users after reconnection. 0 Files needed uploading.");
        }, _distributeDataCTS.Token);
    }

    // Note that we are going to need some kind of logic for handling the edge cases where user A is receiving a new update and that 
    private void UpdateCheck()
    {
        // If there is anyone to push out updates to, do so.
        if (NewVisibleUsers.Count is 0) return;

        // If we are zoning or not available, do not process any updates from us.
        if (PlayerData.IsZoning || !PlayerData.Available || !MainHub.IsConnected) return;

        // Do not process the task if we are currently updating our latest data.
        if (UpdatingData) return;

        // If we are already distributing data, do not start another distribution.
        if (_distributeDataTask is not null && !_distributeDataTask.IsCompleted) return;

        // Process a distribution of full data to the newly visible users and then clear the update.
        // (we could use a semaphore here but can forget it for now.)
        _distributeDataTask = Task.Run(async () =>
        {
            using var ct = new CancellationTokenSource();
            ct.CancelAfter(10000);
            var currentModdedState = await CollectModdedState(ct.Token).ConfigureAwait(false);
            // Apply the new state to the lastCreatedData and retrieve the mod update dto from it.
            var modData = _lastCreatedData.ApplyNewModState(currentModdedState);
            var appearance = _lastCreatedData.ToFullUpdate();

            // grab the new visible sundesmos not in limbo state.
            var toSend = NewVisibleUsers.ToList();
            if (InLimbo.Count is not 0)
            {
                Logger.LogDebug("Clearing limbo as we have a new state.");
                InLimbo.Clear();
            }
            NewVisibleUsers.Clear();
            // await the full send.
            var res = await _hub.UserPushIpcFull(new(toSend, modData, appearance)).ConfigureAwait(false);
            if (res.ErrorCode is SundouleiaApiEc.Success)
            {
                Logger.LogInformation($"Full Ipc Cache sent to {toSend.Count} newly visible users. {res.Value!.Count} Files needed uploading.");
                // The callback list contains the files that we need to process the uploads for.
                // Handle as fire-and-forget so that we do not block the distribution task updates.
                _ = Task.Run(() =>
                {
                    if (res.Value is null || res.Value.Count is 0)
                        return;

                    // Upload the files and then send the remainder off to the file uploader.
                    var newToSend = await _fileUploader.UploadFiles(res.Value, toSend).ConfigureAwait(false);
                    if (newToSend.Count is 0)
                        return;

                    Logger.LogInformation($"Sending out uploaded remaining files to {toSend.Count} users.");
                    // Send the remaining files off to the file uploader.
                    await _hub.UserPushIpcMods(new(toSend, newToSend)).ConfigureAwait(false);
                });

            }
        }, _distributeDataCTS.Token);
    }

    // Possibly merge into the transient resource manger later, and rename it to moddedStateManager or something.
    private async Task<HashSet<ModdedFile>> CollectModdedState(CancellationToken ct)
    {
        if (!_config.HasValidCacheFolderSetup())
        {
            Logger.LogWarning("Cache Folder not setup! Cannot process mod files!");
            return new HashSet<ModdedFile>(ModdedFileComparer.Instance);
        }

        // await until we are present and visible.
        await SundouleiaEx.WaitForPlayerLoading().ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // A lot of unnecessary calculation work that needs to be done simply because people like the idea of custom skeletons,
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

        // At this point we would want to add any pet resources as transient resources, then clear them from this list
        // (right now this is only grabbing from the player object, but should be grabbing from all other objects simultaneously if possible)


        // Removes any resources caught from fetching the on-screen actor resources from that which loaded as transient (glowing armor vfx ext)
        // any remaining transients for this OwnedObject are marked as PersistentTransients and returned.
        var persistents = _transients.ClearTransientsAndGetPersistents(OwnedObject.Player, moddedPaths.SelectMany(c => c.GamePaths).ToList());

        // For these paths, get their file replacement objects.
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(persistents).ConfigureAwait(false);
        Logger.LogDebug("== Transient Replacements ==");
        foreach (var replacement in resolvedTransientPaths.Select(c => new ModdedFile([.. c.Value], c.Key)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            Logger.LogDebug("=> {repl}", replacement);
            moddedPaths.Add(replacement);
        }

        _transients.RemoveUnmoddedPersistentTransients(OwnedObject.Player, [.. moddedPaths]);
        // obtain the final moddedFiles to send that is the result.
        moddedPaths = new HashSet<ModdedFile>(moddedPaths.Where(p => p.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), ModdedFileComparer.Instance);
        ct.ThrowIfCancellationRequested();

        // All remaining paths that are not file-swaps come from modded game files that need to be sent over sundouleia servers.
        // To authorize them we need their 40 character SHA1 computed hashes from their file data.
        var toCompute = moddedPaths.Where(f => !f.IsFileSwap).ToArray();
        Logger.LogDebug($"Computing hashes for {toCompute.Length} files.");

        // Grab these hashes via the FileCacheEntity.
        var computedPaths = _cacheManager.GetFileCachesByPaths(toCompute.Select(c => c.ResolvedPath).ToArray());
        
        // Ensure we set and log said computed hashes.
        foreach (var file in toCompute)
        {
            ct.ThrowIfCancellationRequested();
            file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
            Logger.LogDebug($"=> {file} (Hash: {file.Hash})");
        }

        // Finally as a sanity check, remove any invalid file hashes for files that are no longer valid.
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

        // All files should not be file swaps and valid hashes by this point, and as such we should return the result.
        return moddedPaths;
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
    private void ClearLimboAfterUpdate()
    {
        if (InLimbo.Count is not 0)
        {
            Logger.LogDebug("Clearing limbo as they will need a full update next time.");
            InLimbo.Clear();
        }
    }

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
            // If feeling slow can always run this together but might be best to run separately.
            var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            await _hub.UserPushIpcFull(new(SundesmosForUpdatePush, modChanges, visualChanges)).ConfigureAwait(false);
            ClearLimboAfterUpdate();
            Logger.LogInformation($"Ipc Cache Full Update completed.");
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
            var modChanges = await UpdateModCacheInternal().ConfigureAwait(false);
            await _hub.UserPushIpcMods(new(SundesmosForUpdatePush, modChanges)).ConfigureAwait(false);
            ClearLimboAfterUpdate();
            Logger.LogInformation($"Ipc Cache Mod Update completed.");
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
            // Process the changes for this cache into the clone of the latest data.
            var visualChanges = await UpdateIpcCacheInternal(newChanges).ConfigureAwait(false);
            if (!visualChanges.HasData())
            {
                Logger.LogInformation($"Ipc Cache Visuals Update found no changes, skipping push.");
                return;
            }

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            var toSend = SundesmosForUpdatePush;
            await _hub.UserPushIpcOther(new(toSend, visualChanges)).ConfigureAwait(false);
            ClearLimboAfterUpdate();
            Logger.LogInformation($"Pushed Visual IpcCache update to {toSend.Count} sundesmos. ({visualChanges.ToChangesString()})");
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
            // It's ok to modify the original here without cloning it since we are locking access.
            if (await UpdateDataCacheSingle(obj, type, _lastCreatedData).ConfigureAwait(false) is not { } newData)
            {
                Logger.LogInformation($"IpcCacheSingle ({obj})({type}) had no changes, skipping.");
                return;
            }

            // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
            var toSend = SundesmosForUpdatePush;
            await _hub.UserPushIpcSingle(new(toSend, obj, type, newData)).ConfigureAwait(false);
            ClearLimboAfterUpdate();
            Logger.LogInformation($"Pushed IpcCacheSingle update to {toSend.Count} sundesmos. ({obj})({type})");
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

    private async Task<ModUpdates> UpdateModCacheInternal()
    {
        // await CollectModdedState(CancellationToken.None).ConfigureAwait(false);
        // do some kind of file scan voodoo with our db and transient resource handler to grab the
        // latest active hashes for our character.
        return new ModUpdates(new List<ModFile>(), new List<string>());
    }

    // This task should not in any way be cancelled as it is important we finish it.
    private async Task<VisualUpdate> UpdateIpcCacheInternal(Dictionary<OwnedObject, IpcKind> changes)
    {
        // Create _lastCreatedData if it doesn't exist yet.
        _lastCreatedData ??= new ClientDataCache();
        // Make a deep clone for the changes.
        var changedData = _lastCreatedData.DeepClone();

        // process the tasks for each object in parallel.
        var tasks = new List<Task>();
        foreach (var (obj, kinds) in changes)
        {
            if (kinds == IpcKind.None) continue;
            tasks.Add(UpdateDataCache(obj, kinds, changedData));
        }
        // Execute in parallel.
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // With all tasks run, run a comparison of the created against the latest.
        return _lastCreatedData.ApplyAllIpc(changedData);
    }

    private async Task UpdateDataCache(OwnedObject obj, IpcKind toUpdate, ClientDataCache data)
    {
        if (toUpdate.HasAny(IpcKind.Glamourer))
            data.GlamourerState[obj] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;

        if (toUpdate.HasAny(IpcKind.CPlus))
            data.CPlusState[obj] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;

        if (obj is not OwnedObject.Player)
            return;

        if (toUpdate.HasAny(IpcKind.ModManips)) data.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Heels)) data.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Moodles)) data.Moodles = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Honorific)) data.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.PetNames)) data.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
    }

    private async Task<string?> UpdateDataCacheSingle(OwnedObject obj, IpcKind type, ClientDataCache data)
    {
        // Attempt to apply the retrieved data string to the latest data, outputting if the change occured.
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
        return data.ApplySingleIpc(obj, type, dataStr) ? dataStr : null;
    }

    #endregion Cache Updates
}
