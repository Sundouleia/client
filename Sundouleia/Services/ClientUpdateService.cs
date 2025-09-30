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
using TerraFX.Interop.Windows;

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
    private readonly CharaObjectWatcher _objectWatcher;

    private Task? _updateTask;
    private CancellationTokenSource _updateCTS = new();

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
        _objectWatcher = ownedObjects;

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
                    (true, false) => SendFullIpcUpdate(usersToSendTo),
                    (true, true) => SendModIpcUpdate(usersToSendTo),
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
            IpcKind.Glamourer => await _ipc.Glamourer.GetBase64StateByPtr(_objectWatcher.FromOwned(obj)).ConfigureAwait(false),
            IpcKind.CPlus => await _ipc.CustomizePlus.GetActiveProfileByPtr(_objectWatcher.FromOwned(obj)).ConfigureAwait(false),
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

    // Can maybe lighten up on the parallelism inside of the compile changes functions if we run all objects at once lol. But I went a lil wild. XD
    private async Task SendOtherIpcUpdate(List<UserData> toUpdate, IEnumerable<OwnedObject> objects)
    {
        // Start compile tasks in parallel
        var playerTask = objects.Contains(OwnedObject.Player) ? CompilePlayerDataChanges() : Task.FromResult<PlayerIpcData?>(null);
        var nonPlayerTasks = objects.Where(obj => obj != OwnedObject.Player).Select(Obj => (Obj, Task: CompileNonPlayerDataChanges(Obj))).ToList();

        // Run all calculations in parallel.
        var playerRes = await playerTask.ConfigureAwait(false);
        var otherRes = await Task.WhenAll(nonPlayerTasks.Select(x => x.Task)).ConfigureAwait(false);

        // Create the ret object.
        var toSend = new VisualDataUpdate();
        if (playerRes is not null && LastSentData.Player.IsDifferent(playerRes))
        {
            Logger.LogDebug($"PlayerData had new changes! {playerRes.Updates}");
            toSend.Player = playerRes;
        }

        // For each non-player result, if it had changes, and was different, apply it.
        for (int i = 0; i < otherRes.Length; i++)
        {
            if (otherRes[i] != null && LastSentData.NonPlayers[nonPlayerTasks[i]!.Obj].IsDifferent(otherRes[i]!))
            {
                Logger.LogDebug($"NonPlayerData had new changes! {nonPlayerTasks[i]!.Obj} - {otherRes[i]!.Updates}");
                toSend.NonPlayers[nonPlayerTasks[i]!.Obj] = otherRes[i]!;
            }
        }

        // If the object is empty, nothing changed, so do not update.
        if (toSend.Player.Updates is 0 && toSend.NonPlayers.Count is 0)
        {
            Logger.LogDebug($"Aborting SendOtherIpcUpdate, no changes detected for {string.Join(", ", objects)}");
            return;
        }

        Logger.LogInformation($"Sending Other Ipc Update to {toUpdate.Count} Sundesmos");
        LastSentData.UpdateFrom(toSend);
        await _hub.UserPushIpcOther(new(toUpdate, toSend)).ConfigureAwait(false);
    }

    // Helpers. 
    private async Task<PlayerIpcData?> CompilePlayerDataChanges()
    {
        var pending = _pendingUpdates.GetValueOrDefault(OwnedObject.Player, IpcKind.None);
        // Create tasks to run in parallel with default returns if not pending.
        var manips = pending.HasAny(IpcKind.ModManips) ? _ipc.Penumbra.GetMetaManipulationsString() : null;
        var petNames = pending.HasAny(IpcKind.PetNames) ? _ipc.PetNames.GetPetNicknames() : null;
        var glamTask = pending.HasAny(IpcKind.Glamourer) ? _ipc.Glamourer.GetBase64StateByPtr(_objectWatcher.WatchedPlayerAddr) : Task.FromResult<string?>(null);
        var cPlusTask = pending.HasAny(IpcKind.CPlus) ? _ipc.CustomizePlus.GetActiveProfileByPtr(_objectWatcher.WatchedPlayerAddr) : Task.FromResult<string?>(null);
        var heelsTask = pending.HasAny(IpcKind.Heels) ? _ipc.Heels.GetClientOffset() : Task.FromResult(string.Empty);
        var moodlesTask = pending.HasAny(IpcKind.Moodles) ? _ipc.Moodles.GetOwn() : Task.FromResult(string.Empty);
        var honorificTask = pending.HasAny(IpcKind.Honorific) ? _ipc.Honorific.GetTitle() : Task.FromResult(string.Empty);
        // Run all in parallel.
        var results = await Task.WhenAll(glamTask!, cPlusTask!, heelsTask, moodlesTask!, honorificTask!).ConfigureAwait(false);
        // Get Object to return.
        var toReturn = new PlayerIpcData()
        {
            Updates = pending,
            GlamourerState = results[0] ?? string.Empty,
            CPlusData = results[1] ?? string.Empty,
            HeelsOffset = results[2] ?? string.Empty,
            Moodles = results[3] ?? string.Empty,
            HonorificTitle = results[4] ?? string.Empty,
            ModManips = manips ?? string.Empty,
            PetNicknames = petNames ?? string.Empty,
        };
        return LastSentData.Player.IsDifferent(toReturn) ? toReturn : null;
    }

    private async Task<NonPlayerIpcData?> CompileNonPlayerDataChanges(OwnedObject obj)
    {
        var pending = _pendingUpdates.GetValueOrDefault(obj, IpcKind.None);
        // Create tasks to run in parallel with default returns if not pending.
        var glamTask = pending.HasAny(IpcKind.Glamourer) ? _ipc.Glamourer.GetBase64StateByPtr(_objectWatcher.FromOwned(obj)) : Task.FromResult<string?>(null);
        var cPlusTask = pending.HasAny(IpcKind.CPlus) ? _ipc.CustomizePlus.GetActiveProfileByPtr(_objectWatcher.FromOwned(obj)) : Task.FromResult<string?>(null);
        // Run all in parallel.
        var results = await Task.WhenAll(glamTask, cPlusTask).ConfigureAwait(false);

        var toReturn = new NonPlayerIpcData()
        {
            Updates = pending,
            GlamourerState = results[0] ?? string.Empty,
            CPlusData = results[1] ?? string.Empty,
        };
        return toReturn.Updates > 0 ? toReturn : null;
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
        if (!_objectWatcher.WatchedTypes.TryGetValue(address, out OwnedObject type)) return;
        // Otherwise it is valid, so recreate the CTS and add a delay time.
        AddPendingUpdate(type, IpcKind.Glamourer);
    }

    // Fired within the framework thread.
    private void OnCPlusProfileUpdate(ushort objIdx, Guid id)
    {
        var address = Svc.Objects[objIdx]?.Address ?? IntPtr.Zero;
        if (!_objectWatcher.WatchedTypes.TryGetValue(address, out var type)) return;
        AddPendingUpdate(type, IpcKind.CPlus);
    }

    private void OnHeelsOffsetUpdate(string newOffset)
    {
        if (_objectWatcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Heels);
    }

    private void OnMoodlesUpdate(IPlayerCharacter player)
    {
        if (player.Address != _objectWatcher.WatchedPlayerAddr) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Moodles);
    }

    private void OnHonorificUpdate(string newTitle)
    {
        if (_objectWatcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.Honorific);
    }

    private void OnPetNamesUpdate(string data)
    {
        if (_objectWatcher.WatchedPlayerAddr == IntPtr.Zero) return;
        AddPendingUpdate(OwnedObject.Player, IpcKind.PetNames);
    }

}
