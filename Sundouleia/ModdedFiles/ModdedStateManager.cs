using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Penumbra.Api.IpcSubscribers;
using Sundouleia.Interop;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.ModFiles;

// This manager will be heavily reduced if some requested penumbra API could be merged in that allows us to track
// the current active collection's fileCache for effective changes.
// This internal list gives us the final resulting modded paths with all priority conflicts taken into account.
// 
// With this list tracked for the collections on our active actors, we will be able to remove a lot of overhead
// that runs multiple API calls to know if a mod is enabled or not on every change.
//
// Instead of this, we will simply need to compare the current on-screen actor's files, and then reflect it across the animation files and other data that cannot be obtained
// such as .pap's, .scd's, and other extension types, as whenever the plugin loads or unloads, or a collection switches,
// we can simply check it against the fileCache, and remove paths no longer in the collectionCache, and add ones used while it is active.
// This way we only update the cache when a change occurs, and we only need to grab the on-screen actor paths that can be quickly retrieved.
//
// Until then we need to deal with this confusing nightmare mess of code that somehow manages to work by a lot of things managing to
// somehow line up in a way that can work together, unstable as it is.

/// <summary>
///     Processes changes to transient data, and tracks persistent data, along with on-screen data, 
///     to calculate the client's current modded state. <para />
///     Maybe rename to something shorter idk.
/// </summary>
public sealed class ModdedStateManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly TransientCacheConfig _cacheConfig;
    private readonly PlzNoCrashFrens _noCrashPlz;
    private readonly FileCacheManager _fileDb;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher; // saves my sanity.

    private string CurrentClientKey = string.Empty;

    // Tracks transients for each of the clients owned objects, to help with processing.
    private ConcurrentDictionary<OwnedObject, HashSet<string>>? _persistentTransients = null;

    // Only really useful for like, helping mitigate transient data loading at massive venues and stuff, but otherwise useless.
    private readonly object _cacheAdditionLock = new();
    private readonly HashSet<string> _cachedHandledPaths = new(StringComparer.Ordinal);
    
    // TransientCache of the current logged in player.
    // would prefer to restructure this as it does not really tell us when we should be
    // saving changes to the cache or not, but figure this out later.
    private TransientPlayerCache _clientCache
    {
        get
        {
            if (!_cacheConfig.Current.PlayerCaches.TryGetValue(CurrentClientKey, out var cache))
                _cacheConfig.Current.PlayerCaches[CurrentClientKey] = cache = new();
            return cache;
        }
    }

    public ModdedStateManager(ILogger<ModdedStateManager> logger, SundouleiaMediator mediator, MainConfig config, 
        TransientCacheConfig cacheConfig, PlzNoCrashFrens noCrashPlz, FileCacheManager fileDb, IpcManager ipc, 
        CharaObjectWatcher watcher) : base(logger, mediator)
    {
        _config = config;
        _cacheConfig = cacheConfig;
        _noCrashPlz = noCrashPlz;
        _fileDb = fileDb;
        _ipc = ipc;
        _watcher = watcher;

        // Tells us whenever any resource, from any source, is loaded by anything. I wish i could avoid needing to detour this,
        // but there is not much I can do about that.

        _ipc.Penumbra.OnObjectResourcePathResolved = GameObjectResourcePathResolved.Subscriber(Svc.PluginInterface, OnPenumbraLoadedResource);
        _ipc.Penumbra.OnObjectRedrawn = GameObjectRedrawn.Subscriber(Svc.PluginInterface, OnObjectRedrawn);

        // Need to make sure we get the correct key for our current logged in player.
        Svc.Framework.Update += OnTick;
        Svc.ClientState.Login += OnLogin;
        Svc.ClientState.ClassJobChanged += OnJobChange;
        if (PlayerData.IsLoggedIn)
            OnLogin();
    }

    // holds partially valid transient resources that have been loaded in.
    // personally not a fan of this but whatever for right now i dont care too much. just focused on getting it working.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> PersistentTransients
    {
        get
        {
            // if none exists yet will need to create a new one for it.
            if (_persistentTransients == null)
            {
                _persistentTransients = new();
                _clientCache.JobBasedCache.TryGetValue(PlayerData.JobId, out var jobPaths);
                _persistentTransients[OwnedObject.Player] = _clientCache.PersistentCache.Concat(jobPaths ?? []).ToHashSet(StringComparer.Ordinal);
                _clientCache.JobBasedPetCache.TryGetValue(PlayerData.JobId, out var petPaths);
                _persistentTransients[OwnedObject.Pet] = [.. petPaths ?? []];
            }

            return _persistentTransients;
        }
    }

    private ConcurrentDictionary<OwnedObject, HashSet<string>> OwnedModelRelatedFiles { get; } = new();

    // Fully transient resources for each owned client object.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> TransientResources { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _ipc.Penumbra.OnObjectRedrawn?.Dispose();
        _ipc.Penumbra.OnObjectResourcePathResolved?.Dispose();

        Svc.Framework.Update -= OnTick;
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.ClassJobChanged -= OnJobChange;
        OwnedModelRelatedFiles.Clear();
        TransientResources.Clear();
        PersistentTransients.Clear();
    }

    private void OnTick(IFramework _)
    {
        lock (_cacheAdditionLock)
            _cachedHandledPaths.Clear();
    }

    private async void OnLogin()
    {
        // await for the client to be fully loaded in, then retrieved their information.
        await SundouleiaEx.WaitForPlayerLoading();
        // we should always be created & loaded in at this point. If not something is wrong with dalamuds internal code.
        unsafe
        {
            var obj = ((Character*)_watcher.WatchedPlayerAddr);
            CurrentClientKey = obj->NameString + "_" + obj->HomeWorld;
        }
    }

    private void OnJobChange(uint newJobId)
    {
        if (PersistentTransients.TryGetValue(OwnedObject.Pet, out var value))
            value?.Clear();
        // reload the config for this current new classjob. (see if we can fizzle out the double semi-transients later)
        _clientCache.JobBasedCache.TryGetValue(newJobId, out var jobSpecificData);
        PersistentTransients[OwnedObject.Player] = _clientCache.PersistentCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _clientCache.JobBasedPetCache.TryGetValue(newJobId, out var petSpecificData);
        PersistentTransients[OwnedObject.Pet] = [.. petSpecificData ?? []];
    }

    private void OnObjectRedrawn(IntPtr address, int objectIdx)
    {
        Logger.LogDebug($"Object at Idx [{objectIdx}] Redrawn.", LoggerType.IpcPenumbra);
        Mediator.Publish(new PenumbraObjectRedrawn(address, objectIdx));

        // If the address is not from any of our watched addresses, immediately ignore it.
        if (!_watcher.CurrentOwned.Contains(address))
            return;

        // Clear the distinct resources for this owned object.
        var objKind = _watcher.WatchedTypes[address];
        if (OwnedModelRelatedFiles.TryRemove(objKind, out _))
            Logger.LogTrace($"Cleared Distinct Model-Related Files for {{{objKind}}} on Redraw.", LoggerType.ResourceMonitor);
    }

    // Mod Enabled? -> (Item Updates via Auto-Reload Gear ? [It will be in next OnScreenResourceFetch] : [Not on actor so dont care])
    // Mod Disabled? -> (Item updates via Auto-Reload Gear ? [Wont be in next OnScreenResourceFetch, so dont care] : [Still on you, so no change or need to send])
    /// <summary>
    ///     An event firing every time an objects resource path is resolved. <para />
    ///     This occurs a LOT. And should be handled with care!. <para />
    ///     We use this to fetch the changes in data that GetPlayerOnScreenActorPaths fails to obtain. <para />
    /// </summary>
    private void OnPenumbraLoadedResource(IntPtr address, string gamePath, string filePath)
    {
        // If the address is not from any of our watched addresses, immediately ignore it.
        if (!_watcher.CurrentOwned.Contains(address))
            return;

        // we know at this point that this is a loaded resource path for one of our game objects.
        var objKind = _watcher.WatchedTypes[address]; // if this fails something is going fundamentally wrong with the code.

        // ignore files already processed this frame
        if (_cachedHandledPaths.Contains(gamePath))
            return;

        lock (_cacheAdditionLock)
            _cachedHandledPaths.Add(gamePath);

        // ==== SANITIZE DATA ====
        // replace individual mtrl stuff (Some penumbra mtrl paths return paths with | in them for formatting reasons)
        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
            filePath = filePath.Split("|")[2];

        // replace fix slash direction in both file path and game path.
        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        // vanilla, only process modded.
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase))
            return;

        // Handle Mdl/Mtrl/Tex files to detect new mod changes.
        // Can possibly compare this with the cached client data state, idk.
        if (Constants.MdlMtrlTexExtensions.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            var ownedModelRelated = OwnedModelRelatedFiles.GetOrAdd(objKind, new HashSet<string>(StringComparer.Ordinal));
            // Get if we already have this path as a transient resource.
            if (ownedModelRelated.Contains(replacedGamePath)) return;
            // We got a new value, so add it.
            Logger.LogTrace($"Loaded New Distinct Mdl/Mtrl/Tex Resource {{{objKind}}} @ {gamePath} => {filePath}", LoggerType.ResourceMonitor);
            if (ownedModelRelated.Add(replacedGamePath))
            {
                Logger.LogDebug($"Distinct Mdl/Mtrl/Tex Resource Added [{replacedGamePath}] => [{filePath}]", LoggerType.ResourceMonitor);
                Mediator.Publish(new ModelRelatedResourceLoaded(objKind));
            }
        }

        // ignore files to not handle (this includes .mdl, .tex, and .mtrl files [which makes me curious what purpose the | filter served?)
        if (Constants.HandledExtensions.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
            HandleDistinctTransient(address, objKind, gamePath, filePath, replacedGamePath);
    }

    private CancellationTokenSource _transientCTS = new();
    private void HandleDistinctTransient(nint address, OwnedObject objKind, string gamePath, string filePath, string replacedGamePath)
    {
        var transients = TransientResources.GetOrAdd(objKind, new HashSet<string>(StringComparer.Ordinal));
        // Get if we already have this path as a transient resource.
        if (transients.Contains(replacedGamePath))
            return;

        Logger.LogTrace($"Distinct Transient Loaded {{{objKind}}} @ {gamePath} => {filePath}", LoggerType.ResourceMonitor);

        // Leverage the HashSet property of values to avoid a selectMany statement and run an O(1) check with contains for each owned object.
        if (PersistentTransients.Values.Any(set => set.Contains(gamePath, StringComparer.OrdinalIgnoreCase)))
            return;
        // If it was added, we should log and send a transient changed message.
        if (transients.Add(replacedGamePath))
        {
            Logger.LogDebug($"Distinct Transient Added [{replacedGamePath}] => [{filePath}]", LoggerType.ResourceMonitor);
            _ = Task.Run(async () =>
            {
                _transientCTS = _transientCTS.SafeCancelRecreate();
                // This feels like a band-aid fix for something that could be solved better.
                await Task.Delay(TimeSpan.FromSeconds(3), _transientCTS.Token).ConfigureAwait(false);
                foreach (var kvp in TransientResources)
                    if (TransientResources.TryGetValue(kvp.Key, out var values) && values.Any())
                        Mediator.Publish(new TransientResourceLoaded(kvp.Key));
            });
        }
    }


    // Death
    public void RemoveUnmoddedPersistentTransients(OwnedObject obj, List<ModdedFile>? replacements = null)
    {
        if (!PersistentTransients.TryGetValue(obj, out HashSet<string>? value))
            return;

        // If null is passed in, clear everything inside.
        if (replacements is null)
        {
            value.Clear();
            return;
        }

        // Otherwise, remove all unmodded PersistentTransients.
        Logger.LogDebug($"Removing unmodded PersistentTransients from ({obj})", LoggerType.ResourceMonitor);
        int removedPaths = 0;
        foreach (var replacementGamePath in replacements.Where(p => !p.HasFileReplacement).SelectMany(p => p.GamePaths).ToList())
        {
            // Remove it from the config directly as well.
            removedPaths += _cacheConfig.RemovePath(CurrentClientKey, obj, replacementGamePath);
            value.Remove(replacementGamePath);
        }
        if (removedPaths > 0)
        {
            Logger.LogTrace($"Removed {removedPaths} PersistentTransients during CleanUp. Saving Config.", LoggerType.ResourceMonitor);
            _cacheConfig.Save();
        }
    }

    /// <summary>
    ///     Called by the function fetched modded resources from the player shortly after calling <see cref="ClearTransientPaths"/> <para />
    ///     If any paths are still present in the the TransientResources after this is processed, they should be marked as PersistentTransients.
    /// </summary>
    public void PersistTransients(OwnedObject obj)
    {
        // Ensure that the PersistentTransients exists.
        if (!PersistentTransients.TryGetValue(obj, out HashSet<string>? persistentTransients))
            PersistentTransients[obj] = persistentTransients = new(StringComparer.Ordinal);

        // if no transients exist, nothing to keep persistent.
        if (!TransientResources.TryGetValue(obj, out var resources))
            return;

        // Otherwise persist transients leftover.
        var transientResources = resources.ToList();
        Logger.LogDebug($"Persisting {transientResources.Count} transient resources", LoggerType.ResourceMonitor);

        // set the newly added game paths as the transients from the transient resources that are not semi-transient resources.
        List<string> newlyAddedGamePaths = resources.Except(persistentTransients, StringComparer.Ordinal).ToList();

        foreach (var gamePath in transientResources)
            persistentTransients.Add(gamePath);

        bool saveConfig = false;
        // if we have newly added paths for our client player, append/elevate them to the persistent cache.
        if (obj is OwnedObject.Player && newlyAddedGamePaths.Count != 0)
        {
            saveConfig = true;
            foreach (var item in newlyAddedGamePaths.Where(f => !string.IsNullOrEmpty(f)))
                _cacheConfig.AddOrElevate(CurrentClientKey, PlayerData.JobId, item);
        }
        // Prevent redraw city.
        else if (obj is OwnedObject.Pet && newlyAddedGamePaths.Count != 0)
        {
            saveConfig = true;
            if (!_clientCache.JobBasedPetCache.TryGetValue(PlayerData.JobId, out var petPerma))
                _clientCache.JobBasedPetCache[PlayerData.JobId] = petPerma = [];

            foreach (var item in newlyAddedGamePaths.Where(f => !string.IsNullOrEmpty(f)))
                petPerma.Add(item);
        }

        if (saveConfig)
        {
            Logger.LogTrace("Saving transient.json from PersistTransientResources", LoggerType.ResourceMonitor);
            _cacheConfig.Save();
        }

        if (resources.Count > 0)
        {
            // Bomb the remaining.
            Logger.LogDebug($"Removing remaining {resources.Count} transient resources", LoggerType.ResourceMonitor);
            foreach (var file in resources)
                Logger.LogTrace($"Removing {file} from TransientResources", LoggerType.ResourceMonitor);

            TransientResources[obj].Clear();
        }
    }

    /// <summary>
    ///     Given an owned object <paramref name="obj"/>, and a list of their present modded game-paths, 
    ///     remove all transient resources that match any of the paths in <paramref name="list"/>. <para />
    ///     After this occurs, any remaining transient paths should be considered PersistentTransients (extras) to be processed. 
    /// </summary>
    public HashSet<string> ClearTransientsAndGetPersistents(OwnedObject obj, List<string> list)
    {
        // Skip clear process if no transient resources exist for this owned object.
        if (list.Count > 0)
        {
            // Attempt to retrieve the transient resources caught for this owned object.
            if (TransientResources.TryGetValue(obj, out var set))
            {
                // Remove all paths that match any of the paths in the list.
                int removed = set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
                Logger.LogDebug($"Removed {removed} previously existing transient paths", LoggerType.ResourceMonitor);
            }

            // We should also remove any PersistentTransients that have these paths as well, if present. (Only do this for the Player object)
            bool reloadSemiTransient = false;
            if (obj is OwnedObject.Player && PersistentTransients.TryGetValue(obj, out var semiset))
            {
                foreach (var file in semiset.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
                {
                    Logger.LogTrace($"Removing From SemiTransient: {file}", LoggerType.ResourceMonitor);
                    _cacheConfig.RemovePath(CurrentClientKey, obj, file);
                }

                int removed = semiset.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
                Logger.LogDebug($"Removed {removed} previously existing PersistentTransient paths", LoggerType.ResourceMonitor);
                // if any were removed we should reload the persistent transient paths.
                if (removed > 0)
                {
                    reloadSemiTransient = true;
                    Logger.LogTrace("Saving transient.json from ClearTransientPaths", LoggerType.ResourceMonitor);
                    _cacheConfig.Save();
                }
            }

            if (reloadSemiTransient)
                _persistentTransients = null;
        }

        // Any remaining transients that survived this should now become PersistentTransients.
        PersistTransients(obj);

        // Retrieve said PersistentTransients for return value, whose are valid.
        var pathsToResolve = PersistentTransients.GetValueOrDefault(obj, new HashSet<string>(StringComparer.Ordinal));
        pathsToResolve.RemoveWhere(string.IsNullOrEmpty);
        return pathsToResolve;
    }

    public void ReloadPersistentTransients()
        => _persistentTransients = null;

    private bool AddTransient(OwnedObject obj, string item)
    {
        if (PersistentTransients.TryGetValue(obj, out var semiTransient) && semiTransient != null && semiTransient.Contains(item))
            return false;

        if (!TransientResources.TryGetValue(obj, out HashSet<string>? transientResource))
        {
            transientResource = new HashSet<string>(StringComparer.Ordinal);
            TransientResources[obj] = transientResource;
        }

        return transientResource.Add(item.ToLowerInvariant());
    }

    private void RemoveTransient(OwnedObject obj, string path)
    {
        if (PersistentTransients.TryGetValue(obj, out var resources))
        {
            resources.RemoveWhere(f => string.Equals(path, f, StringComparison.Ordinal));
            if (obj is OwnedObject.Player)
            {
                _cacheConfig.RemovePath(CurrentClientKey, obj, path);
                Logger.LogTrace("Saving transient.json from RemoveTransient", LoggerType.ResourceMonitor);
                _cacheConfig.Save();
            }
        }
    }

    /// <summary>
    ///     Collects up all transients, on-screen data, persistant transients, calculating the ownedObject's current modded state. <para />
    ///     Used primarily for single SMAD related file operations.
    /// </summary>
    /// <returns> The modded files applied to this owned object at the time of calculation. </returns>
    public async Task<ModdedState> CollectActorModdedState(OwnedObject kind, CancellationToken ct)
    {
        var moddedState = new ModdedState();

        // Fail if no valid config.
        if (!_config.HasValidCacheFolderSetup())
        {
            Logger.LogWarning("Cache Folder not setup! Cannot process mod files!");
            return moddedState;
        }

        // Fail if owned object is not present.
        var ownedAddr = _watcher.FromOwned(kind);
        if (ownedAddr == IntPtr.Zero)
        {
            Logger.LogWarning($"Owned Object of kind {{{kind}}} not present! Cannot process mod files!");
            return moddedState;
        }

        // A bit messy but can maybe clean up later idk.
        ushort ownedIdx = ushort.MaxValue;
        unsafe { ownedIdx = ((GameObject*)ownedAddr)->ObjectIndex; }
        if (ownedIdx == ushort.MaxValue)
        {
            Logger.LogWarning($"Owned Object of kind {{{kind}}} has invalid index! Cannot process mod files!");
            return moddedState;
        }

        // Collect the on-screen paths for this owned object.
        Logger.LogDebug($"Collecting on-screen resource paths for owned object {{{kind}}} @ Idx [{ownedIdx}]");
        if (await _ipc.Penumbra.GetCharacterResourcePathData(ownedIdx).ConfigureAwait(false) is not { } onScreenPaths)
            throw new InvalidOperationException("Penumbra returned null data");

        Logger.LogDebug($"Processing owned object modded state for {{{kind}}}");
        var objectFiles = (kind is OwnedObject.Player)
            ? await CollectPlayerModdedFiles(onScreenPaths, ct)
            : await CollectOtherModdedFiles(kind, onScreenPaths, ct);

        // Store the result into the modded state.
        Logger.LogDebug($"Processed owned object modded state for {{{kind}}} with {objectFiles.Count} modded files.");
        moddedState.SetOwnedFiles(kind, objectFiles);
        return moddedState;
    }

    /// <summary>
    ///     Collects up all transients, on-screen data, and persistent transients, calculating the Client's current modded state. <para />
    ///     TODO: Separate this to process all owned objects concurrently, with special logic for pets.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<ModdedState> CollectModdedState(CancellationToken ct)
    {
        if (!_config.HasValidCacheFolderSetup())
        {
            Logger.LogWarning("Cache Folder not setup! Cannot process mod files!");
            return new ModdedState();
        }

        // Grab all of our owned object paths.
        // Obtain our current on-screen actor state.
        if (await _ipc.Penumbra.GetClientOnScreenResourcePaths().ConfigureAwait(false) is not { } clientOnScreenPaths)
            throw new InvalidOperationException("Penumbra returned null data");

        // Generate a new modded state to store the collected data into.
        var moddedState = new ModdedState();

        var sw = Stopwatch.StartNew();
        Logger.LogDebug($"Processing {clientOnScreenPaths.Keys.Count} owned object modded states in parallel.", LoggerType.ResourceMonitor);
        // if we really want optimizations we can run this in parallel however we will need a unique instance for each of the owned object transients!
        foreach (var (ownedObjectIdx, onScreenPaths) in clientOnScreenPaths)
        {
            // If this somehow triggers false we managed to unrender the object between the ipc call and now (same ms) which would be insane, but is possible.
            if (!_watcher.TryFindOwnedObjectByIdx(ownedObjectIdx, out var ownedObject))
                continue;

            var objectFiles = (ownedObject is OwnedObject.Player)
                ? await CollectPlayerModdedFiles(onScreenPaths, ct)
                : await CollectOtherModdedFiles(ownedObject, onScreenPaths, ct);
            // Store the result into the modded state.
            moddedState.SetOwnedFiles(ownedObject, objectFiles);
        }

        sw.Stop();
        Logger.LogDebug($"Processed owned object modded states in {sw.ElapsedMilliseconds}ms.", LoggerType.ResourceMonitor);

        Logger.LogTrace($"== Final Replacements ==", LoggerType.ResourceMonitor);
        foreach (var replacement in moddedState.AllFiles.OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            Logger.LogTrace($" => {replacement}", LoggerType.ResourceMonitor);
            ct.ThrowIfCancellationRequested();
        }

        // Inform the mediator that we calculated our modded state.
        Mediator.Publish(new ModdedStateCollected(moddedState));

        // return the modded state.
        return moddedState;
    }

    private async Task<HashSet<ModdedFile>> CollectPlayerModdedFiles(Dictionary<string, HashSet<string>> onScreenPaths, CancellationToken ct)
    {
        // await until we are present and visible.
        await _watcher.WaitForFullyLoadedGameObject(_watcher.WatchedPlayerAddr, ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // A lot of unnecessary calculation work that needs to be done simply because people like the idea of custom skeletons,
        // and.... they tend to crash other people :D
        Dictionary<string, List<ushort>>? boneIndices;
        unsafe { boneIndices = _noCrashPlz.GetSkeletonBoneIndices((GameObject*)_watcher.WatchedPlayerAddr); }

        // Set the file replacements to all currently visible paths on our player data, where they are modded.
        var moddedPaths = new HashSet<ModdedFile>(onScreenPaths.Select(c => new ModdedFile([.. c.Value], c.Key)), ModdedFileComparer.Instance).Where(p => p.HasFileReplacement).ToHashSet();
        // Remove unsupported filetypes.
        moddedPaths.RemoveWhere(c => c.GamePaths.Any(g => !Constants.ValidExtensions.Any(e => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));

        // Run the internal process.
        moddedPaths = await GetModdedStateInternal(OwnedObject.Player, moddedPaths, ct, LoggerType.PlayerMods);

        // Helps ensure we are not going to send files that will crash people yippee
        var invalidHashes = await _noCrashPlz.VerifyPlayerAnimationBones(boneIndices, moddedPaths, ct).ConfigureAwait(false);
        // Remove any invalid hashes from the persistent transients if any.
        foreach (var invalid in invalidHashes)
            RemoveTransient(OwnedObject.Player, invalid);

        return moddedPaths;
    }

    private async Task<HashSet<ModdedFile>> CollectOtherModdedFiles(OwnedObject obj, Dictionary<string, HashSet<string>> onScreenPaths, CancellationToken ct)
    {
        await _watcher.WaitForFullyLoadedGameObject(_watcher.FromOwned(obj), ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        // Set the file replacements to all currently visible paths on our player data, where they are modded.
        var moddedPaths = new HashSet<ModdedFile>(onScreenPaths.Select(c => new ModdedFile([.. c.Value], c.Key)), ModdedFileComparer.Instance).Where(p => p.HasFileReplacement).ToHashSet();
        // Remove unsupported filetypes.
        moddedPaths.RemoveWhere(c => c.GamePaths.Any(g => !Constants.ValidExtensions.Any(e => g.EndsWith(e, StringComparison.OrdinalIgnoreCase))));

        return await GetModdedStateInternal(obj, moddedPaths, ct, obj switch
        {
            OwnedObject.MinionOrMount => LoggerType.MinionMods,
            OwnedObject.Pet => LoggerType.PetMods,
            OwnedObject.Companion => LoggerType.CompanionMods,
            _ => LoggerType.ResourceMonitor
        });
    }

    private async Task<HashSet<ModdedFile>> GetModdedStateInternal(OwnedObject obj, HashSet<ModdedFile> moddedPaths, CancellationToken ct, LoggerType loggerType)
    {
        // If we wished to abort then abort.
        ct.ThrowIfCancellationRequested();

        // Log the remaining files. (without checking for transients)
        Logger.LogTrace($"== Static {obj} Replacements ==", loggerType);
        foreach (var replacement in moddedPaths.OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            Logger.LogTrace($"{obj} => {replacement}", loggerType);
            ct.ThrowIfCancellationRequested();
        }

        // Pets are special cases. For things like summoner, then keep ALL file replacements alive at ALL times, to prevent redraws
        // occurring all of the time on every summon.
        if (obj is OwnedObject.Pet)
        {
            foreach (var item in moddedPaths.Where(mp => mp.HasFileReplacement).SelectMany(p => p.GamePaths))
            {
                if (AddTransient(OwnedObject.Pet, item))
                    Logger.LogDebug($"Marking Static {item} for Pet as Transient", loggerType);
            }

            // Now that all the static replacements have been made transient, bomb all other modded paths.
            Logger.LogTrace($"Clearing {moddedPaths.Count} static paths for Pet.", loggerType);
            moddedPaths.Clear();
        }

        // Since all modded paths were bombed, there is nothing to remove from transients, making them all persistent transients.
        var persistents = ClearTransientsAndGetPersistents(obj, moddedPaths.SelectMany(c => c.GamePaths).ToList());

        // Resolve the persistent transients from penumbra.
        // (could maybe avoid this call too if we just get the subset of .Where(mp => mp.HasFileReplacement) ? Can look into later)
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(persistents).ConfigureAwait(false);

        // Append all resolved transient paths to the modded paths.
        Logger.LogTrace($"== Transient {obj} Replacements ==", loggerType);
        foreach (var replacement in resolvedTransientPaths.Select(c => new ModdedFile([.. c.Value], c.Key)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            Logger.LogTrace($"{obj} => {replacement}", loggerType);
            moddedPaths.Add(replacement);
        }

        // Since moddedPaths has contents again, we should remove any unmodded. 
        RemoveUnmoddedPersistentTransients(obj, [.. moddedPaths]);

        // obtain the final moddedFiles to send that is the result.
        moddedPaths = new HashSet<ModdedFile>(moddedPaths.Where(p => p.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), ModdedFileComparer.Instance);
        ct.ThrowIfCancellationRequested();

        // All remaining paths that are not file-swaps come from modded game files that need to be sent over sundouleia servers.
        // To authorize them we need their BLAKE3 file hashes from their file data.
        var toCompute = moddedPaths.Where(f => !f.IsFileSwap).ToArray();
        Logger.LogDebug($"Computing hashes for {toCompute.Length} files.", loggerType);

        // Grab these hashes via the FileCacheEntity.
        var computedPaths = _fileDb.GetFileCachesByPaths(toCompute.Select(c => c.ResolvedPath).ToArray());

        // Ensure we set and log said computed hashes.
        // We also group by hash here to ensure we only have one ModdedFile instance per hash, with all game paths it replaces,
        // as we use the hashes as unique identifiers throughout the sync, and multiple files with different paths and names
        // could have the same contents and thus same hash. For example: fully transparent textures.
        Dictionary<string, ModdedFile> groupedByHash = new(StringComparer.Ordinal);
        foreach (var file in toCompute)
        {
            ct.ThrowIfCancellationRequested();
            file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
            Logger.LogDebug($"=> {file} (Hash: {file.Hash})", loggerType);

            if (!string.IsNullOrEmpty(file.Hash))
            {
                if (groupedByHash.TryGetValue(file.Hash, out var existing))
                    existing.GamePaths.UnionWith(file.GamePaths);
                else
                    groupedByHash[file.Hash] = file;
            }
        }

        // Finally as a sanity check, remove any invalid file hashes for files that are no longer valid.
        var removed = moddedPaths.RemoveWhere(f => !f.IsFileSwap && string.IsNullOrEmpty(f.Hash));
        if (removed > 0)
            Logger.LogDebug($"=> Removed {removed} invalid file hashes.", loggerType);

        // Replace all modded paths with the grouped by hash versions.
        moddedPaths.RemoveWhere(f => groupedByHash.ContainsKey(f.Hash));
        moddedPaths.UnionWith(groupedByHash.Values);

        ct.ThrowIfCancellationRequested();

        return moddedPaths;
    }



    /// <summary>
    ///     Obtains the file replacements for the set of given paths that have forward and reverse resolve.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string[]>> GetFileReplacementsFromPaths(HashSet<string> forwardResolve)
    {
        var forwardPaths = forwardResolve.ToArray();
        string[] reversePaths = Array.Empty<string>();

        // track our resolved paths.
        Dictionary<string, List<string>> resolvedPaths = new(StringComparer.Ordinal);

        // grab them from penumbra.
        // If the mod is no longer enabled, it will return a FileSwap path instead of a modded path.
        //
        // === Example === 
        // With Yippee Mod disabled:
        // => chara/human/c0801/animation/a0001/bt_common/emote/welcome.pap : chara/human/c0801/animation/a0001/bt_common/emote/welcome.pap
        // With Yippee Mod enabled:
        // => c:\ffxivmodding\penumbra\yippeewelcome\chara\human\c0801\animation\a0001\bt_common\emote\welcome.pap : chara/human/c0801/animation/a0001/bt_common/emote/welcome.pap
        var (forward, reverse) = await _ipc.Penumbra.ResolveModPaths(forwardPaths, reversePaths).ConfigureAwait(false);
        for (int i = 0; i < forwardPaths.Length; i++)
        {
            var filePath = forward[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
                list.Add(forwardPaths[i].ToLowerInvariant());
            else
                resolvedPaths[filePath] = [forwardPaths[i].ToLowerInvariant()];
        }

        for (int i = 0; i < reversePaths.Length; i++)
        {
            var filePath = reversePaths[i].ToLowerInvariant();
            if (resolvedPaths.TryGetValue(filePath, out var list))
                list.AddRange(reverse[i].Select(c => c.ToLowerInvariant()));
            else
                resolvedPaths[filePath] = reverse[i].Select(c => c.ToLowerInvariant()).ToList();
        }

        Logger.LogTrace("== Resolved Paths ==", LoggerType.ResourceMonitor);
        foreach (var kvp in resolvedPaths.OrderBy(k => k.Key, StringComparer.Ordinal))
            Logger.LogTrace($"=> {kvp.Key} : {string.Join(", ", kvp.Value)}", LoggerType.ResourceMonitor);
        
        return resolvedPaths.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase).AsReadOnly();
    }

    /// <summary>
    ///     Drawer for debug UI. Internal so it can access private attributes.
    /// </summary>
    public void DrawTransientResources()
    {
        using var node = ImRaii.TreeNode($"Transient Resources##transient-resource-info");
        if (!node) return;

        using var table = ImRaii.Table("transientResources", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("OwnedObject");
        ImGui.TableSetupColumn("Resource Path");
        ImGui.TableHeadersRow();

        var allEntries = TransientResources.SelectMany(kv => kv.Value.Select(path => (OwnedObject: kv.Key, ResourcePath: path)));

        foreach (var (obj, entry) in allEntries)
        {
            ImGui.TableNextColumn();
            CkGui.ColorText(obj.ToString(), ImGuiColors.TankBlue.ToUint());
            ImGuiUtil.DrawTableColumn(entry.ToString());
        }
    }

    /// <summary>
    ///     Drawer for debug UI. Internal so it can access private attributes.
    /// </summary>
    public void DrawPersistentTransients()
    {
        using var node = ImRaii.TreeNode($"Persistent-Transients##persistent-transients-info");
        if (!node) return;

        using var table = ImRaii.Table("persistent-transients", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("OwnedObject");
        ImGui.TableSetupColumn("Resource Path");
        ImGui.TableHeadersRow();

        var allEntries = PersistentTransients.SelectMany(kv => kv.Value.Select(path => (OwnedObject: kv.Key, ResourcePath: path)));

        foreach (var (obj, entry) in allEntries)
        {
            ImGui.TableNextColumn();
            CkGui.ColorText(obj.ToString(), ImGuiColors.TankBlue.ToUint());
            ImGuiUtil.DrawTableColumn(entry.ToString());
        }
    }
}