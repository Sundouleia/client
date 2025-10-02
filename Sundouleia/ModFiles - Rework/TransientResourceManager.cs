using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

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
// changes for things related to helping with update syncronization. This is underandable, but unfortunate, and will need patience
// until the dust settles and these changes can see reason for implementation.

public sealed class TransientResourceManager : DisposableMediatorSubscriberBase
{
    // The allowed file types we are willing to transfer. (excluding textures model and material files)
    private static readonly IEnumerable<string> HANDLED_FILE_TYPES = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];
    private static readonly IEnumerable<string> HANDLED_RECORDING_TYPES = ["tex", "mdl", "mtrl"];

    private readonly TransientCacheConfig _config;
    private readonly CharaObjectWatcher _watcher; // saves my sanity.

    // internal vars.
    private string CurrentClientKey = string.Empty;
    private uint _lastClassJobId = uint.MaxValue;

    // Overhead?
    private ConcurrentDictionary<OwnedObject, HashSet<string>>? _semiTransientResources = null;
    private readonly object _cacheAdditionLock = new();
    private readonly HashSet<string> _cachedHandledPaths = new(StringComparer.Ordinal);
    
    // TransientCache of the current logged in player.
    // would prefer to restructure this as it does not really tell us when we should be
    // saving changes to the cache or not, but figure this out later.
    private TransientPlayerCache _clientCache
    {
        get
        {
            if (!_config.Current.PlayerCaches.TryGetValue(CurrentClientKey, out var cache))
                _config.Current.PlayerCaches[CurrentClientKey] = cache = new();
            return cache;
        }
    }

    public TransientResourceManager(ILogger<TransientResourceManager> logger, SundouleiaMediator mediator,
        TransientCacheConfig config, CharaObjectWatcher watcher) : base(logger, mediator)
    {
        _config = config;
        _watcher = watcher;

        // Tells us whenever any resource, from any source, is loaded by anything. I wish i could avoid needing to detour this,
        // but there is not much I can do about that.
        Mediator.Subscribe<PenumbraResourceLoaded>(this, _ => OnPenumbraLoadedResource(_.Address, _.GamePath, _.ReplacePath));

        // For whenever a setting in our mod path changes. Helps us know when we are about to get a flood of resource loads from penumbra, or when to check what is still present or not.
        // That being said, this is somewhat wierd to have given it will always be out of sync?
        Mediator.Subscribe<PenumbraSettingsChanged>(this, _ => OnModSettingsChanged());

        // Need to make sure we get the correct key for our current logged in player.
        Svc.ClientState.Login += OnLogin;
        if (PlayerData.IsLoggedIn)
            OnLogin();
    }


    // holds partially valid transient resources that have been loaded in.
    // personally not a fan of this but whatever for right now i dont care too much. just focused on getting it working.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> SemiTransientResources
    {
        get
        {
            // if none exists yet will need to create a new one for it.
            if (_semiTransientResources == null)
            {
                _semiTransientResources = new();
                _clientCache.JobBasedCache.TryGetValue(PlayerData.JobId, out var jobPaths);
                _semiTransientResources[OwnedObject.Player] = _clientCache.PersistantCache.Concat(jobPaths ?? []).ToHashSet(StringComparer.Ordinal);
                _clientCache.JobBasedPetCache.TryGetValue(PlayerData.JobId, out var petPaths);
                _semiTransientResources[OwnedObject.Pet] = [.. petPaths ?? []];
            }

            return _semiTransientResources;
        }
    }

    // Fully transient resoruces for each owned client object.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> TransientResources { get; } = new();


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Svc.ClientState.Login -= OnLogin;
        TransientResources.Clear();
        SemiTransientResources.Clear();
    }

    private async void OnLogin()
    {
        // await for the client to be fully loaded in, then retrieved their information.
        await SundouleiaEx.WaitForPlayerLoading();
        // we should always be created & loaded in at this point.
        // If not something is wrong with dalamuds internal code.
        unsafe
        {
            var obj = ((Character*)_watcher.WatchedPlayerAddr);
            CurrentClientKey = obj->NameString + "_" + obj->HomeWorld;
        }
    }

    private void OnPenumbraLoadedResource(IntPtr address, string gamePath, string filePath)
    {
        // we know at this point that this is a loaded resource path for one of our game objects.
        var objKind = _watcher.WatchedTypes[address]; // if this fails something is going fundementally wrong with the code.

        // ignore files already processed this frame
        if (_cachedHandledPaths.Contains(gamePath))
            return;

        lock (_cacheAdditionLock)
        {
            _cachedHandledPaths.Add(gamePath);
        }

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
        if (!HANDLED_FILE_TYPES.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            // not a type we want to handle, so add it to the list of handled paths (so we skip it?... idk) (seems lazy, but will revise later)
            lock (_cacheAdditionLock)
            {
                _cachedHandledPaths.Add(gamePath);
            }
            return;
        }
        // ==== END SANITIZE DATA ====

        var transients = TransientResources.GetOrAdd(objKind, new HashSet<string>(StringComparer.Ordinal));
        // Get if we already have this path as a transient resource.
        bool alreadyTransient = transients.Contains(replacedGamePath);
        bool alreadySemiTransient = SemiTransientResources.SelectMany(k => k.Value).Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase));
        if (alreadyTransient || alreadySemiTransient)
        {
            Logger.LogTrace($"Not adding {replacedGamePath} => {filePath}, Reason: AlreadyTransient: {alreadyTransient}, AlreadySemiTransient: {alreadySemiTransient}");
            return;
        }
        // Otherwise, we should add it
        else
        {
            // If it was added, we should log and send a transient changed message.
            if (transients.Add(replacedGamePath))
            {
                Logger.LogDebug($"Adding {replacedGamePath} for {address:X} ({filePath})");
                SendTransients(address, objKind);
            }
        }
    }

    /// <summary>
    ///     Occurs whenever a penumbra setting had changed. <para />
    ///     We use this to track when mods are disabled, because they fire 0 resource loaded 
    ///     events upon a disable if Glamourers Auto-Reload gear is off.
    /// </summary>
    private void OnModSettingsChanged()
    {
        // Could yap all day about this, see top of file.
        _ = Task.Run(() =>
        {
            Logger.LogDebug("Penumbra Mod Settings changed, verifying SemiTransientResources");
            foreach (var address in _watcher.CurrentOwned)
            {
                Logger.LogTrace("I used to tell other owned objects to recreate themselves.");
            }
        });
    }

    private CancellationTokenSource _sendTransientCts = new();
    private void SendTransients(nint address, OwnedObject objKind)
    {
        // Hold 5s, then send off the transient resources changed event for all transient resources.
        _ = Task.Run(async () =>
        {
            _sendTransientCts = _sendTransientCts.SafeCancelRecreate();
            var token = _sendTransientCts.Token;
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);

            foreach (var kvp in TransientResources)
            {
                if (TransientResources.TryGetValue(objKind, out var values) && values.Any())
                {
                    Logger.LogTrace($"Sending Transients for {objKind}");
                    // Later please... lol.
                    // Mediator.Publish(new TransientResourceChangedMessage(gameObject));
                }
            }
        });
    }

    // Death.
    public void CleanupSemiTransients(OwnedObject obj, List<string>? replacements = null)
    {
        if (!SemiTransientResources.TryGetValue(obj, out HashSet<string>? value))
            return;
        // if there was no replacements for this semi-transient, then clear the
        // semi-transient resource cache & exit early.
        if (replacements is null)
        {
            value.Clear();
            return;
        }

        // Otherwise we have paths to remove so remove them.
        int removedPaths = 0;
        foreach (var replacementGamePath in replacements.ToList())
        {
            removedPaths += _config.RemovePath(CurrentClientKey, obj, replacementGamePath);
            value.Remove(replacementGamePath);
        }

        if (removedPaths > 0)
        {
            Logger.LogTrace($"Removed {removedPaths} of SemiTransient paths during CleanUp, Saving from CleanupSemiTransients.");
            _config.Save();
        }
    }

    public HashSet<string> GetSemiTransients(OwnedObject obj)
    {
        SemiTransientResources.TryGetValue(obj, out var result);
        return result ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public void PersistTransients(OwnedObject obj)
    {
        // make fresh semi-transients if none exist.
        if (!SemiTransientResources.TryGetValue(obj, out HashSet<string>? semiTransientResources))
            SemiTransientResources[obj] = semiTransientResources = new(StringComparer.Ordinal);

        // if no transients exist, nothing to keep persistent.
        if (!TransientResources.TryGetValue(obj, out var resources))
            return;

        // Otherwise persist transients leftover.
        var transientResources = resources.ToList();
        List<string> newlyAddedGamePaths = resources.Except(semiTransientResources, StringComparer.Ordinal).ToList();
        Logger.LogDebug($"Persisting {transientResources.Count} transient resources");

        foreach (var gamePath in transientResources)
            semiTransientResources.Add(gamePath);

        bool saveConfig = false;
        // if we have newly added paths for our client player, append/elevate them to the persistent cache.
        if (obj is OwnedObject.Player && newlyAddedGamePaths.Count != 0)
        {
            saveConfig = true;
            foreach (var item in newlyAddedGamePaths.Where(f => !string.IsNullOrEmpty(f)))
                _config.AddOrElevate(CurrentClientKey, PlayerData.JobId, item);
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
            Logger.LogTrace("Saving transient.json from PersistTransientResources");
            _config.Save();
        }
        // Bomb the remaining.
        TransientResources[obj].Clear();
    }


    public void RemoveTransient(OwnedObject obj, string path)
    {
        if (SemiTransientResources.TryGetValue(obj, out var resources))
        {
            resources.RemoveWhere(f => string.Equals(path, f, StringComparison.Ordinal));
            if (obj is OwnedObject.Player)
            {
                _config.RemovePath(CurrentClientKey, obj, path);
                Logger.LogTrace("Saving transient.json from RemoveTransient");
                _config.Save();
            }
        }
    }

    internal bool AddTransient(OwnedObject obj, string item)
    {
        if (SemiTransientResources.TryGetValue(obj, out var semiTransient) && semiTransient != null && semiTransient.Contains(item))
            return false;

        if (!TransientResources.TryGetValue(obj, out HashSet<string>? transientResource))
        {
            transientResource = new HashSet<string>(StringComparer.Ordinal);
            TransientResources[obj] = transientResource;
        }

        return transientResource.Add(item.ToLowerInvariant());
    }

    internal void ClearTransientPaths(OwnedObject obj, List<string> list)
    {
        if (TransientResources.TryGetValue(obj, out var set))
        {
            // Logging
            foreach (var file in set.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
                Logger.LogTrace($"Removing From Transient: {file}");

            // Actual removal
            int removed = set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogDebug($"Removed {removed} previously existing transient paths");
        }

        bool reloadSemiTransient = false;
        if (obj is OwnedObject.Player && SemiTransientResources.TryGetValue(obj, out var semiset))
        {
            foreach (var file in semiset.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Logger.LogTrace($"Removing From SemiTransient: {file}");
                _config.RemovePath(CurrentClientKey, obj, file);
            }

            int removed = semiset.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogDebug($"Removed {removed} previously existing semi transient paths");
            if (removed > 0)
            {
                reloadSemiTransient = true;
                Logger.LogTrace("Saving transient.json from ClearTransientPaths");
                _config.Save();
            }
        }

        if (reloadSemiTransient)
            _semiTransientResources = null;
    }

    // :catscream: (try and remove this if possible, idk)
    private void OnFrameworkUpdate()
    {
        //_cachedFrameAddresses = new(_playerRelatedPointers.Where(k => k.Address != nint.Zero).ToDictionary(c => c.Address, c => c.ObjectKind));
        //lock (_cacheAdditionLock)
        //{
        //    _cachedHandledPaths.Clear();
        //}

        //if (_lastClassJobId != _dalamudUtil.ClassJobId)
        //{
        //    _lastClassJobId = _dalamudUtil.ClassJobId;
        //    if (SemiTransientResources.TryGetValue(ObjectKind.Pet, out HashSet<string>? value))
        //    {
        //        value?.Clear();
        //    }

        //    // reload config for current new classjob
        //    PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out var jobSpecificData);
        //    SemiTransientResources[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        //    PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out var petSpecificData);
        //    SemiTransientResources[ObjectKind.Pet] = [.. petSpecificData ?? []];
        //}

        //foreach (var kind in Enum.GetValues(typeof(ObjectKind)))
        //{
        //    if (!_cachedFrameAddresses.Any(k => k.Value == (ObjectKind)kind) && TransientResources.Remove((ObjectKind)kind, out _))
        //    {
        //        Logger.LogDebug("Object not present anymore: {kind}", kind.ToString());
        //    }
        //}
    }
}