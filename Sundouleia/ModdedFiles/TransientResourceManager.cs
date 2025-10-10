using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using OtterGui;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

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
    private readonly TransientCacheConfig _config;
    private readonly CharaObjectWatcher _watcher; // saves my sanity.

    // internal vars.
    private string CurrentClientKey = string.Empty;
    private uint _lastClassJobId = uint.MaxValue;

    // Overhead?
    private ConcurrentDictionary<OwnedObject, HashSet<string>>? _persistantTransients = null;
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
        Svc.Framework.Update += OnTick;
        Svc.ClientState.Login += OnLogin;
        Svc.ClientState.ClassJobChanged += OnJobChange;
        if (PlayerData.IsLoggedIn)
            OnLogin();
    }

    // holds partially valid transient resources that have been loaded in.
    // personally not a fan of this but whatever for right now i dont care too much. just focused on getting it working.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> PersistantTransients
    {
        get
        {
            // if none exists yet will need to create a new one for it.
            if (_persistantTransients == null)
            {
                _persistantTransients = new();
                _clientCache.JobBasedCache.TryGetValue(PlayerData.JobId, out var jobPaths);
                _persistantTransients[OwnedObject.Player] = _clientCache.PersistantCache.Concat(jobPaths ?? []).ToHashSet(StringComparer.Ordinal);
                _clientCache.JobBasedPetCache.TryGetValue(PlayerData.JobId, out var petPaths);
                _persistantTransients[OwnedObject.Pet] = [.. petPaths ?? []];
            }

            return _persistantTransients;
        }
    }

    // Fully transient resoruces for each owned client object.
    private ConcurrentDictionary<OwnedObject, HashSet<string>> TransientResources { get; } = new();


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

    public void DrawPersistantTransients()
    {
        using var node = ImRaii.TreeNode($"Persistant-Transients##persistant-transients-info");
        if (!node) return;

        using var table = ImRaii.Table("persistant-transients", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("OwnedObject");
        ImGui.TableSetupColumn("Resource Path");
        ImGui.TableHeadersRow();

        var allEntries = PersistantTransients.SelectMany(kv => kv.Value.Select(path => (OwnedObject: kv.Key, ResourcePath: path)));

        foreach (var (obj, entry) in allEntries)
        {
            ImGui.TableNextColumn();
            CkGui.ColorText(obj.ToString(), ImGuiColors.TankBlue.ToUint());
            ImGuiUtil.DrawTableColumn(entry.ToString());
        }
    }


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        Svc.Framework.Update -= OnTick;
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.ClassJobChanged -= OnJobChange;
        TransientResources.Clear();
        PersistantTransients.Clear();
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
        if (PersistantTransients.TryGetValue(OwnedObject.Pet, out var value))
            value?.Clear();
        // reload the config for this current new classjob. (see if we can fizzle out the double semi-transients later)
        _clientCache.JobBasedCache.TryGetValue(newJobId, out var jobSpecificData);
        PersistantTransients[OwnedObject.Player] = _clientCache.PersistantCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _clientCache.JobBasedPetCache.TryGetValue(newJobId, out var petSpecificData);
        PersistantTransients[OwnedObject.Pet] = [.. petSpecificData ?? []];
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
        if (PersistantTransients.Values.Any(set => set.Contains(gamePath, StringComparer.OrdinalIgnoreCase)))
            return;
        
        // If it was added, we should log and send a transient changed message.
        if (transients.Add(replacedGamePath))
        {
            Logger.LogDebug($"Distinct Transient Added [{replacedGamePath}] => [{filePath}]", LoggerType.ResourceMonitor);
            SendTransients(address, objKind);
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
            Logger.LogDebug("Penumbra Mod Settings changed, verifying PersistantTransients", LoggerType.ResourceMonitor);
            Mediator.Publish(new TransientResourceLoaded(OwnedObject.Player));
        });
    }

    // It is possible that we could be just spamming between outfits or jobs or whatever. In this case it is a good idea
    // to put a debouncer on the transient sender to avoid any unessisary calculations where possible.
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
                    Logger.LogTrace($"Sending Transients for {objKind}", LoggerType.ResourceMonitor);
                    Mediator.Publish(new TransientResourceLoaded(objKind));
                }
            }
        });
    }

    // Death
    public void RemoveUnmoddedPersistantTransients(OwnedObject obj, List<ModdedFile>? replacements = null)
    {
        if (!PersistantTransients.TryGetValue(obj, out HashSet<string>? value))
            return;
        // If null is passed in, clear everything inside.
        if (replacements is null)
        {
            value.Clear();
            return;
        }

        // Otherwise, remove all unmodded PersistantTransients.
        Logger.LogDebug($"Removing unmodded PersistantTransients from ({obj})", LoggerType.ResourceMonitor);
        int removedPaths = 0;
        foreach (var replacementGamePath in replacements.Where(p => !p.HasFileReplacement).SelectMany(p => p.GamePaths).ToList())
        {
            // Remove it from the config directly as well.
            removedPaths += _config.RemovePath(CurrentClientKey, obj, replacementGamePath);
            value.Remove(replacementGamePath);
        }
        if (removedPaths > 0)
        {
            Logger.LogTrace($"Removed {removedPaths} PersistantTransients during CleanUp. Saving Config.", LoggerType.ResourceMonitor);
            _config.Save();
        }
    }

    /// <summary>
    ///     Called by the function fetched modded resources from the player shortly after calling <see cref="ClearTransientPaths"/> <para />
    ///     If any paths are still present in the the TransientResources after this is processed, they should be marked as PersistentTransients.
    /// </summary>
    /// <param name="obj"></param>
    public void PersistTransients(OwnedObject obj)
    {
        // Ensure that the PersistantTransients exists.
        if (!PersistantTransients.TryGetValue(obj, out HashSet<string>? persistantTransients))
            PersistantTransients[obj] = persistantTransients = new(StringComparer.Ordinal);

        // if no transients exist, nothing to keep persistent.
        if (!TransientResources.TryGetValue(obj, out var resources))
            return;

        // Otherwise persist transients leftover.
        var transientResources = resources.ToList();
        Logger.LogDebug($"Persisting {transientResources.Count} transient resources", LoggerType.ResourceMonitor);

        // set the newly added game paths as the transients from the transient resources that are not semi-transient resources.
        List<string> newlyAddedGamePaths = resources.Except(persistantTransients, StringComparer.Ordinal).ToList();

        foreach (var gamePath in transientResources)
            persistantTransients.Add(gamePath);

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
            Logger.LogTrace("Saving transient.json from PersistTransientResources", LoggerType.ResourceMonitor);
            _config.Save();
        }

        Logger.LogDebug($"Removing remaining {resources.Count} transient resources", LoggerType.ResourceMonitor);
        foreach (var file in resources)
            Logger.LogTrace($"Removing {file} from TransientResources", LoggerType.ResourceMonitor);

        // Bomb the remaining.
        TransientResources[obj].Clear();
    }


    public void RemoveTransient(OwnedObject obj, string path)
    {
        if (PersistantTransients.TryGetValue(obj, out var resources))
        {
            resources.RemoveWhere(f => string.Equals(path, f, StringComparison.Ordinal));
            if (obj is OwnedObject.Player)
            {
                _config.RemovePath(CurrentClientKey, obj, path);
                Logger.LogTrace("Saving transient.json from RemoveTransient", LoggerType.ResourceMonitor);
                _config.Save();
            }
        }
    }

    internal bool AddTransient(OwnedObject obj, string item)
    {
        if (PersistantTransients.TryGetValue(obj, out var semiTransient) && semiTransient != null && semiTransient.Contains(item))
            return false;

        if (!TransientResources.TryGetValue(obj, out HashSet<string>? transientResource))
        {
            transientResource = new HashSet<string>(StringComparer.Ordinal);
            TransientResources[obj] = transientResource;
        }

        return transientResource.Add(item.ToLowerInvariant());
    }

    /// <summary>
    ///     Given an owned object <paramref name="obj"/>, and a list of their present modded gamepaths, remove
    ///     all transient resources that match any of the paths in <paramref name="list"/>. <para />
    ///     After this occurs, any remaining transient paths should be considered PersistentTransients (extras) to be processed. 
    /// </summary>
    public HashSet<string> ClearTransientsAndGetPersistants(OwnedObject obj, List<string> list)
    {
        // Attempt to retrieve the transient resources caught for this owned object.
        if (TransientResources.TryGetValue(obj, out var set))
        {
            // Remove all paths that match any of the paths in the list.
            int removed = set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogDebug($"Removed {removed} previously existing transient paths", LoggerType.ResourceMonitor);
        }

        // We should also remove any PersistantTransients that have these paths as well, if present. (Only do this for the Player object)
        bool reloadSemiTransient = false;
        if (obj is OwnedObject.Player && PersistantTransients.TryGetValue(obj, out var semiset))
        {
            foreach (var file in semiset.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Logger.LogTrace($"Removing From SemiTransient: {file}", LoggerType.ResourceMonitor);
                _config.RemovePath(CurrentClientKey, obj, file);
            }

            int removed = semiset.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogDebug($"Removed {removed} previously existing PersistantTransient paths", LoggerType.ResourceMonitor);
            // if any were removed we should reload the persistant transient paths.
            if (removed > 0)
            {
                reloadSemiTransient = true;
                Logger.LogTrace("Saving transient.json from ClearTransientPaths", LoggerType.ResourceMonitor);
                _config.Save();
            }
        }

        if (reloadSemiTransient)
            _persistantTransients = null;

        // Any remaining transients that survived this should now become PersistantTransients.
        PersistTransients(obj);

        // Retrieve said PersistantTransients for return value, whose are valid.
        var pathsToResolve = PersistantTransients.GetValueOrDefault(obj, new HashSet<string>(StringComparer.Ordinal));
        pathsToResolve.RemoveWhere(string.IsNullOrEmpty);
        return pathsToResolve;
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
        //    if (PersistantTransients.TryGetValue(ObjectKind.Pet, out HashSet<string>? value))
        //    {
        //        value?.Clear();
        //    }

        //    // reload config for current new classjob
        //    PlayerConfig.JobSpecificCache.TryGetValue(_dalamudUtil.ClassJobId, out var jobSpecificData);
        //    PersistantTransients[ObjectKind.Player] = PlayerConfig.GlobalPersistentCache.Concat(jobSpecificData ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        //    PlayerConfig.JobSpecificPetCache.TryGetValue(_dalamudUtil.ClassJobId, out var petSpecificData);
        //    PersistantTransients[ObjectKind.Pet] = [.. petSpecificData ?? []];
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