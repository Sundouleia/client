using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Interop;
using Sundouleia.Pairs.Enums;
using Sundouleia.PlayerClient;
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
    private readonly AccountConfig _config;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    // Helper Refs
    private RedrawManager _redrawer { get; init; }
    public OwnedObject ObjectType { get; init; }
    public Sundesmo Sundesmo { get; init; }

    // Task Control
    private CancellationTokenSource _runtimeCTS = new();

    // Internal Cache
    private unsafe GameObject* _gameObject = null;
    private Guid _tempProfile = Guid.Empty;
    private IpcDataCache? _appearanceData = new();

    private bool _hasAlterations => _appearanceData is not null;
    private bool _blockApplication => !_hasAlterations || !IsRendered || _config.ConnectionKind is ConnectionKind.StreamerMode;

    public PlayerOwnedHandler(OwnedObject kind, Sundesmo sundesmo, RedrawManager redrawer, ILogger<PlayerOwnedHandler> logger,
        SundouleiaMediator mediator, AccountConfig config, IpcManager ipc, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _redrawer = redrawer;
        ObjectType = kind;
        Sundesmo = sundesmo;

        _config = config;
        _ipc = ipc;
        _watcher = watcher;

        Mediator.Subscribe<WatchedObjectCreated>(this, msg => MarkVisibleForAddress(msg.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, msg => UnrenderObject(msg.Address));
    }

    // Public accessors.
    public GameObject DataState { get { unsafe { return *_gameObject; } } }
    public unsafe IntPtr Address => (nint)_gameObject;
    public unsafe ushort ObjIndex => _gameObject->ObjectIndex;
    public unsafe ulong EntityId => _gameObject->EntityId;
    public unsafe ulong GameObjectId => _gameObject->GetGameObjectId().ObjectId;
    public unsafe IntPtr DrawObjAddress => (nint)_gameObject->DrawObject;
    public unsafe ulong RenderFlags => (ulong)_gameObject->RenderFlags;
    public unsafe bool HasModelInSlotLoaded => ((CharacterBase*)_gameObject->DrawObject)->HasModelInSlotLoaded != 0;
    public unsafe bool HasModelFilesInSlotLoaded => ((CharacterBase*)_gameObject->DrawObject)->HasModelFilesInSlotLoaded != 0;
    public string NameString { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public bool IsOwnerValid => Sundesmo.IsRendered;
    public unsafe bool IsRendered => _gameObject != null;

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
        // Set the game data.
        _gameObject = (GameObject*)address;
        NameString = _gameObject->NameString;
        
        // Notify other services.
        Logger.LogInformation($"({Sundesmo.GetNickAliasOrUid()})'s {ObjectType} rendered!", LoggerType.PairHandler);

        // ReInitialize alterations after becoming visible again.
        ReInitializeInternal().ConfigureAwait(false);
    }

    private async Task ReInitializeInternal()
    {
        // If they are online and have alterations, reapply them. Otherwise, exit.
        if (!Sundesmo.IsOnline || !_hasAlterations)
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
    private unsafe void UnrenderObject(IntPtr address)
    {
        if (Address == IntPtr.Zero || address != Address)
            return;
        // Clear the GameData.
        _gameObject = null;
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

    /// <inheritdoc cref="RevertAlterations(string, ushort?, CancellationToken)"/>
    public async Task RevertAlterations(CancellationToken ct = default)
        => await RevertAlterations(NameString, IsRendered ? ObjIndex : null, ct).ConfigureAwait(false);

    /// <summary>
    ///     Reverts the rendered alterations on the Sundesmo-owned object. <br/>
    ///     <b>This does not delete the alteration data. </b>
    /// </summary>
    private async Task RevertAlterations(string name, ushort? objIdx = null, CancellationToken ct = default)
    {
        // Revert the customize+ alterations that do not require actor info.
        await RevertAssignedAlterations().ConfigureAwait(false);

        // Revert glamourer based on the rendered state.
        if (objIdx is { } idx)
            await _ipc.Glamourer.ReleaseActor(idx).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(name))
            await _ipc.Glamourer.ReleaseByName(name).ConfigureAwait(false);
        // Otherwise do nothing.
    }

    /// <summary>
    ///     Revert alterations that entrusted us with an ID, and dont require actor info. <para/>
    ///     <b>Currently only Customize+</b>
    /// </summary>
    private async Task RevertAssignedAlterations()
    {
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
    }

    public async Task ClearAlterations(CancellationToken ct = default)
    {
        if (_tempProfile != Guid.Empty)
        {
            Logger.LogError("You are clearing alterations prior to reverting them!\n" +
                "This will have consequences on the stability of your data sync!");
            Logger.LogError("If you are getting this, find out why it is happening!");
        }

        _appearanceData = null;
        _tempProfile = Guid.Empty;
    }

    public async Task UpdateAndApplyIpc(IpcDataUpdate ipcChanges, bool isInitialData)
    {
        // Update the IPC data, if there is any differences, apply and inform the redrawer.
        if (UpdateDataIpc(ipcChanges) is { } diff && diff is not IpcKind.None)
        {
            // Apply the visuals, and inform the redrawer.
            var redrawType = await ApplyAlterations(diff).ConfigureAwait(false);
            _redrawer.RedrawOwnedObject(this, redrawType);
        }
    }

    public async Task UpdateAndApplyIpc(IpcKind kind, string newData)
    {
        // If nothing updates, return.
        if (!UpdateDataIpc(kind, newData))
            return;
        // Otherwise, set the updateKind based on what IpcKind it was.
        var redrawType = await ApplyVisuals(kind).ConfigureAwait(false);
        _redrawer.RedrawOwnedObject(this, redrawType);
    }

    public async Task ReapplyAlterations()
    {
        // Reapply the visuals, then request a redraw or enqueue it.
        var redrawType = await ReapplyVisuals().ConfigureAwait(false);
        _redrawer.RedrawOwnedObject(this, redrawType);
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
    private async Task<RedrawKind> ApplyAlterations(IpcKind visualChanges)
    {
        if (visualChanges is IpcKind.None)
            return RedrawKind.None;

        // If the visuals applied with redraw changes, redraw.
        RedrawKind redraw = await ApplyVisuals(visualChanges).ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had alterations applied. (Changes: {visualChanges})", LoggerType.PairHandler);

        return redraw;
    }

    // True if data was applied, false otherwise. (useful for redraw)
    private async Task<RedrawKind> ApplyVisuals(IpcKind changes)
    {
        if (_blockApplication)
            return RedrawKind.None;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return RedrawKind.None;

        Logger.LogDebug($"Reapplying visual data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        if (changes.HasAny(IpcKind.Glamourer))
            await ApplyGlamourer().ConfigureAwait(false);
        if (changes.HasAny(IpcKind.CPlus))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} had their visuals reapplied.", LoggerType.PairHandler);
        return changes.HasAny(IpcKind.Glamourer) ? RedrawKind.Reapply : RedrawKind.None;
    }

    // Everything in owned objects should require a reapply.
    private async Task ApplyVisualsSingle(IpcKind kind)
    {
        if (_blockApplication)
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

    private async Task<RedrawKind> ReapplyVisuals()
    {
        if (_blockApplication)
            return RedrawKind.None;

        await WaitUntilValidDrawObject().ConfigureAwait(false);

        if (_runtimeCTS.Token.IsCancellationRequested)
            return RedrawKind.None;

        if (!string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.Glamourer]))
            await ApplyGlamourer().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))
            await ApplyCPlus().ConfigureAwait(false);

        Logger.LogInformation($"{Sundesmo.GetNickAliasOrUid()}'s {ObjectType} reapplied alterations.", LoggerType.PairHandler);
        return !string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Glamourer]) ? RedrawKind.Reapply : RedrawKind.None;
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
    #endregion Ipc Helpers

    /// <summary>
    ///     Perform a true disposal on the handler, reverting rendered alterations,
    ///     clearing alteration data, and disposing of the runtime CTS. <para />
    ///     <b>Do not call this unless you are disposing the Sundesmo.</b>
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IntPtr addr = IsRendered ? Address : IntPtr.Zero;
        ushort objIdx = IsRendered ? ObjIndex : (ushort)0;

        // Stop any actively running tasks.
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

        // Process off the disposal thread. (Avoids deadlocking on plugin shutdown)
        // Everything in here, if it errors, should not crash the game as it is fire and forget.
        _ = Task.Run(async () =>
        {
            var nickAliasOrUid = Sundesmo.GetNickAliasOrUid();
            var name = NameString;
            try
            {
                await RevertAlterations().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error reverting ({nickAliasOrUid}'s {name}) on disposal: {ex}");
            }
            finally
            {
                // Clear internal data.
                _tempProfile = Guid.Empty;
                _appearanceData = null;
                NameString = string.Empty;
                unsafe { _gameObject = null; }
            }
        });
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
