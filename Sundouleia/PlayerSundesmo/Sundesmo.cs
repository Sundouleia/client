using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Sundouleia.Pairs.Factories;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs;

/// <summary>
///     Stores information about a pairing (Sundesmo) between 2 users. <para />
///     Created via the SundesmoFactory
/// </summary>
public class Sundesmo : IComparable<Sundesmo>
{
    private readonly ILogger<Sundesmo> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly ServerConfigManager _nickConfig;
    private readonly SundesmoHandlerFactory _factory;
    private readonly CharaObjectWatcher _watcher;

    private CancellationTokenSource _timeoutCTS = new();
    private Task? _timeoutTask; // may not need? Could fire off thread but unsure.

    // Associated Player Data (Created once online).
    private OnlineUser? _onlineUser;
    private PlayerHandler _player;
    // possibility that some alterations could go out of sync? but debug later.
    private PlayerOwnedHandler _mountMinion;
    private PlayerOwnedHandler _pet;
    private PlayerOwnedHandler _companion;

    public Sundesmo(UserPair userPairInfo, ILogger<Sundesmo> logger, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, ServerConfigManager nicks, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _mediator = mediator;
        _nickConfig = nicks;
        _watcher = watcher;

        UserPair = userPairInfo;
        // Create handlers for each of the objects.
        _player = factory.Create(this);
        _mountMinion = factory.Create(OwnedObject.MinionOrMount, this);
        _pet = factory.Create(OwnedObject.Pet, this);
        _companion = factory.Create(OwnedObject.Companion, this);
    }

    public bool IsReloading { get; private set; } = false;

    // Associated ServerData.
    public UserPair UserPair { get; init; }
    public UserData UserData => UserPair.User;
    public PairPerms OwnPerms => UserPair.OwnPerms;
    public GlobalPerms PairGlobals => UserPair.Globals;
    public PairPerms PairPerms => UserPair.Perms;

    // Internal Helpers
    public bool IsTemporary => UserPair.IsTemp;
    public bool IsOnline => _onlineUser != null;
    public bool IsRendered => _player.IsRendered;
    public bool IsPaused => OwnPerms.PauseVisuals;
    public string Ident => _onlineUser?.Ident ?? string.Empty;
    public string PlayerName => _player.NameString;
    public IntPtr PlayerAddress => IsRendered ? _player.Address : IntPtr.Zero;
    public PlayerHandler PlayerHandler => _player;

    /// <summary> Do not call if you are not certain the player is rendered! </summary>
    public ulong PlayerEntityId => _player.EntityId;

    /// <summary> Do not call if you are not certain the player is rendered! </summary>
    public ulong PlayerObjectId => _player.GameObjectId;

    // Comparable helper, allows us to do faster lookup.
    public int CompareTo(Sundesmo? other)
    {
        if (other is null) return 1;
        return string.Compare(UserData.UID, other.UserData.UID, StringComparison.Ordinal);
    }

    public void OpenSundouleiaSubMenu(IMenuItemClickedArgs args)
    {
        args.OpenSubmenu("Sundouleia Options", [ new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Profile").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { _mediator.Publish(new ProfileOpenMessage(UserData)); },
        }, new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Permissions").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { _mediator.Publish(new TogglePermissionWindow(this)); },
        }]);
    }

    public string? GetNickname() => _nickConfig.GetNicknameForUid(UserData.UID);
    public string GetNickAliasOrUid() => GetNickname() ?? UserData.AliasOrUID;

    // Reapply all existing data to all rendered objects.
    public void ReapplyAlterations()
    {
        _player.ReapplyAlterations().ConfigureAwait(false);
        _mountMinion.ReapplyAlterations().ConfigureAwait(false);
        _pet.ReapplyAlterations().ConfigureAwait(false);
        _companion.ReapplyAlterations().ConfigureAwait(false);
    }

    // Tinker with async / no async later.
    public async Task ApplyFullData(NewModUpdates newModData, VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null)
            await _player.UpdateAndApplyFullData(newModData, newIpc.PlayerChanges);

        if (newIpc.MinionMountChanges != null)
            await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);

        if (newIpc.PetChanges != null)
            await _pet.ApplyIpcData(newIpc.PetChanges);

        if (newIpc.CompanionChanges != null)
            await _companion.ApplyIpcData(newIpc.CompanionChanges);
    }

    public async void ApplyModData(NewModUpdates newModData)
        => await _player.UpdateAndApplyModData(newModData, true);

    public async void ApplyIpcData(VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges is not null)
            await _player.ApplyIpcData(newIpc.PlayerChanges, true);

        if (newIpc.MinionMountChanges is not null)
            await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);

        if (newIpc.PetChanges is not null)
            await _pet.ApplyIpcData(newIpc.PetChanges);

        if (newIpc.CompanionChanges is not null)
            await _companion.ApplyIpcData(newIpc.CompanionChanges);
    }

    public async void ApplyIpcSingle(OwnedObject obj, IpcKind kind, string newData)
    {
        await (obj switch
        {
            OwnedObject.Player => _player.ApplyIpcSingle(kind, newData),
            OwnedObject.MinionOrMount => _mountMinion.ApplyIpcSingle(kind, newData),
            OwnedObject.Pet => _pet.ApplyIpcSingle(kind, newData),
            OwnedObject.Companion => _companion.ApplyIpcSingle(kind, newData),
            _ => Task.CompletedTask,
        }).ConfigureAwait(false);
    }


    /// <summary>
    ///     Sets the sundesmo <see cref="OnlineUser"/> data, and updates their <see cref="SundesmoState"/>. <para />
    ///     TBD...
    /// </summary>
    public unsafe void MarkOnline(OnlineUser dto)
    {
        // Cancel any existing timeout task.
        _timeoutCTS.SafeCancel();
        // Set the OnlineUser & update the sundesmo state.
        _onlineUser = dto;

        var isVisible = _watcher.TryGetExisting(_player, out IntPtr playerAddr);
        // Notify other parts of Sundouleia they are online, and if we should send them full data.
        var needsFullData = isVisible && (IsReloading || !_player.HasAlterations);
        _mediator.Publish(new SundesmoOnline(this, needsFullData));
        // Ensure that IsReloading is false (prior to sending that they are online)
        IsReloading = false;

        // TryGetExisting returns true if already rendered, or if found in the watcher.
        if (isVisible && playerAddr != IntPtr.Zero)
        {
            // If not rendered, render them, otherwise, reapply their alterations.
            if (!IsRendered)
                _player.ObjectRendered((Character*)playerAddr);
            else
                _player.ReapplyAlterations().ConfigureAwait(false);
        }

        // If the player is not rendered, their owned objects should not be.
        if (!IsRendered)
            return;

        if (_watcher.TryGetExisting(_mountMinion, out IntPtr mountAddr))
        {
            _mountMinion.ObjectRendered((GameObject*)mountAddr);
            _mountMinion.ReapplyAlterations().ConfigureAwait(false);
        }

        if (_watcher.TryGetExisting(_pet, out IntPtr petAddr))
        {
            _pet.ObjectRendered((GameObject*)petAddr);
            _pet.ReapplyAlterations().ConfigureAwait(false);
        }

        if (_watcher.TryGetExisting(_companion, out IntPtr compAddr))
        {
            _companion.ObjectRendered((GameObject*)compAddr);
            _companion.ReapplyAlterations().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Should occur whenever the sundesmo reloads the plugin or performs a manual full disconnect. <para />
    ///     Any disconnects or temporary timeouts do not call this. It should be used to skip any timeouts,
    ///     and immediately remove any active alterations the next time they are marked offline. <para />
    ///     This also will prevent them from being sent into limbo in the distribution service.
    /// </summary>
    public void MarkForUnload()
    {
        // Whenever IsReloading is true, marking as offline will skip the
        // timeouts entirely and immediately force an unload of alterations.
        _logger.LogDebug($"Marking [{PlayerName}] ({GetNickAliasOrUid()}) as reloading.", LoggerType.PairManagement);
        IsReloading = true;
    }

    /// <summary>
    ///     Marks the sundesmo as offline, triggering the alteration data revert timeout. <para />
    ///     When this expires, all applied alterations will be removed, regardless of visibility state.
    /// </summary>
    public void MarkOffline()
    {
        _onlineUser = null;
        TriggerTimeoutTask();
        _mediator.Publish(new SundesmoOffline(this));
    }

    /// <summary>
    ///     Removes all applied appearance data for the sundesmo if rendered, 
    ///     and disposes all internal data.
    /// </summary>
    public void DisposeData()
    {
        _logger.LogDebug($"Disposing data for [{PlayerName}] ({GetNickAliasOrUid()})", UserData.AliasOrUID);
        // Cancel any existing timeout task, and then dispose of all data.
        _timeoutCTS.SafeCancel();
        _player.Dispose();
        _mountMinion.Dispose();
        _pet.Dispose();
        _companion.Dispose();
    }

    public void EndTimeout() => _timeoutCTS.SafeCancel();

    /// <summary>
    ///     Fired whenever the sundesmo goes offline, or they become unrendered. <para />
    ///     If this needs to be fired while the task is already active, return.
    /// </summary>
    public void TriggerTimeoutTask()
    {
        if (_timeoutTask != null && !_timeoutTask.IsCompleted)
            return;

        _timeoutCTS = _timeoutCTS.SafeCancelRecreate();
        // Process a task that awaits exactly 7 seconds, and then clear all alterations for all objects.
        // Note that this is across all handled objects of the sundesmo.
        _timeoutTask = Task.Run(async () =>
        {
            try
            {
                // If visible, send into limbo.
                if (IsRendered)
                    _mediator.Publish(new SundesmoEnteredLimbo(this));

                // Await for the defined time, then clear the alterations
                await Task.Delay(TimeSpan.FromSeconds(Constants.SundesmoTimeoutSeconds), _timeoutCTS.Token);

                // Clear regardless of render or not.
                _mediator.Publish(new SundesmoLeftLimbo(this));

                // Revert all alterations.
                _logger.LogDebug($"Timeout elapsed for [{PlayerName}] ({GetNickAliasOrUid()}). Clearing Alterations.", UserData.AliasOrUID);
                await _player.ClearAlterations(CancellationToken.None).ConfigureAwait(false);
                await _mountMinion.ClearAlterations().ConfigureAwait(false);
                await _pet.ClearAlterations().ConfigureAwait(false);
                await _companion.ClearAlterations().ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug($"Timeout cancelled for [{PlayerName}] ({GetNickAliasOrUid()}).", UserData.AliasOrUID);
                _mediator.Publish(new SundesmoLeftLimbo(this));
            }
        }, _timeoutCTS.Token);
    }

    // --------------- Helper Methods -------------------
    public bool IsMountMinionAddress(IntPtr addr)
    {
        // If the player is not rendered then it cannot be their mount/minion.
        if (!IsRendered)
            return false;

        // Otherwise obtain the object index of the player, shift it by 1, and search the index sorted object table.
        unsafe
        {
            var expected = (IntPtr)GameObjectManager.Instance()->Objects.IndexSorted[_player.ObjIndex + 1].Value;
            if (expected == addr)
                return true;
        }
        // Otherwise it failed.
        return false;
    }

    public bool IsPetAddress(IntPtr addr)
    {
        if (!IsRendered)
            return false;
        unsafe
        {
            var expected = (IntPtr)CharacterManager.Instance()->LookupPetByOwnerObject((BattleChara*)_player.Address);
            if (expected == addr)
                return true;
        }
        // Otherwise it failed.
        return false;
    }

    public bool IsCompanionAddress(IntPtr addr)
    {
        if (!IsRendered)
            return false;
        unsafe
        {
            var expected = (IntPtr)CharacterManager.Instance()->LookupBuddyByOwnerObject((BattleChara*)_player.Address);
            if (expected == addr)
                return true;
        }
        // Otherwise it failed.
        return false;
    }

    // ----- Debuggers -----
    public void DrawRenderDebug()
    {
        using var node = ImRaii.TreeNode($"Visible Info##{UserData.UID}-visible");
        if (!node) return;

        using (var t = ImRaii.Table("##debug-visible" + UserData.UID, 12, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (!t) return;
            ImGui.TableSetupColumn("OwnedObject");
            ImGui.TableSetupColumn("Rendered?");
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Address");
            ImGui.TableSetupColumn("ObjectIdx");
            ImGui.TableSetupColumn("EntityId");
            ImGui.TableSetupColumn("ObjectId");
            ImGui.TableSetupColumn("ParentId");
            ImGui.TableSetupColumn("DrawObjValid");
            ImGui.TableSetupColumn("RenderFlags");
            ImGui.TableSetupColumn("MdlInSlot");
            ImGui.TableSetupColumn("MdlFilesInSlot");
            ImGui.TableHeadersRow();
            // Handle Player.
            ImGuiUtil.DrawFrameColumn("Player");
            ImGui.TableNextColumn();
            CkGui.IconText(IsRendered ? FAI.Check : FAI.Times, IsRendered ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            ImGuiUtil.DrawFrameColumn(PlayerName);
            if (IsRendered)
            {
                ImGui.TableNextColumn();
                CkGui.ColorText($"{PlayerAddress:X}", ImGuiColors.TankBlue);
                ImGuiUtil.DrawFrameColumn(_player.ObjIndex.ToString());
                ImGuiUtil.DrawFrameColumn(PlayerEntityId.ToString());
                ImGuiUtil.DrawFrameColumn(PlayerObjectId.ToString());
                ImGuiUtil.DrawFrameColumn("N/A");

                ImGui.TableNextColumn();
                var drawObjValid = _player.DrawObjAddress != IntPtr.Zero;
                CkGui.IconText(drawObjValid ? FAI.Check : FAI.Times, drawObjValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);

                ImGui.TableNextColumn();
                CkGui.ColorText(_player.RenderFlags.ToString(), ImGuiColors.DalamudGrey2);

                if (drawObjValid)
                {
                    ImGui.TableNextColumn();
                    CkGui.IconText(_player.HasModelInSlotLoaded ? FAI.Check : FAI.Times, _player.HasModelInSlotLoaded ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                    ImGui.TableNextColumn();
                    CkGui.IconText(_player.HasModelFilesInSlotLoaded ? FAI.Check : FAI.Times, _player.HasModelFilesInSlotLoaded ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                }
            }
            ImGui.TableNextRow();

            // Handle Mount/Minion.
            ImGuiUtil.DrawFrameColumn("Mount/Minion");
            ImGui.TableNextColumn();
            CkGui.IconText(_mountMinion.IsRendered ? FAI.Check : FAI.Times, _mountMinion.IsRendered ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            ImGuiUtil.DrawFrameColumn(_mountMinion.NameString);
            if (_mountMinion.IsRendered)
            {
                ImGui.TableNextColumn();
                CkGui.IconText(_mountMinion.IsOwnerValid ? FAI.Check : FAI.Times, _mountMinion.IsOwnerValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                ImGui.TableNextColumn();
                CkGui.ColorText($"{_mountMinion.Address:X}", ImGuiColors.TankBlue);
                ImGuiUtil.DrawFrameColumn(_mountMinion.ObjIndex.ToString());
                ImGuiUtil.DrawFrameColumn(_mountMinion.EntityId.ToString());
                ImGuiUtil.DrawFrameColumn(_mountMinion.GameObjectId.ToString());
                ImGuiUtil.DrawFrameColumn(_mountMinion.DataState.OwnerId.ToString());
            }
            ImGui.TableNextRow();

            // Handle Pet.
            ImGuiUtil.DrawFrameColumn("Pet");
            ImGui.TableNextColumn();
            CkGui.IconText(_pet.IsRendered ? FAI.Check : FAI.Times, _pet.IsRendered ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            ImGuiUtil.DrawFrameColumn(_pet.NameString);
            if (_pet.IsRendered)
            {
                ImGui.TableNextColumn();
                CkGui.IconText(_pet.IsOwnerValid ? FAI.Check : FAI.Times, _pet.IsOwnerValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                ImGui.TableNextColumn();
                CkGui.ColorText($"{_pet.Address:X}", ImGuiColors.TankBlue);
                ImGuiUtil.DrawFrameColumn(_pet.ObjIndex.ToString());
                ImGuiUtil.DrawFrameColumn(_pet.EntityId.ToString());
                ImGuiUtil.DrawFrameColumn(_pet.GameObjectId.ToString());
                ImGuiUtil.DrawFrameColumn(_pet.DataState.OwnerId.ToString());
            }
            ImGui.TableNextRow();

            // Handle Companion.
            ImGuiUtil.DrawFrameColumn("Companion");
            ImGui.TableNextColumn();
            CkGui.IconText(_companion.IsRendered ? FAI.Check : FAI.Times, _companion.IsRendered ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
            ImGuiUtil.DrawFrameColumn(_companion.NameString);
            if (_companion.IsRendered)
            {
                ImGui.TableNextColumn();
                CkGui.IconText(_companion.IsOwnerValid ? FAI.Check : FAI.Times, _companion.IsOwnerValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
                ImGui.TableNextColumn();
                CkGui.ColorText($"{_companion.Address:X}", ImGuiColors.TankBlue);
                ImGuiUtil.DrawFrameColumn(_companion.ObjIndex.ToString());
                ImGuiUtil.DrawFrameColumn(_companion.EntityId.ToString());
                ImGuiUtil.DrawFrameColumn(_companion.GameObjectId.ToString());
                ImGuiUtil.DrawFrameColumn(_companion.DataState.OwnerId.ToString());
            }
        }

        _player.DrawDebugInfo();
        _mountMinion.DrawDebugInfo();
        _pet.DrawDebugInfo();
        _companion.DrawDebugInfo();
    }
}
