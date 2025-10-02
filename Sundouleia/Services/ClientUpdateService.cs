using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

/// <summary> 
///     Listens to all relevant IPC updates to our client state, and processes them appropriately. <para />
///     This helps avoid excessive mediator calls to free them up for other areas of the plugin, and
///     also allows for finer precision.
/// </summary>
public sealed class ClientUpdateService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    // Remember to check for visible users that become visible but recovered from a timeout,
    // as they already have our latest data and we should not update to them.

    private Task? _updateTask;
    private CancellationTokenSource _updateCTS = new();

    // Should cache latest mod files here, or in some other ModFileService,
    // along with a file uploader likely, or something.
    private ModDataUpdate LastSendModUpdate = new(); // will not be our current mod files..
    private VisualDataUpdate LastSentData = new();

    // Which pending updates we have. (rework later probably)
    private Dictionary<OwnedObject, IpcKind> _pendingUpdates = new();
    private IpcKind _allPendingUpdates = IpcKind.None;

    public ClientUpdateService(ILogger<ClientUpdateService> logger, SundouleiaMediator mediator,
        MainHub hub, IpcManager ipc, SundesmoManager pairs, CharaObjectWatcher ownedObjects)
        : base(logger, mediator)
    {
        _hub = hub;
        _ipc = ipc;
        _sundesmos = pairs;
        _watcher = ownedObjects;

        _ipc.Glamourer.OnStateChanged = StateChangedWithType.Subscriber(Svc.PluginInterface, OnGlamourerUpdate);
        _ipc.Glamourer.OnStateChanged.Enable();
        _ipc.CustomizePlus.OnProfileUpdate.Subscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Subscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusModified.Subscribe(OnMoodlesUpdate);
        _ipc.Honorific.OnTitleChange.Subscribe(OnHonorificUpdate);
        _ipc.PetNames.OnNicknamesChanged.Subscribe(OnPetNamesUpdate);

        Svc.Framework.Update += OnFrameworkTick;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _updateCTS.SafeCancelDispose();
        _updateCTS.Dispose();

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

    // Could also maybe just pull this into a delayed timer that fires too if we want, idk.
    // I guess it's just more convenient because it makes sure that we are available and stuff.
    private void OnFrameworkTick(IFramework framework)
    {
        // If no updates, do not update.
        if (_pendingUpdates.Count is 0)
            return;
        // Make sure we are available.
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // Do not run if task already running.
        if (_updateTask is not null && !_updateTask.IsCompleted)
            return;

        // Otherwise, assign update task.
        _updateTask = Task.Run(async () =>
        {
            // await for the processed debounce time, or until cancelled.
            await Task.Delay(GetDebounceTime(), _updateCTS.Token).ConfigureAwait(false);
            // after the delay retrieve the current visible users to send the update to.
            var usersToSendTo = _sundesmos.GetVisibleConnected();
            if (usersToSendTo.Count is 0)
            {
                ClearPendingUpdates(); return;
            }

            // If there is only a single thing to update, send that over.
            var modUpdate = (_allPendingUpdates & IpcKind.Mods) != 0;
            var isSingle = _pendingUpdates.Keys.Count is 1 && SundouleiaEx.IsSingleFlagSet((byte)_allPendingUpdates);
            try
            {
                var updateTask = (modUpdate, isSingle) switch
                {
                    (true, false) => SendOtherIpcUpdate(usersToSendTo, _pendingUpdates.Keys),
                    (true, true) => Task.CompletedTask,
                    (false, false) => SendOtherIpcUpdate(usersToSendTo, _pendingUpdates.Keys),
                    (false, true) => SendSingleIpcUpdate(usersToSendTo, _pendingUpdates.Keys.First(), _allPendingUpdates),
                };
                await updateTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Logger.LogCritical($"Error during ClientUpdate Process: {ex}"); }
            finally
            {
                _pendingUpdates.Clear();
                _allPendingUpdates = IpcKind.None;
            }
        }, _updateCTS.Token);
    }

    private async Task SendSingleIpcUpdate(List<UserData> toUpdate, OwnedObject obj, IpcKind type)
    {
        string? newData = type switch
        {
            IpcKind.ModManips => _ipc.Penumbra.GetMetaManipulationsString(),
            IpcKind.Glamourer => await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false),
            IpcKind.CPlus => await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false),
            IpcKind.Heels => await _ipc.Heels.GetClientOffset().ConfigureAwait(false),
            IpcKind.Moodles => await _ipc.Moodles.GetOwn().ConfigureAwait(false),
            IpcKind.Honorific => await _ipc.Honorific.GetTitle().ConfigureAwait(false),
            IpcKind.PetNames => _ipc.PetNames.GetPetNicknames(),
            _ => null
        };
        // If null, there was no change (if we remove comparisons it will still do this.
        if (newData is null)
        {
            // Only for debugging, condense after.
            Logger.LogDebug($"Aborting SendSingleIpcUpdate, no changes detected for {obj} - {type}");
            return;
        }
        // Send it off.
        Logger.LogInformation($"Sending Single Ipc Update for {obj} - {type} to {toUpdate.Count} users.");
        await _hub.UserPushIpcSingle(new(toUpdate, obj, type, newData)).ConfigureAwait(false);
    }

    // Can maybe lighten up on the parallelism inside of the compile changes functions
    // if we run all objects at once lol. But I went a lil wild. XD
    // Will need to update the way we transfer things later so that we can simplify this process.
    private async Task SendOtherIpcUpdate(List<UserData> toUpdate, IEnumerable<OwnedObject> objects)
    {
        var toSend = new VisualDataUpdate();
        var compileTasks = new List<Task<ObjectIpcData>>();
        // Compile tasks for parallel execution.
        foreach (var (kind, updates) in _pendingUpdates)
            compileTasks.Add(CompileAppearanceData(kind, updates));
        // Grab all IPC data from all client owned objects in parallel.
        var results = await Task.WhenAll(compileTasks).ConfigureAwait(false);

        Logger.LogInformation($"Sending Other Ipc Update to {toUpdate.Count} Sundesmos");
        //LastSentData.UpdateFrom(toSend);
        await _hub.UserPushIpcOther(new(toUpdate, toSend)).ConfigureAwait(false);
    }

    // Helpers. 
    private async Task<ObjectIpcData> CompileAppearanceData(OwnedObject obj, IpcKind updates)
    {
        var ret = new ObjectIpcData();

        if (updates is IpcKind.None) 
            return ret;

        // it is ok to not run these in parallel, as we are already running all objects in parallel.
        if (updates.HasAny(IpcKind.Glamourer))
            ret.Data[IpcKind.Glamourer] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;
        if (updates.HasAny(IpcKind.CPlus))
            ret.Data[IpcKind.CPlus] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;

        // Handle other updates for player.
        if (obj is OwnedObject.Player)
        {
            if (updates.HasAny(IpcKind.Heels))
                ret.Data[IpcKind.Heels] = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
            if (updates.HasAny(IpcKind.Honorific))
                ret.Data[IpcKind.Honorific] = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
            if (updates.HasAny(IpcKind.Moodles))
                ret.Data[IpcKind.Moodles] = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
            if (updates.HasAny(IpcKind.ModManips))
                ret.Data[IpcKind.ModManips] = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
            if (updates.HasAny(IpcKind.PetNames))
                ret.Data[IpcKind.PetNames] = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
        }

        return ret;
    }

    private void AddPendingUpdate(OwnedObject type, IpcKind kind)
    {
        _updateCTS.SafeCancelRecreate();
        _pendingUpdates[type] |= kind;
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
