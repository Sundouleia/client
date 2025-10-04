using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.Pairs;

// MAINTAINERS NOTE:
// It would actually be benificial to create a 'SundesmoMainHandler' and 'SundesmoSubHandler' (for minions / mounts / pets / ext)
// this would help reduce process time complexity and allow for framented data analysis with faster performance optimizations.
// also since only the player needs to manage download data it can handle it seperately.

// Example:
// - CharaCreated -> Player? -> SundesmoMainHandler -> Manage IPC + Penumbra + Mods + Downloads -> Reapply on application.
// - CharaCreated -> Pet? -> SundesmoSubHandler -> Manage IPC only, Redraw on application.

/// <summary>
///     Stores information about visual appearance data for a sundesmo's player-owned objects.
///     This includes their minion/mount, pet, or companion. <para />
///     
///     Any changes will result in a redraw of their character. <para />
///     
///     The Sundesmo object will handle player timeouts and inform the object handler 
///     directly of any changes. You will not need to handle timers internally here.
/// </summary>
public class PlayerOwnedHandler : DisposableMediatorSubscriberBase
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IpcManager _ipc;

    private CancellationTokenSource _timeoutCTS = new();
    private Task? _timeoutTask = null;

    public PlayerOwnedHandler(OwnedObject kind, Sundesmo sundesmo, ILogger<PlayerOwnedHandler> logger,
        SundouleiaMediator mediator, IHostApplicationLifetime lifetime, IpcManager ipc, CharaObjectWatcher watcher) 
        : base(logger, mediator)
    {
        ObjectType = kind;
        Sundesmo = sundesmo;

        _lifetime = lifetime;
        _ipc = ipc;

        // It is possible that our sundesmo was made after they were rendered.
        // If this is the case, we should iterate our watched objects to check for any
        // of their already rendered characters / companions and initialize them.
        watcher.CheckForExisting(this);

        unsafe
        {
            Mediator.Subscribe<WatchedObjectCreated>(this, msg =>
            {
                if (msg.Kind == OwnedObject.Player || Address != nint.Zero)
                    return;
                // Validate this is for the correct sundesmo, then create if so
                if (((GameObject*)msg.Address)->OwnerId != Sundesmo.PlayerEntityId)
                    return;
                ObjectRendered((GameObject*)msg.Address);
            });

            Mediator.Subscribe<WatchedObjectDestroyed>(this, msg =>
            {
                if (msg.Kind == OwnedObject.Player || Address == nint.Zero || msg.Address != Address)
                    return;
                ClearRenderedObject();
            });
        }
    }

    public OwnedObject ObjectType { get; init; }
    public Sundesmo Sundesmo { get; init; }
    private unsafe GameObject* _gameObject = null;

    // Cached Data for appearance.
    private Guid _tempProfile = Guid.Empty;
    private IpcDataCache? _appearanceData = new();

    // Public accessors.
    public GameObject DataState { get { unsafe { return *_gameObject; } } }
    public unsafe IntPtr Address => (nint)_gameObject;
    public unsafe ushort ObjIndex => _gameObject->ObjectIndex;
    public string ObjectName { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public bool IsOwnerValid => Sundesmo.PlayerRendered;
    public unsafe bool IsRendered => _gameObject != null;

    public unsafe void ObjectRendered(GameObject* obj)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));

        // Cancel any pending timeouts.
        _timeoutCTS.SafeCancel();

        // Init the object and set its name.
        _gameObject = obj;
        ObjectName = obj->NameString;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} rendered!", LoggerType.PairHandler);
    }

    public unsafe void ClearRenderedObject()
    {
        _gameObject = null;
        // do not clear the object name and other data yet, hold until timeout occurs, and then revert.
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} unrendered! Reverting in 10s unless reappearing.", LoggerType.PairHandler);
        _timeoutCTS = _timeoutCTS.SafeCancelRecreate();
        _timeoutTask = ObjectedDespawnedTask();
    }

    private async Task ObjectedDespawnedTask()
    {
        // Await the proper delay for data removal.
        await Task.Delay(TimeSpan.FromSeconds(10), _timeoutCTS.Token).ConfigureAwait(false);
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} has been unrendered for 10s, reverting data.", LoggerType.PairHandler);
        // Revert any applied data.
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
            await _ipc.Glamourer.ReleaseByName(ObjectName).ConfigureAwait(false);

        _appearanceData = null;
        ObjectName = string.Empty;
        // Notify the owner that we went poofy or whatever if we need to here.
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} data has been reverted due to timeout.", LoggerType.PairHandler);
    }

    public void ReapplyAlterations()
    {
        // Return if there is no valid appearance data or object is not rendered.
        if (!IsRendered || _appearanceData is null)
            return;
        // Reapply alterations.
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Glamourer]))
            ApplyGlamourer().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))
            ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"Reapplied ({Sundesmo.GetNickAliasOrUid()})'s alterations.", LoggerType.PairHandler);
    }

    // Thankfully only ever need to worry about cplus and glamourer here!.
    public async Task ApplyIpcData(IpcDataUpdate newIpc)
    {
        // 0) Set initial data if none present.
        _appearanceData ??= new();

        // 1) See what updates are applied, if any.
        var changes = _appearanceData.UpdateCache(newIpc);

        // 2) If nothing changed, or not present, return.
        if (changes == IpcKind.None || Address == IntPtr.Zero)
            return;

        // Process the updates if any were present.
        if (changes.HasAny(IpcKind.Glamourer)) 
            await ApplyGlamourer().ConfigureAwait(false);
        
        if (changes.HasAny(IpcKind.CPlus))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"Applied IPC changes for [{Sundesmo.GetNickAliasOrUid()}] - {ObjectType} : {changes}", LoggerType.PairHandler);
    }

    // Intended to be super fast and instant.
    public async Task ApplyIpcSingle(IpcKind kind, string newData)
    {
        // 0) Set initial data if none present.
        _appearanceData ??= new();

        // 1) Update the changes, return if not rendered or nothing changed.
        if (!_appearanceData.UpdateCacheSingle(kind, newData) || Address == IntPtr.Zero)
            return;

        // 3) Apply change based on the type.
        if (kind is IpcKind.Glamourer)
            await ApplyGlamourer().ConfigureAwait(false);
        else if (kind is IpcKind.CPlus)
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"Applied single IPC change for [{Sundesmo.GetNickAliasOrUid()}] - {kind}", LoggerType.PairHandler);
    }

    private async Task ApplyGlamourer()
        => await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);

    private async Task ApplyCPlus()
    {
        var hasData = !string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]);
        // If the string is blank, and the value exists, revert it.
        if (hasData && _tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            _tempProfile = await _ipc.CustomizePlus.ApplyTempProfile(this, _appearanceData.Data[IpcKind.CPlus]).ConfigureAwait(false);
        }
    }

    public async Task RevertAlterations()
    {
        if (Address == IntPtr.Zero) return;

        Logger.LogDebug($"Reverting {ObjectName}'s alterations.", LoggerType.PairHandler);
        if (_tempProfile != Guid.Empty)
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
        
        await _ipc.Glamourer.ReleaseActor(this).ConfigureAwait(false);
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
    }

    // NOTE: This can be very prone to crashing or inconsistant states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // store the name and address to reference removal properly.
        var name = ObjectName;
        Logger.LogDebug($"Disposing [{name}] @ [{Address:X}]", LoggerType.PairHandler);
        // Perform a safe disposal.
        try
        {
            // If they were valid before, parse out the event message for their disposal.
            if (!string.IsNullOrEmpty(name))
                Mediator.Publish(new EventMessage(new(name, Sundesmo.UserData.UID, DataEventType.Disposed, "Owned Object Disposed")));

            // If the lifetime host is stopping, log it is and return.
            if (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                // null the rendered player.
                unsafe { _gameObject = null; }
                return;
            }

            // Otherwise begin cleanup if valid to do so.
            if (!PlayerData.IsZoning && !PlayerData.InCutscene && !string.IsNullOrEmpty(name))
            { 
                Logger.LogTrace($"Restoring state for {name}. ({Sundesmo.UserData.AliasOrUID})", LoggerType.PairHandler);
                if (Address == IntPtr.Zero)
                {
                    _ipc.Glamourer.ReleaseByName(name).GetAwaiter().GetResult();
                }                
                else
                {
                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    Logger.LogInformation($"There is CachedData for {name} that requires removal.");
                    try
                    {
                        RevertAlterations().GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Error reverting alterations for {name}: {ex.Message}", LoggerType.PairHandler);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error upon disposal of {name}: {ex.Message}", LoggerType.PairHandler);
        }
        finally
        {
            // null the rendered player.
            unsafe { _gameObject = null; }
            // Cancel any pending timeouts.
            _timeoutCTS.SafeCancel();
            ObjectName = string.Empty;
            _appearanceData = null;
        }
    }
}
