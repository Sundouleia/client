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

        _pendingUpdates = Enum.GetValues<OwnedObject>().ToDictionary(o => o, _ => IpcKind.None);

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
        => (_debounceTask is not null && !_debounceTask.IsCompleted) || _distributor.ProcessingCacheUpdate;

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
        => _allPendingUpdates switch
        {
            IpcKind.Mods => 1000,
            IpcKind.Glamourer => 750,
            IpcKind.Heels => 750,
            IpcKind.CPlus => 750,
            IpcKind.Honorific => 500,
            IpcKind.Moodles => 250,
            IpcKind.ModManips => 250,
            IpcKind.PetNames => 150,
            _ => 1500,
        };

    private void OnFrameworkTick(IFramework framework)
    {
        // If no updates, do not update.
        if (_allPendingUpdates is 0)
            return;
        // Make sure we are available.
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // Do not run if task already running.
        if (_debounceTask is not null && !_debounceTask.IsCompleted)
            return;

        // Otherwise, assign update task.
        _debounceTask = Task.Run(async () =>
        {
            // await for the processed debounce time, or until cancelled.
            await Task.Delay(GetDebounceTime(), _debounceCTS.Token).ConfigureAwait(false);
            // after the delay retrieve the current visible users to send the update to.
            if (_distributor.SundesmosForUpdatePush.Count is 0)
            {
                ClearPendingUpdates();
                return; 
            }

            // If there is only a single thing to update, send that over.
            var modUpdate = _allPendingUpdates.HasAny(IpcKind.Mods);
            var isSingle = SundouleiaEx.IsSingleFlagSet((byte)_allPendingUpdates);
            try
            {
                switch (modUpdate, isSingle)
                {
                    case (true, false):
                        Logger.LogInformation($"Processing full Mod update for {_pendingUpdates.Count} objects.");
                        _distributor.UpdateIpcCacheFull(_pendingUpdates);
                        break;
                    case (true, true):
                        Logger.LogInformation($"Processing single Mod update for {_pendingUpdates.Keys.First()}.");
                        _distributor.UpdateModCache();
                        break;
                    case (false, false):
                        Logger.LogInformation($"Processing partial update ({_allPendingUpdates}) for {_pendingUpdates.Count} objects.");
                        _distributor.UpdateIpcCache(_pendingUpdates);
                        break;
                    case (false, true):
                        Logger.LogInformation($"Processing single partial update ({_allPendingUpdates}) for {_pendingUpdates.Keys.First()}.");
                        _distributor.UpdateIpcCacheSingle(_pendingUpdates.Keys.First(), _allPendingUpdates);
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.LogCritical($"Error during ClientUpdate Process: {ex}"); }
            finally
            {
                ClearPendingUpdates();
            }
        }, _debounceCTS.Token);
    }

    private void AddPendingUpdate(OwnedObject type, IpcKind kind)
    {
        _debounceCTS = _debounceCTS.SafeCancelRecreate();
        _pendingUpdates[type] |= kind;
        _allPendingUpdates |= kind;
    }

    private void ClearPendingUpdates()
    {
        foreach (var key in _pendingUpdates.Keys)
            _pendingUpdates[key] = IpcKind.None;
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
