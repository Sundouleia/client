using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Sundouleia.Interop;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;

namespace Sundouleia.ModFiles;

// Hi.
// ---
// I absolutely hate most of the overhead in this class, and wish that it could be more optimized.
// However as things currently are, it is difficult to know the mods that are presently applied
// to your on-screen actor until fully redrawn via penumbra API's.
// 
// While Glamourer has the [Auto-Reload Gear] option, that automatically reapplies your character upon
// changing a mod or reloading your gear, to update your visual state before redrawing, we must also
// consider that not everyone has this option enabled.
// 
// Additionally, there is no API in glamourer yet to check the state of this option, know when it gets toggled,
// or to call upon reapplication. There is currently a PR pending that requests the addition of these features.
// As if they could be requested then we could remove a lot of unessisary code here and also prevent the 
// need to redraw other sundesmo player actors almost entirely. We could even reapply their states mid-animation
// and update them to keep being in said animation.
//
// Even with this API added in Glamourer, it would still not be enough to cover the on-screen actors effective
// changes list, which is idealy what we need at the time of pulling the data, so we have to use resource-load still.
// ------
// The ideal solution would be the following:
// - Penumbra gets API to grab the 'on-screen actor effective changes' for their collection.
//    => out <string[] GamePaths, string[] ReplacementPaths>)
// - Glamourer adds the ReapplyState api, an event to know when the Auto-Reload gear option is toggled,
//   and one to get the state, much like the version.
//
// With these two changes, we would only need to grab the effective changes of an actor whenever
// mod settings changed or glamourerState changed, and send off to others those changes. Following that we would call
// a reapply self if glamourer's auto-reload gear was disabled.
// 
// Then everything would be synced as simple as that.
//
// But right now, we have a lot of people fighting over what they think is a competition, which makes it difficult to request any
// changes for things related to helping with update synchronization. This is understandable, but unfortunate, and will need patience
// until the dust settles and these changes can see reason for implementation.


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
    private uint _lastClassJobId = uint.MaxValue;

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
        Mediator.Subscribe<PenumbraResourceLoaded>(this, _ => OnPenumbraLoadedResource(_.Address, _.GamePath, _.ReplacePath));

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

    // Fully transient resources for each owned client object.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> TransientResources { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Svc.Framework.Update -= OnTick;
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.ClassJobChanged -= OnJobChange;
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

    // Mod Enabled? -> (Item Updates via Auto-Reload Gear ? [It will be in next OnScreenResourceFetch] : [Not on actor so dont care])
    // Mod Disabled? -> (Item updates via Auto-Reload Gear ? [Wont be in next OnScreenResolurceFetch, so dont care] : [Still on you, so no change or need to send])
    /// <summary>
    ///     Intentionally ignores .mdl, .tex, .mtrl files. See above for reason as to why this occurs. <para />
    ///     Is primarily for handling non-player model related resources, thing effects, animations, sounds, ext.
    /// </summary>
    private void OnPenumbraLoadedResource(IntPtr address, string gamePath, string filePath)
    {
        // we know at this point that this is a loaded resource path for one of our game objects.
        var objKind = _watcher.WatchedTypes[address]; // if this fails something is going fundementally wrong with the code.

        // ignore files already processed this frame
        if (_cachedHandledPaths.Contains(gamePath))
            return;

        lock (_cacheAdditionLock) _cachedHandledPaths.Add(gamePath); 

        // ==== SANITIZE DATA ====
        // replace individual mtrl stuff (Some penumbra mtrl paths return paths with | in them for formatting reasons)
        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
            filePath = filePath.Split("|")[2];

        // replace fix slash direction in both file path and game path.
        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        // ignore duplicates.
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase))
            return;

        // ignore files to not handle (this includes .mdl, .tex, and .mtrl files [which makes me curious what purpose the | filter served?)
        if (!Constants.HandledExtensions.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            // not a type we want to handle, so add it to the list of handled paths (so we skip it?... idk) (seems lazy, but will revise later)
            lock (_cacheAdditionLock)
                _cachedHandledPaths.Add(gamePath);
            return;
        }
        // ==== END SANITIZE DATA ====

        Logger.LogTrace($"Distinct TransientResourceLoad {{{objKind}}} @ {gamePath} => {filePath}", LoggerType.ResourceMonitor);
        var transients = TransientResources.GetOrAdd(objKind, new HashSet<string>(StringComparer.Ordinal));
        // Get if we already have this path as a transient resource.
        if (transients.Contains(replacedGamePath))
            return;

        // Leverage the HashSet property of values to avoid a selectMany statement and run an O(1) check with contains for each owned object.
        if (PersistentTransients.Values.Any(set => set.Contains(gamePath, StringComparer.OrdinalIgnoreCase)))
            return;
        
        // If it was added, we should log and send a transient changed message.
        if (transients.Add(replacedGamePath))
        {
            Logger.LogDebug($"Distinct Transient Added [{replacedGamePath}] => [{filePath}]", LoggerType.ResourceMonitor);
            SendTransients(address, objKind);
        }
    }

    // It is possible that we could be just spamming between outfits or jobs or whatever. In this case it is a good idea
    // to put a debouncer on the transient sender to avoid any unessisary calculations where possible.
    private CancellationTokenSource _sendTransientCts = new();
    private void SendTransients(nint address, OwnedObject obj)
    {
        // Hold 5s, then send off the transient resources changed event for all transient resources.
        _ = Task.Run(async () =>
        {
            _sendTransientCts = _sendTransientCts.SafeCancelRecreate();
            var token = _sendTransientCts.Token;
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);

            foreach (var kvp in TransientResources)
            {
                if (TransientResources.TryGetValue(obj, out var values) && values.Any())
                {
                    Logger.LogTrace($"Sending Transients for {obj}", LoggerType.ResourceMonitor);
                    Mediator.Publish(new TransientResourceLoaded(obj));
                }
            }
        });
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

        Logger.LogDebug($"Removing remaining {resources.Count} transient resources", LoggerType.ResourceMonitor);
        foreach (var file in resources)
            Logger.LogTrace($"Removing {file} from TransientResources", LoggerType.ResourceMonitor);

        // Bomb the remaining.
        TransientResources[obj].Clear();
    }

    /// <summary>
    ///     Given an owned object <paramref name="obj"/>, and a list of their present modded game-paths, 
    ///     remove all transient resources that match any of the paths in <paramref name="list"/>. <para />
    ///     After this occurs, any remaining transient paths should be considered PersistentTransients (extras) to be processed. 
    /// </summary>
    public HashSet<string> ClearTransientsAndGetPersistents(OwnedObject obj, List<string> list)
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

        // Any remaining transients that survived this should now become PersistentTransients.
        PersistTransients(obj);

        // Retrieve said PersistentTransients for return value, whose are valid.
        var pathsToResolve = PersistentTransients.GetValueOrDefault(obj, new HashSet<string>(StringComparer.Ordinal));
        pathsToResolve.RemoveWhere(string.IsNullOrEmpty);
        return pathsToResolve;
    }

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
    ///     Collects up all transients, on-screen data, and persistent transients, calculating the Client's current modded state. <para />
    ///     TODO: Separate this to process all owned objects concurrently, with special logic for pets.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<HashSet<ModdedFile>> CollectModdedState(CancellationToken ct)
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
        Logger.LogTrace("== Static Replacements ==", LoggerType.ResourceMonitor);
        foreach (var replacement in moddedPaths.OrderBy(i => i.GamePaths.First(), StringComparer.OrdinalIgnoreCase))
        {
            Logger.LogTrace($"=> {replacement}", LoggerType.ResourceMonitor);
            ct.ThrowIfCancellationRequested();
        }

        // At this point we would want to add any pet resources as transient resources, then clear them from this list
        // (right now this is only grabbing from the player object, but should be grabbing from all other objects simultaneously if possible)


        // Removes any resources caught from fetching the on-screen actor resources from that which loaded as transient (glowing armor vfx ext)
        // any remaining transients for this OwnedObject are marked as PersistentTransients and returned.
        var persistents = ClearTransientsAndGetPersistents(OwnedObject.Player, moddedPaths.SelectMany(c => c.GamePaths).ToList());

        // For these paths, get their file replacement objects.
        var resolvedTransientPaths = await GetFileReplacementsFromPaths(persistents).ConfigureAwait(false);
        Logger.LogTrace("== Transient Replacements ==", LoggerType.ResourceMonitor);
        foreach (var replacement in resolvedTransientPaths.Select(c => new ModdedFile([.. c.Value], c.Key)).OrderBy(f => f.ResolvedPath, StringComparer.Ordinal))
        {
            Logger.LogTrace($"=> {replacement}", LoggerType.ResourceMonitor);
            moddedPaths.Add(replacement);
        }

        RemoveUnmoddedPersistentTransients(OwnedObject.Player, [.. moddedPaths]);
        // obtain the final moddedFiles to send that is the result.
        moddedPaths = new HashSet<ModdedFile>(moddedPaths.Where(p => p.HasFileReplacement).OrderBy(v => v.ResolvedPath, StringComparer.Ordinal), ModdedFileComparer.Instance);
        ct.ThrowIfCancellationRequested();

        // All remaining paths that are not file-swaps come from modded game files that need to be sent over sundouleia servers.
        // To authorize them we need their 40 character SHA1 computed hashes from their file data.
        var toCompute = moddedPaths.Where(f => !f.IsFileSwap).ToArray();
        Logger.LogDebug($"Computing hashes for {toCompute.Length} files.", LoggerType.ResourceMonitor);

        // Grab these hashes via the FileCacheEntity.
        var computedPaths = _fileDb.GetFileCachesByPaths(toCompute.Select(c => c.ResolvedPath).ToArray());

        // Ensure we set and log said computed hashes.
        foreach (var file in toCompute)
        {
            ct.ThrowIfCancellationRequested();
            file.Hash = computedPaths[file.ResolvedPath]?.Hash ?? string.Empty;
            Logger.LogDebug($"=> {file} (Hash: {file.Hash})", LoggerType.ResourceMonitor);
        }

        // Finally as a sanity check, remove any invalid file hashes for files that are no longer valid.
        var removed = moddedPaths.RemoveWhere(f => !f.IsFileSwap && string.IsNullOrEmpty(f.Hash));
        if (removed > 0)
            Logger.LogDebug($"=> Removed {removed} invalid file hashes.", LoggerType.ResourceMonitor);

        // Final throw check.
        ct.ThrowIfCancellationRequested();
        // This is original if (OwnedObject is OwnedObject.Player), can fix later.
        if (true)
        {
            // Helps ensure we are not going to send files that will crash people yippee
            var invalidHashes = await _noCrashPlz.VerifyPlayerAnimationBones(boneIndices, moddedPaths, ct).ConfigureAwait(false);
            // Remove any invalid hashes from the persistent transients if any.
            foreach (var invalid in invalidHashes)
                RemoveTransient(OwnedObject.Player, invalid);
        }
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