using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using TerraFX.Interop.Windows;
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
                // do not create if they are not online or already created.
                if (Address != IntPtr.Zero || !Sundesmo.IsRendered || !Sundesmo.IsOnline) return;
                // Ignore if the address is not the expected address.
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
                if (Address == IntPtr.Zero || msg.Address != Address) return;
                // Mark the object as unrendered, triggering the timeout.
                ObjectUnrendered();
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
    public unsafe ulong GameObjectId => _gameObject->GetGameObjectId().ObjectId;
    public string NameString { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public bool IsOwnerValid => Sundesmo.IsRendered;
    public unsafe bool IsRendered => _gameObject != null;
    public bool HasAlterations => _appearanceData != null;

    /// <summary>
    ///     Fired whenever the OwnedObject is rendered in the game world. <para />
    ///     This is not to be linked to the appearance alterations in any shape or form.
    /// </summary>
    /// <remarks>
    ///     Due to the sundesmo being on a single timeout for alterations there is a change 
    ///     we may have cached alterations for a different object. If we need to track the
    ///     previous type somehow we can add it later.
    /// </remarks>
    /// <exception cref="ArgumentNullException"></exception>
    public unsafe void ObjectRendered(GameObject* obj)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        // Init the object and set its name.
        _gameObject = obj;
        NameString = obj->NameString;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} rendered!", LoggerType.PairHandler);
    }

    /// <summary>
    ///     Fired whenever the player is unrendered from the game world. <para />
    ///     Not linked to appearance alterations.
    /// </summary>
    private void ObjectUnrendered()
    {
        if (!IsRendered) return;
        // Clear the GameData.
        unsafe { _gameObject = null; }
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {ObjectType} unrendered!", LoggerType.PairHandler);
    }

    /// <summary>
    ///     Removes all cached alterations from the OwnedObject and reverts their state. <para />
    ///     Different reverts will occur based on the rendered state.
    /// </summary>
    public async Task ClearAlterations()
    {
        // Regardless of rendered state, we should revert any C+ Data if we have any.
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }

        var isValid = !PlayerData.IsZoning && !PlayerData.InCutscene && IsRendered;

        // Revert glamourer based on rendered state.
        if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
        {
            if (isValid)
                await _ipc.Glamourer.ReleaseActor(ObjIndex).ConfigureAwait(false);
            else
                await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);
        }

        if (isValid)
            _ipc.Penumbra.RedrawGameObject(ObjIndex);
        // Clear out the alterations data (keep NameString alive so One-Time-Init does not re-fire.)
        _appearanceData = null;
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
        // redraw
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
        Logger.LogInformation($"Reapplied ({Sundesmo.GetNickAliasOrUid()})'s alterations.", LoggerType.PairHandler);
    }

    // Thankfully only ever need to worry about CPlus and glamourer here!.
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

    // NOTE: This can be very prone to crashing or inconsistent states!
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
            await ClearAlterations().ConfigureAwait(false);
            Logger.LogInformation($"Reverted {name}({nickAliasOrUid}) on shutdown.", LoggerType.PairHandler);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error reverting {name}({nickAliasOrUid} on shutdown: {ex}");
        }
        finally
        {
            // Clear internal data.
            NameString = string.Empty;
            unsafe { _gameObject = null; }
        }
    }

    public void DrawDebugInfo()
    {
        using var node = ImRaii.TreeNode($"Alterations##{Sundesmo.UserData.UID}-alterations");
        if (!node) return;

        if (_appearanceData is null)
            CkGui.ColorText("No Alteration Data", ImGuiColors.DalamudRed);
        else
        {
            using (var table = ImRaii.Table("sundesmo-appearance", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter))
            {
                if (!table) return;

                ImGui.TableSetupColumn("Data Type");
                ImGui.TableSetupColumn("Data Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                ImGui.TableNextColumn();
                ImGui.Text("Glamourer");
                ImGui.TableNextColumn();
                ImGui.Text(_appearanceData!.Data[IpcKind.Glamourer]);

                ImGui.TableNextColumn();
                ImGui.Text("CPlus");
                ImGui.TableNextColumn();
                ImGui.Text(_appearanceData.Data[IpcKind.CPlus]);
            }
        }
    }
}
