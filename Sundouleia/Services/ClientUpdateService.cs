using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
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
        DistributionService distributor) : base(logger, mediator)
    {
        _hub = hub;
        _ipc = ipc;
        _sundesmos = pairs;
        _watcher = watcher;
        _distributor = distributor;

        _ipc.Glamourer.OnStateChanged = StateChangedWithType.Subscriber(Svc.PluginInterface, OnGlamourerUpdate);
        _ipc.Glamourer.OnStateChanged.Enable();
        _ipc.CustomizePlus.OnProfileUpdate.Subscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Subscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusModified.Subscribe(OnMoodlesUpdate);
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

        _ipc.Glamourer.OnStateChanged?.Disable();
        _ipc.Glamourer.OnStateChanged?.Dispose();
        _ipc.CustomizePlus.OnProfileUpdate.Unsubscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Unsubscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusModified.Unsubscribe(OnMoodlesUpdate);
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
            Logger.LogTrace($"Waiting for debounce time of {GetDebounceTime()}ms");
            await Task.Delay(GetDebounceTime(), _debounceCTS.Token).ConfigureAwait(false);
            // snapshot the changes dictionary and clear after.
            var pendingSnapshot = new Dictionary<OwnedObject, IpcKind>(_pendingUpdates);
            var allPendingSnapshot = _allPendingUpdates;
            ClearPendingUpdates();

            if (_distributor.SundesmosForUpdatePush.Count is 0)
                return;

            // If there is only a single thing to update, send that over.
            var modUpdate = allPendingSnapshot.HasAny(IpcKind.Mods);
            var isSingle = pendingSnapshot.Count is 1 && SundouleiaEx.IsSingleFlagSet((byte)allPendingSnapshot);
            try
            {
                switch (modUpdate, isSingle)
                {
                    case (true, false):
                        Logger.LogDebug($"Processing full Mod update for {pendingSnapshot.Count} objects.");
                        await _distributor.UpdateIpcCacheFull(pendingSnapshot).ConfigureAwait(false);
                        break;
                    case (true, true):
                        Logger.LogDebug($"Processing single Mod update for {pendingSnapshot.Keys.First()}.");
                        await _distributor.UpdateModCache().ConfigureAwait(false);
                        break;
                    case (false, false):
                        Logger.LogDebug($"Processing partial update ({allPendingSnapshot}) for {pendingSnapshot.Count} objects.");
                        await _distributor.UpdateIpcCache(pendingSnapshot).ConfigureAwait(false);
                        break;
                    case (false, true):
                        Logger.LogDebug($"Processing single partial update ({allPendingSnapshot}) for {pendingSnapshot.Keys.First()}.");
                        await _distributor.UpdateIpcCacheSingle(pendingSnapshot.Keys.First(), allPendingSnapshot).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.LogCritical($"Error during ClientUpdate Process: {ex}"); }
        }, _debounceCTS.Token);
    }

    private void AddPendingUpdate(OwnedObject type, IpcKind kind)
    {
        _debounceCTS = _debounceCTS.SafeCancelRecreate();
        Logger.LogTrace($"Detected update for {type} ({kind})");
        if (_pendingUpdates.ContainsKey(type))
            _pendingUpdates[type] |= kind;
        else
            _pendingUpdates[type] = kind;
        _allPendingUpdates |= kind;
    }

    private void ClearPendingUpdates()
    {
        _pendingUpdates.Clear();
        _allPendingUpdates = IpcKind.None;
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

    private void OnMoodlesUpdate(IPlayerCharacter player)
    {
        if (player.Address != _watcher.WatchedPlayerAddr) return;
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
