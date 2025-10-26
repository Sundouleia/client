using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Interop;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;

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
    private readonly CharaObjectWatcher _watcher;

    private CancellationTokenSource _runtimeCTS = new();

    public OwnedObject ObjectType { get; init; }
    public Sundesmo Sundesmo { get; init; }
    private unsafe GameObject* _gameObject = null;
    private Guid _tempProfile = Guid.Empty;
    private IpcDataCache? _appearanceData = new();

    public PlayerOwnedHandler(OwnedObject kind, Sundesmo sundesmo, ILogger<PlayerOwnedHandler> logger,
        SundouleiaMediator mediator, IpcManager ipc, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        ObjectType = kind;
        Sundesmo = sundesmo;

        _ipc = ipc;
        _watcher = watcher;

        Mediator.Subscribe<WatchedObjectCreated>(this, msg => MarkVisibleForAddress(msg.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, msg => UnrenderPlayer(msg.Address));
    }

    // Public accessors.
    public GameObject DataState { get { unsafe { return *_gameObject; } } }
    public unsafe IntPtr Address => (nint)_gameObject;
    public unsafe ushort ObjIndex => _gameObject->ObjectIndex;
    public unsafe ulong EntityId => _gameObject->EntityId;
    public unsafe ulong GameObjectId => _gameObject->GetGameObjectId().ObjectId;
    public unsafe IntPtr DrawObjAddress => (nint)_gameObject->DrawObject;
    public unsafe int RenderFlags => _gameObject->RenderFlags;
    public unsafe bool HasModelInSlotLoaded => ((CharacterBase*)_gameObject->DrawObject)->HasModelInSlotLoaded != 0;
    public unsafe bool HasModelFilesInSlotLoaded => ((CharacterBase*)_gameObject->DrawObject)->HasModelFilesInSlotLoaded != 0;
    public string NameString { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public bool IsOwnerValid => Sundesmo.IsRendered;
    public unsafe bool IsRendered => _gameObject != null;
    public bool HasAlterations => _appearanceData != null;

    #region Rendering
    // Initializes Rendering for this object if the address matches the OnlineUserIdent.
    // Called by the Watcher's mediator subscriber. Not intended for public access.
    // Assumes the passed in address is a visible Character*
    private void MarkVisibleForAddress(IntPtr address)
    {
        if (Address != IntPtr.Zero || !Sundesmo.IsRendered || !Sundesmo.IsOnline) return;
        var isMatch = ObjectType switch
        {
            OwnedObject.MinionOrMount => Sundesmo.IsMountMinionAddress(address),
            OwnedObject.Pet => Sundesmo.IsPetAddress(address),
            OwnedObject.Companion => Sundesmo.IsCompanionAddress(address),
            _ => false
        };
        if (!isMatch) return;
        Logger.LogDebug($"Matched {Sundesmo.GetNickAliasOrUid()}'s {ObjectType} to a created object @ [{address:X}]", LoggerType.PairHandler);
        MarkRenderedInternal(address);
    }

    // Publicly accessible method to try and identify the address of an online user to mark them as visible.
    internal async Task SetVisibleIfRendered()
    {
        if (!Sundesmo.IsOnline || !IsOwnerValid) return; // Must be online.
        // If already rendered, reapply alterations and return.
        if (IsRendered)
        {
            Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()})'s {ObjectType} is already rendered, reapplying alterations.", LoggerType.PairHandler);
            await ReInitializeInternal().ConfigureAwait(false);
        }
        else if (_watcher.TryGetExisting(this, out IntPtr playerAddr))
        {
            Logger.LogDebug($"Matched ({Sundesmo.GetNickAliasOrUid()})'s {ObjectType} to existing object @ [{playerAddr:X}]", LoggerType.PairHandler);
            MarkRenderedInternal(playerAddr); 
        }
    }

    private unsafe void MarkRenderedInternal(IntPtr address)
    {
        // End Timeouts if running as one of our states became valid again.
        Sundesmo.EndTimeout();
        // Set the game data.
        _gameObject = (GameObject*)address;
        NameString = _gameObject->NameString;
        // Notify other services.
        Logger.LogInformation($"({Sundesmo.GetNickAliasOrUid()})'s {ObjectType} rendered!", LoggerType.PairHandler);
        ReInitializeInternal().ConfigureAwait(false);
    }

    private async Task ReInitializeInternal()
    {
        // If they are online and have alterations, reapply them. Otherwise, exit.
        if (!Sundesmo.IsOnline || !HasAlterations)
            return;

        // Await until we know the player has absolutely finished loading in.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        Logger.LogDebug($"[{Sundesmo.GetNickAliasOrUid()}] is fully loaded, reapplying alterations.", LoggerType.PairHandler);
        await ReapplyAlterations().ConfigureAwait(false);
    }

    /// <summary>
    ///     Fired whenever the player is unrendered from the game world. <para />
    ///     Not linked to appearance alterations, but does begin its timeout if not set.
    /// </summary>
    private unsafe void UnrenderPlayer(IntPtr address)
    {
        if (Address == IntPtr.Zero || address != Address)
            return;
        // Clear the GameData.
        _gameObject = null;
        // Refresh the list to reflect visible state.
        Logger.LogDebug($"Marking {Sundesmo.GetNickAliasOrUid()}'s {ObjectType} as unrendered @ [{address:X}]", LoggerType.PairHandler);
    }

    private async Task WaitUntilValidDrawObject(CancellationToken timeoutToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, _runtimeCTS.Token);
        while (!cts.IsCancellationRequested)
        {
            // Yes this is dependent on our current state.
            if (!PlayerData.IsZoning && IsObjectLoaded())
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     There are conditions where an object can be rendered / created, but not drawable, or currently bring drawn. <para />
    ///     This mainly occurs on login or when transferring between zones, but can also occur during redraws and such.
    ///     We can get around this by checking for various draw conditions.
    /// </summary>
    public unsafe bool IsObjectLoaded()
    {
        if (!IsRendered) return false; // Not even rendered, does not exist.
        if (DrawObjAddress == IntPtr.Zero) return false; // Character object does not exist yet.
        if (RenderFlags == 2048) return false; // Render Flag for "IsLoading" (2048 == 0b100000000000)
        if (HasModelInSlotLoaded) return false; // There are models that need to still be loaded into the DrawObject slots.
        if (HasModelFilesInSlotLoaded) return false; // There are model files that need to still be loaded into the DrawObject slots.
        return true;
    }
    #endregion Rendering

    #region Altaration Control
    /// <summary>
    ///     Reverts the rendered alterations on a player. <b> This does not delete the alteration data. </b>
    /// </summary>
    public async Task RevertRenderedAlterations()
    {
        var idx = IsRendered ? ObjIndex : (ushort)0;
        await RevertAlterationsInternal(NameString, idx).ConfigureAwait(false);
    }

    private async Task RevertAlterationsInternal(string name, ushort objectIdx)
    {
        // Revert based on rendered state.
        if (!PlayerData.IsZoning && !PlayerData.InCutscene && IsRendered)
        {
            await _ipc.Glamourer.ReleaseActor(objectIdx).ConfigureAwait(false);
            if (_tempProfile != Guid.Empty)
            {
                await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
                _tempProfile = Guid.Empty;
            }
        }
        else if (!string.IsNullOrEmpty(name))
        {
            // Glamourer Fallback.
            await _ipc.Glamourer.ReleaseByName(name).ConfigureAwait(false);
            // maybe CPlus here but idk.
        }
    }

    public async Task UpdateAndApplyIpc(IpcDataUpdate ipcChanges, bool isInitialData)
    {
        var visualDiff = UpdateDataIpc(ipcChanges);
        if (visualDiff is IpcKind.None) 
            return;
        // Apply and redraw regardless (for now).
        await ApplyVisuals(visualDiff).ConfigureAwait(false);
        RedrawObject();

    }

    public async Task UpdateAndApplyIpc(IpcKind kind, string newData)
    {
        if (!UpdateDataIpc(kind, newData))
            return;
        // Apply and redraw regardless (for now).
        await ApplyVisuals(kind).ConfigureAwait(false);
        RedrawObject();
    }
    public async Task ReapplyAlterations()
    {
        // Apply changes, then redraw if necessary.
        await ReapplyVisuals().ConfigureAwait(false);
        // redraw regardless for now i guess, idk.
        RedrawObject();
    }
    #endregion Altaration Control

    #region Alteration Updates
    // Returns the changes applied.
    private IpcKind UpdateDataIpc(IpcDataUpdate ipcData)
    {
        _appearanceData ??= new();
        return _appearanceData.UpdateCache(ipcData);
    }

    private bool UpdateDataIpc(IpcKind kind, string newData)
    {
        _appearanceData ??= new();
        return _appearanceData.UpdateCacheSingle(kind, newData);
    }
    #endregion Alteration Updates

    #region Alteration Application
    private async Task ApplyAlterations(IpcKind visualChanges)
    {
        if (visualChanges is IpcKind.None)
            return;

        // If the visuals applied with redraw changes, redraw.
        await ApplyVisuals(visualChanges).ConfigureAwait(false);
        RedrawObject();
        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had alterations applied. (Changes: {visualChanges})", LoggerType.PairHandler);
    }

    // True if data was applied, false otherwise. (useful for redraw)
    private async Task ApplyVisuals(IpcKind changes)
    {
        if (!IsRendered || _appearanceData is null)
            return;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return;

        Logger.LogDebug($"Reapplying visual data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        if (changes.HasAny(IpcKind.Glamourer))
            await ApplyGlamourer().ConfigureAwait(false);
        if (changes.HasAny(IpcKind.CPlus))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had their visuals reapplied.", LoggerType.PairHandler);
    }

    // Everything in owned objects should require a reapply.
    private async Task ApplyVisualsSingle(IpcKind kind)
    {
        if (!IsRendered || _appearanceData is null)
            return;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return;

        if (kind.HasAny(IpcKind.Glamourer)) 
            await ApplyGlamourer().ConfigureAwait(false);
        else if (kind.HasAny(IpcKind.CPlus))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had a single IPC change ({kind})", LoggerType.PairHandler);
    }

    private async Task ReapplyVisuals()
    {
        if (!IsRendered || _appearanceData is null)
            return;

        await WaitUntilValidDrawObject().ConfigureAwait(false);

        if (_runtimeCTS.Token.IsCancellationRequested)
            return;

        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Glamourer]))
            await ApplyGlamourer().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had alterations reapplied.", LoggerType.PairHandler);
    }
    #endregion Alteration Application

    #region Ipc Helpers

    private async Task ApplyGlamourer()
    {
        Logger.LogDebug($"Applying ({Sundesmo.GetNickAliasOrUid()}'s {ObjectType}) Glamourer data", LoggerType.PairAppearance);
        await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);
    }

    private async Task ApplyCPlus()
    {
        if (string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]) && _tempProfile != Guid.Empty)
        {
            Logger.LogDebug($"Reverting ({Sundesmo.GetNickAliasOrUid()}'s {ObjectType}) CPlus profile", LoggerType.PairAppearance);
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            Logger.LogDebug($"Applying ({Sundesmo.GetNickAliasOrUid()}'s {ObjectType}) CPlus profile", LoggerType.PairAppearance);
            _tempProfile = await _ipc.CustomizePlus.ApplyTempProfile(this, _appearanceData.Data[IpcKind.CPlus]).ConfigureAwait(false);
        }
    }

    public void RedrawObject()
    {
        if (IsRendered)
        {
            Logger.LogDebug($"Redrawing ({Sundesmo.GetNickAliasOrUid()}'s {ObjectType}) due to alteration changes.", LoggerType.PairHandler);
            _ipc.Penumbra.RedrawGameObject(ObjIndex);
        }
    }
    #endregion Ipc Helpers

    // NOTE: This can be very prone to crashing or inconsistent states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IntPtr addr = IsRendered ? Address : IntPtr.Zero;
        ushort objIdx = IsRendered ? ObjIndex : (ushort)0;
        // Cancel any tasks depending on runtime. (Do not dispose)
        _runtimeCTS.SafeCancel();
        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrEmpty(NameString))
            Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.Disposed, "Disposed")));

        // Do not dispose-revert if the framework is unloading!
        // (means we are shutting down the game and cannot transmit calls to other ipcs without causing fatal errors!)
        if (Svc.Framework.IsFrameworkUnloading)
        {
            Logger.LogWarning($"Framework is unloading, skipping disposal for {NameString}({Sundesmo.GetNickAliasOrUid()})");
            return;
        }

        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrWhiteSpace(NameString))
            Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.Disposed, "Owned Object Disposed")));

        // Do not dispose if the framework is unloading!
        // (means we are shutting down the game and cannot transmit calls to other ipcs without causing fatal errors!)
        if (Svc.Framework.IsFrameworkUnloading)
        {
            Logger.LogWarning($"Framework is unloading, skipping disposal for {NameString}({Sundesmo.GetNickAliasOrUid()})");
            return;
        }

        // Process off the disposal thread. (Avoids deadlocking on plugin shutdown)
        _ = SafeRevertOnDisposal(Sundesmo.GetNickAliasOrUid(), NameString, objIdx).ConfigureAwait(false);
    }

    /// <summary>
    ///     What to fire whenever called on application shutdown instead of the normal disposal method.
    /// </summary>
    private async Task SafeRevertOnDisposal(string nickAliasOrUid, string name, ushort objIdx)
    {
        try
        {
            await RevertRenderedAlterations().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error reverting ({nickAliasOrUid}'s {name}) on shutdown: {ex}");
        }
        finally
        {
            // Clear internal data.
            _tempProfile = Guid.Empty;
            _appearanceData = null;
            NameString = string.Empty;
            unsafe { _gameObject = null; }
        }
    }

    public void DrawDebugInfo()
    {
        using var node = ImRaii.TreeNode($"{ObjectType} Alterations##{Sundesmo.UserData.UID}-alterations-{ObjectType}");
        if (!node) return;

        if (_appearanceData is null)
            CkGui.ColorText("No Alteration Data", ImGuiColors.DalamudRed);
        else
            DebugAppearance();
    }
    private void DebugAppearance()
    {
        using var node = ImRaii.TreeNode($"Appearance##{Sundesmo.UserData.UID}-{ObjectType}-appearance-player");
        if (!node) return;

        using var table = ImRaii.Table($"bawawa##{Sundesmo.UserData.UID}-{ObjectType}-appearance-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Data Type");
        ImGui.TableSetupColumn("Reapply Test");
        ImGui.TableSetupColumn("Data Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        ImGui.Text("Glamourer");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]), id: $"{Sundesmo.UserData.UID}-{ObjectType}-glamourer-reapply"))
            UiService.SetUITask(ApplyGlamourer);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.Glamourer]);

        ImGui.TableNextColumn();
        ImGui.Text("CPlus");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.CPlus]), id: $"{Sundesmo.UserData.UID}-{ObjectType}-cplus-reapply"))
            UiService.SetUITask(ApplyCPlus);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.CPlus]);
    }
}
