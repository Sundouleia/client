using CkCommons;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Sundouleia.Interop;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.Services;

/// <summary> 
///     Listens for updates from various IPC sources, and enqueues their
///     changes to the ClientUpdateService.
/// </summary>
public sealed class ClientUpdateHandler : DisposableMediatorSubscriberBase
{
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;
    private readonly ClientUpdateService _updater;
    private readonly ClientDistributor _distributor;

    public ClientUpdateHandler(ILogger<ClientUpdateHandler> logger, SundouleiaMediator mediator,
        IpcManager ipc, CharaObjectWatcher watcher, ClientUpdateService updater, 
        ClientDistributor distributor)
        : base(logger, mediator)
    {
        _ipc = ipc;
        _watcher = watcher;
        _updater = updater;
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

        Svc.Framework.Update += OnUpdateTick;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _ipc.Penumbra.OnModSettingsChanged?.Dispose();
        _ipc.Glamourer.OnStateChanged?.Disable();
        _ipc.Glamourer.OnStateChanged?.Dispose();
        _ipc.CustomizePlus.OnProfileUpdate.Unsubscribe(OnCPlusProfileUpdate);
        _ipc.Heels.OnOffsetUpdate.Unsubscribe(OnHeelsOffsetUpdate);
        _ipc.Moodles.OnStatusManagerModified.Unsubscribe(OnMoodlesUpdate);
        _ipc.Honorific.OnTitleChange.Unsubscribe(OnHonorificUpdate);
        _ipc.PetNames.OnNicknamesChanged.Unsubscribe(OnPetNamesUpdate);
        Svc.Framework.Update -= OnUpdateTick;
    }


    private void OnUpdateTick(IFramework framework)
    {
        // Fail if there is nothing pending to update.
        if (_updater.AllPendingUpdates is 0)
            return;
        // Fail if zoning or not available.
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // Fail if currently in a debouncing task.
        if (_updater.Debouncing)
            return;

        // Assign the debounce and update/apply operation.
        _updater.SetDebounceTask(DebounceAndApply);
    }

    /// <summary>
    ///     After the debounce period, obtain any pending updates for our cache. <para />
    ///     
    ///     We want to perform this operation regardless of if any users are around, so
    ///     that the LatestData remains up to date. <para />
    ///     This ensures that when new users appear, they recieve the most recent data.
    /// </summary>
    private async Task DebounceAndApply()
    {
        // If there is nobody to push the update to, do not push.
        // We return early so that we exit before clearing the pending
        // updates, ensuring they are included in the next valid check.
        if (_updater.UsersForUpdatePush.Count is 0)
            return;

        // snapshot the changes dictionary and clear after.
        var pendingSnapshot = new Dictionary<OwnedObject, IpcKind>(_updater.PendingUpdates);
        var allPendingSnapshot = _updater.AllPendingUpdates;
        _updater.ClearPendingUpdates();

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

        // Otherwise, process with the assumption that the modded state could have at any point changed.
        Logger.LogDebug($"Processing CheckStateAndUpdate for {pendingSnapshot.Count} owned objects.", LoggerType.ClientUpdates);
        await _distributor.CheckStateAndUpdate(pendingSnapshot, allPendingSnapshot).ConfigureAwait(false);
    }

    private void OnModelRelatedResourceLoaded(OwnedObject ownedObj)
    {
        if (!_watcher.WatchedTypes.Values.Contains(ownedObj))
            return;
        _updater.AddPendingUpdate(ownedObj, IpcKind.Mods);
    }

    private void OnTransientResourceLoaded(OwnedObject ownedObj)
    {
        if (!_watcher.WatchedTypes.Values.Contains(ownedObj))
            return;
        _updater.AddPendingUpdate(ownedObj, IpcKind.Mods);
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
                _updater.AddPendingUpdate(type, IpcKind.Mods);
            // Could make this for all owned objects but whatever.
        }

        // If the change was an edited state change, then our mod manipulation string was modified.
        // (If we run into issues where you can change metadata without mods changing, (somehow bypassing redrawing) we can update them here.
        if (change is ModSettingChange.Edited && _watcher.WatchedPlayerAddr != IntPtr.Zero)
        {
            Logger.LogTrace($"OnModSettingChange: [Change: {change}] [Collection: {collectionId}] [ModDir: {modDir}] [Inherited: {inherited}]", LoggerType.IpcPenumbra);
            _updater.AddPendingUpdate(OwnedObject.Player, IpcKind.ModManips);
        }
    }

    // Fired within the framework thread.
    private void OnGlamourerUpdate(IntPtr address, StateChangeType _)
    {
        if (!_watcher.WatchedTypes.TryGetValue(address, out OwnedObject type))
            return;
        _updater.AddPendingUpdate(type, IpcKind.Glamourer);
    }

    // Fired within the framework thread.
    private void OnCPlusProfileUpdate(ushort objIdx, Guid id)
    {
        var address = Svc.Objects[objIdx]?.Address ?? IntPtr.Zero;
        if (!_watcher.WatchedTypes.TryGetValue(address, out var type))
            return;
        _updater.AddPendingUpdate(type, IpcKind.CPlus);
    }

    private void OnHeelsOffsetUpdate(string newOffset)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero)
            return;
        _updater.AddPendingUpdate(OwnedObject.Player, IpcKind.Heels);
    }

    private void OnMoodlesUpdate(nint playerAddr)
    {
        if (playerAddr != _watcher.WatchedPlayerAddr)
            return;
        _updater.AddPendingUpdate(OwnedObject.Player, IpcKind.Moodles);
    }

    private void OnHonorificUpdate(string newTitle)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero)
            return;
        _updater.AddPendingUpdate(OwnedObject.Player, IpcKind.Honorific);
    }

    private void OnPetNamesUpdate(string data)
    {
        if (_watcher.WatchedPlayerAddr == IntPtr.Zero)
            return;
        _updater.AddPendingUpdate(OwnedObject.Player, IpcKind.PetNames);
    }
}
