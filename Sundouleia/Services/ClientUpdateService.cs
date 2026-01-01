using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary> 
///     Listens to all relevant IPC updates to our client state, and processes them appropriately. <para />
///     This helps avoid excessive mediator calls to free them up for other areas of the plugin, and
///     also allows for finer precision. <para />
///     Any task updates performed by this update service are for updating the latest data in 
///     DistributorService, and do not process the distribution of said data.
/// </summary>
public sealed class ClientUpdateService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;
    private readonly DistributionService _distributor;

    private Task? _debounceTask;
    private CancellationTokenSource _debounceCTS = new();

    // Which pending updates we have. (rework later probably)
    private Dictionary<OwnedObject, IpcKind> _pendingUpdates = new();
    private IpcKind _allPendingUpdates = IpcKind.None;

    public ClientUpdateService(ILogger<ClientUpdateService> logger, SundouleiaMediator mediator,
        MainHub hub, IpcManager ipc, SundesmoManager pairs, CharaObjectWatcher watcher, 
        DistributionService distributor) 
        : base(logger, mediator)
    {
        _hub = hub;
        _ipc = ipc;
        _sundesmos = pairs;
        _watcher = watcher;
        _distributor = distributor;

        Mediator.Subscribe<TransientResourceLoaded>(this, _ => OnTransientResourceLoaded(_.Object));
        Mediator.Subscribe<ModelRelatedResourceLoaded>(this, _ => OnModelRelatedResourceLoaded(_.Object));

        _ipc.Penumbra.OnModSettingsChanged = ModSettingChanged.Subscriber(Svc.PluginInterface, OnModSettingChanged);
        _ipc.Glamourer.OnStateChanged = StateChangedWithType.Subscriber(Svc.PluginInterface, OnGlamourerUpdate);
        _ipc.Glamourer.OnStateChanged.Enable();
        _ipc.CustomizePlus.OnProfileUpdate.Subscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Subscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusManagerModified.Subscribe(OnMoodlesUpdate);
        _ipc.Honorific.OnTitleChange.Subscribe(OnHonorificUpdate);
        _ipc.PetNames.OnNicknamesChanged.Subscribe(OnPetNamesUpdate);

        Svc.Framework.Update += OnFrameworkTick;
    }

    public bool IsUpdateProcessing
        => (_debounceTask is not null && !_debounceTask.IsCompleted) || _distributor.UpdatingData;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _debounceCTS.SafeCancelDispose();
        }

        _ipc.Penumbra.OnModSettingsChanged?.Dispose();
        _ipc.Glamourer.OnStateChanged?.Disable();
        _ipc.Glamourer.OnStateChanged?.Dispose();
        _ipc.CustomizePlus.OnProfileUpdate.Unsubscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Unsubscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusManagerModified.Unsubscribe(OnMoodlesUpdate);
        _ipc.Honorific.OnTitleChange.Unsubscribe(OnHonorificUpdate);
        _ipc.PetNames.OnNicknamesChanged.Unsubscribe(OnPetNamesUpdate);
        Svc.Framework.Update -= OnFrameworkTick;
    }

    // Could maybe even speed this up if we have a faster file comparer.
    private int GetDebounceTime()
    {
        if (_allPendingUpdates.HasAny(IpcKind.Mods))        return 1000;
        if (_allPendingUpdates.HasAny(IpcKind.Glamourer))   return 750;
        if (_allPendingUpdates.HasAny(IpcKind.Heels))       return 750;
        if (_allPendingUpdates.HasAny(IpcKind.CPlus))       return 750;
        if (_allPendingUpdates.HasAny(IpcKind.Honorific))   return 500;
        if (_allPendingUpdates.HasAny(IpcKind.Moodles))     return 250;
        if (_allPendingUpdates.HasAny(IpcKind.ModManips))   return 250;
        if (_allPendingUpdates.HasAny(IpcKind.PetNames))    return 150;
        return 1500;
    }

    // Can make other updates faster if we have some cancellation token logic with _canceledInUpdate triggers to avoid deboune time.

    private void OnFrameworkTick(IFramework framework)
    {
        // If no updates, do not update.
        if (_allPendingUpdates is 0)
            return;

        // Make sure we are available.
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (IsUpdateProcessing)
            return;

        // Otherwise, assign update task.
        _debounceTask = Task.Run(async () =>
        {
            // await for the processed debounce time, or until cancelled.
            Logger.LogTrace($"Waiting for debounce time of {GetDebounceTime()}ms", LoggerType.ClientUpdates);
            await Task.Delay(GetDebounceTime(), _debounceCTS.Token).ConfigureAwait(false);
            // snapshot the changes dictionary and clear after.
            var pendingSnapshot = new Dictionary<OwnedObject, IpcKind>(_pendingUpdates);
            var allPendingSnapshot = _allPendingUpdates;
            ClearPendingUpdates();

            if (_distributor.SundesmosForUpdatePush.Count is 0)
                return;

            // If there is only a single thing to update, send that over.
            var modUpdate = allPendingSnapshot.HasAny(IpcKind.Mods);
            var isSingle = !modUpdate && pendingSnapshot.Count is 1 && SundouleiaEx.IsSingleFlagSet((byte)allPendingSnapshot);

            // If the change was single and it was not glamourer, we can just send single.
            if (isSingle && allPendingSnapshot != IpcKind.Glamourer)
            {
                Logger.LogDebug($"Processing single update ({allPendingSnapshot}) for {pendingSnapshot.Keys.First()}.", LoggerType.ClientUpdates);
                await _distributor.UpdateAndSendSingle(pendingSnapshot.Keys.First(), allPendingSnapshot).ConfigureAwait(false);
                return;
            }
            // Otherwise, we should process it with the assumption that the modded state could have at any point changed.
            Logger.LogDebug($"Processing CheckStateAndUpdate for {pendingSnapshot.Count} owned objects.", LoggerType.ClientUpdates);
            await _distributor.CheckStateAndUpdate(pendingSnapshot, allPendingSnapshot).ConfigureAwait(false);        
        }, _debounceCTS.Token);
    }

    private void AddPendingUpdate(OwnedObject type, IpcKind kind)
    {
        _debounceCTS = _debounceCTS.SafeCancelRecreate();
        Logger.LogTrace($"Detected update for {type} ({kind})", LoggerType.ClientUpdates);
        if (_pendingUpdates.ContainsKey(type))
            _pendingUpdates[type] |= kind;
        else
            _pendingUpdates[type] = kind;
        _allPendingUpdates |= kind;
        _distributor.InvalidateCacheForKind(type, kind);
    }

    private void ClearPendingUpdates()
    {
        _pendingUpdates.Clear();
        _allPendingUpdates = IpcKind.None;
    }

    private void OnModelRelatedResourceLoaded(OwnedObject ownedObj)
    {
        if (!_watcher.WatchedTypes.Values.Contains(ownedObj)) return;
        AddPendingUpdate(ownedObj, IpcKind.Mods);
    }

    private void OnTransientResourceLoaded(OwnedObject ownedObj)
    {
        if (!_watcher.WatchedTypes.Values.Contains(ownedObj)) return;
        AddPendingUpdate(ownedObj, IpcKind.Mods);
    }

    /// <summary>
    ///     Fired whenever we change the settings or state of a mod in penumbra. <para />
    ///     This is useful because while GameObjectResourceLoaded informs us of 
    ///     every modded path loaded in, when things are unloaded, it does not inform us of this. <para />
    ///     It would be ideal if we had other alternative API calls to bind this to for a cleaner approach.
    /// </summary>
    /// <remarks> This will fire multiple times, one for each collection, if multiple collections are linked to it.</remarks>
    private void OnModSettingChanged(ModSettingChange change, Guid collectionId, string modDir, bool inherited)
    {
        // We dont really know all of our owned objects collections are, so until we have a way to track this,
        // we can't really filter out to only allow owned collections.
        // However, that would be a mess anyways, as summoning various minions each with their own
        // collections would be cancerous to monitor.

        // If mod options changed, they could have effected something that we are wearing, so pass a mod update.
        if (change is (ModSettingChange.EnableState | ModSettingChange.Setting) && _watcher.WatchedPlayerAddr != IntPtr.Zero)
        {
            Logger.LogTrace($"OnModSettingChange: [Change: {change}] [Collection: {collectionId}] [ModDir: {modDir}] [Inherited: {inherited}]", LoggerType.IpcPenumbra);
            foreach (var (addr, type) in _watcher.WatchedTypes)
                AddPendingUpdate(type, IpcKind.Mods);
            // Could make this for all owned objects but whatever.
        }

        // If the change was an edited state change, then our mod manipulation string was modified.
        // (If we run into issues where you can change metadata without mods changing, (somehow bypassing redrawing) we can update them here.
        if (change is ModSettingChange.Edited && _watcher.WatchedPlayerAddr != IntPtr.Zero)
        {
            Logger.LogTrace($"OnModSettingChange: [Change: {change}] [Collection: {collectionId}] [ModDir: {modDir}] [Inherited: {inherited}]", LoggerType.IpcPenumbra);
            AddPendingUpdate(OwnedObject.Player, IpcKind.ModManips);
        }
    }

    // Fired within the framework thread.
    private void OnGlamourerUpdate(IntPtr address, StateChangeType _)
    {
        if (!_watcher.WatchedTypes.TryGetValue(address, out OwnedObject type)) return;
        // Otherwise it is valid, so recreate the CTS and add a delay time.
        AddPendingUpdate(type, IpcKind.Glamourer);
    }

    // Fired within the framework thread.
    private void OnCPlusProfileUpdate(ushort objIdx, Guid id)
    {
        var address = Svc.Objects[objIdx]?.Address ?? IntPtr.Zero;
        if (!_watcher.WatchedTypes.TryGetValue(address, out var type)) return;
        AddPendingUpdate(type, IpcKind.CPlus);
    }

    private void OnHeelsOffsetUpdate(string newOffset)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Heels);
    }

    private void OnMoodlesUpdate(nint playerAddr)
    {
        if (playerAddr != _watcher.WatchedPlayerAddr) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Moodles);
    }

    private void OnHonorificUpdate(string newTitle)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Honorific);
    }

    private void OnPetNamesUpdate(string data)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.PetNames);
    }

}
