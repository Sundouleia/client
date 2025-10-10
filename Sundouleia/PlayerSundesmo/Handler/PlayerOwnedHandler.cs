using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using static Lumina.Data.Parsing.Layer.LayerCommon;

namespace Sundouleia.Pairs;

/// <summary>
///     Stores information about visual appearance data for a sundesmo's player-owned objects.
///     This includes their minion/mount, pet, or companion. <para />
///     
///     Any changes will result in a redraw of this object. <para />
///     
///     The Sundesmo object will handle player timeouts and inform the object handler 
///     directly of any changes. You will not need to handle timers internally here.
/// </summary>
public class PlayerOwnedHandler : DisposableMediatorSubscriberBase
{
    private readonly IpcManager _ipc;

    private CancellationTokenSource _timeoutCTS = new();
    private Task? _timeoutTask = null;

    public PlayerOwnedHandler(OwnedObject kind, Sundesmo sundesmo, ILogger<PlayerOwnedHandler> logger,
        SundouleiaMediator mediator, IpcManager ipc) 
        : base(logger, mediator)
    {
        ObjectType = kind;
        Sundesmo = sundesmo;

        _ipc = ipc;

        // Will likely need to revise this a lot until we get it right.
        // There is some discrepancy with object creation order potentially, unsure.
        // for right now the implemented solution is a band-aid fix.
        unsafe
        {
            Mediator.Subscribe<WatchedObjectCreated>(this, msg =>
            {
                // Ignore if already rendered.
                if (msg.Address != nint.Zero) return;
                // Ignore if the address's OwnerId is not equal to the sundesmo's PlayerId.
                if (((GameObject*)msg.Address)->OwnerId != Sundesmo.PlayerEntityId) return;
                // Validate address match via helpers based on type.
                var isMatch = ObjectType switch
                {
                    OwnedObject.MinionOrMount => Sundesmo.IsMountMinionAddress(msg.Address),
                    OwnedObject.Pet => Sundesmo.IsPetAddress(msg.Address),
                    OwnedObject.Companion => Sundesmo.IsCompanionAddress(msg.Address),
                    _ => false
                };
                // Must be a valid match.
                if (!isMatch) return;

                // If it is, log and render the object.
                Logger.LogDebug($"Detected {Sundesmo.GetNickAliasOrUid()}'s {ObjectType} creation @ [{msg.Address:X}]", LoggerType.PairHandler);
                ObjectRendered((GameObject*)msg.Address);
            });

            Mediator.Subscribe<WatchedObjectDestroyed>(this, msg =>
            {
                if (Address == nint.Zero || msg.Address != Address)
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
    public unsafe ulong EntityId => _gameObject->EntityId;
    public unsafe ulong GameObjectId => _gameObject->GetGameObjectId().Id;
    public string NameString { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public bool IsOwnerValid => Sundesmo.IsRendered;
    public unsafe bool IsRendered => _gameObject != null;

    public unsafe void ObjectRendered(GameObject* obj)
    {
        if (obj is null)
            throw new ArgumentNullException(nameof(obj));

        // Cancel any pending timeouts.
        _timeoutCTS.SafeCancel();

        // Init the object and set its name.
        _gameObject = obj;
        NameString = obj->NameString;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} rendered!", LoggerType.PairHandler);
    }

    public unsafe void ClearRenderedObject()
    {
        _gameObject = null;
        // do not clear the object name and other data yet, hold until timeout occurs, and then revert.
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} unrendered! Reverting in 10s unless reappearing.", LoggerType.PairHandler);
        StartTimeoutTask();
    }

    public void StartTimeoutTask()
    {
        if (!IsRendered) return;
        if (_timeoutTask is not null && !_timeoutTask.IsCompleted) return;

        _timeoutCTS = _timeoutCTS.SafeCancelRecreate();
        _timeoutTask = Task.Run(async () =>
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
                await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);

            _appearanceData = null;
            NameString = string.Empty;
            // Notify the owner that we went poofy or whatever if we need to here.
            Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} data has been reverted due to timeout.", LoggerType.PairHandler);
        });
    }

    public void StopTimeoutTask() => _timeoutCTS.SafeCancel();

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
        // redraw
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
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

    public async Task RevertAlterations(ushort objIdx)
    {
        if (Address == IntPtr.Zero) return;

        Logger.LogDebug($"Reverting {NameString}'s alterations.", LoggerType.PairHandler);
        if (_tempProfile != Guid.Empty)
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
        
        await _ipc.Glamourer.ReleaseActor(objIdx).ConfigureAwait(false);
        _ipc.Penumbra.RedrawGameObject(objIdx);
    }

    // NOTE: This can be very prone to crashing or inconsistant states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // store the name and address to reference removal properly.
        var name = NameString;
        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrWhiteSpace(name))
        {
            Logger.LogDebug($"Disposing [{name}] @ [{Address:X}]", LoggerType.PairHandler);
            Mediator.Publish(new EventMessage(new(name, Sundesmo.UserData.UID, DataEventType.Disposed, "Owned Object Disposed")));
        }

        // Do not dispose if the framework is unloading!
        // (means we are shutting down the game and cannot transmit calls to other ipcs without causing fatal errors!)
        if (Svc.Framework.IsFrameworkUnloading)
        {
            Logger.LogWarning($"Framework is unloading, skipping disposal for {name}({Sundesmo.GetNickAliasOrUid()})");
            return;
        }

        // Process off the disposal thread.
        _ = SafeRevertOnDisposal(Sundesmo.GetNickAliasOrUid(), name).ConfigureAwait(false);
    }

    /// <summary>
    ///     What to fire whenever called on application shutdown instead of the normal disposal method.
    /// </summary>
    private async Task SafeRevertOnDisposal(string nickAliasOrUid, string name)
    {
        try
        {
            // revert glamourer by name.
            if (!IsRendered && !string.IsNullOrEmpty(name))
            {
                Logger.LogTrace($"Reverting {name}(({nickAliasOrUid})'s actor state", LoggerType.PairHandler);
                await _ipc.Glamourer.ReleaseByName(name).ConfigureAwait(false);
            }

            // Check for zone relative data.
            if (!PlayerData.IsZoning && !PlayerData.InCutscene && IsRendered)
            {
                Logger.LogDebug($"{name}(({nickAliasOrUid}) is rendered, reverting by address/index.", LoggerType.PairHandler);
                await RevertAlterations(ObjIndex).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error reverting {name}({nickAliasOrUid} on shutdown: {ex}");
        }
        finally
        {
            // Clear internal data.
            _timeoutCTS.SafeCancel();
            _tempProfile = Guid.Empty;
            _appearanceData = new();
            NameString = string.Empty;
            unsafe { _gameObject = null; }
        }
    }
}
