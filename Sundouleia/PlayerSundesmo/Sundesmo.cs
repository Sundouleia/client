using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs;

/// <summary>
///     Stores information about a pairing (Sundesmo) between 2 users.
/// </summary>
/// <remarks>
///     The handlers associated with the sundesmo must be disposed of when removing.
///     However, don't make Sundesmo itself IDiposable.
/// </remarks>
public sealed class Sundesmo : DisposableMediatorSubscriberBase, IComparable<Sundesmo>
{
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favorites;
    private readonly NicksConfig _nicks;
    private readonly LimboStateManager _limboManager;
    private readonly CharaObjectWatcher _watcher;
    private readonly RedrawManager _redrawer;

    // Associated Player Data (Created once online).
    private OnlineUser? _onlineUser;

    // Tracks information about their OwnedObjects. (Where the rendered data is)
    private PlayerHandler _player;
    private PlayerOwnedHandler _mountMinion;
    private PlayerOwnedHandler _pet;
    private PlayerOwnedHandler _companion;

    public Sundesmo(UserPair userPairInfo, ILogger<Sundesmo> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folderConfig, FavoritesConfig favorites, NicksConfig nicks,
        SundesmoHandlerFactory factory, LimboStateManager limbo, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _folderConfig = folderConfig;
        _favorites = favorites;
        _nicks = nicks;
        _limboManager = limbo;
        _watcher = watcher;

        UserPair = userPairInfo;
        // Init the redraw manager for this Sundesmo.
        _redrawer = factory.CreateRM(this);

        // Using this Sundesmo and the created redraw manager, construct all of the Sundesmo's possible handlers.
        _player = factory.Create(this, _redrawer);
        _mountMinion = factory.Create(OwnedObject.MinionOrMount, this, _redrawer);
        _pet = factory.Create(OwnedObject.Pet, this, _redrawer);
        _companion = factory.Create(OwnedObject.Companion, this, _redrawer);

        Logger.LogTrace($"Initialized Sundesmo for ({GetNickAliasOrUid()}).", LoggerType.PairManagement);
        // Mass revert when modes are switched to spesific values.
        Mediator.Subscribe<ConnectionKindChanged>(this, async _ =>
        {
            // We dont care if previous state was FullPause.
            if (_.PrevState is ConnectionKind.FullPause) return;

            // If we switched to StreamerMode, and we are rendered, revert alterations.
            if (_.PrevState is not ConnectionKind.FullPause && _.NewState is ConnectionKind.StreamerMode && IsRendered)
            {
                Logger.LogDebug($"{PlayerName}({GetNickAliasOrUid()}) reverted alterations upon enabling StreamerMode", LoggerType.PairHandler);
                await RevertRenderedAlterations().ConfigureAwait(false);
            }
            // Otherwise if the previous state was Streamer Mode and the new state was not FullPause, Reapply the alterations if rendered.
            else if (_.PrevState is ConnectionKind.StreamerMode && _.NewState is not ConnectionKind.FullPause && IsRendered)
            {
                Logger.LogDebug($"{PlayerName}({GetNickAliasOrUid()}) now reinitializing upon disabling StreamerMode.", LoggerType.PairHandler);
                // Public way to access ReinitializeInternal when already rendered, can optimize later
                SetVisibleIfRendered();
            }
        });
    }

    // Associated ServerData.
    public UserPair     UserPair { get; private set; }
    public UserData     UserData    => UserPair.User;
    public PairPerms    OwnPerms    => UserPair.OwnPerms;
    public GlobalPerms  PairGlobals => UserPair.Globals;
    public PairPerms    PairPerms   => UserPair.Perms;

    // Shared MoodleData. Fresh / empty if not shared.
    public MoodleData   SharedData { get; private set; } = new();

    // Internal Helpers
    public bool IsReloading { get; private set; } = false;

    public bool IsTemporary => UserPair.IsTemporary;
    public bool IsRendered => _player.IsRendered;
    public bool IsOnline => _onlineUser != null;
    public bool IsFavorite => _favorites.SundesmoUids.Contains(UserData.UID);
    public bool IsPaused => OwnPerms.PauseVisuals;
    public string Ident => _onlineUser?.Ident ?? string.Empty;
    public string PlayerName => _player.NameString;
    public string PlayerNameWorld => _player.NameWithWorld;
    public IntPtr PlayerAddress => IsRendered ? _player.Address : IntPtr.Zero;
    public ushort ObjIndex => _player.ObjIndex;
    public ulong PlayerEntityId => _player.EntityId;
    public ulong PlayerObjectId => _player.GameObjectId;
    public PlayerHandler PlayerHandler => _player; // Phase this out


    // Comparable helper, allows us to do faster lookup.
    public int CompareTo(Sundesmo? other)
    {
        if (other is null) return 1;
        return string.Compare(UserData.UID, other.UserData.UID, StringComparison.Ordinal);
    }

    public string? AlphabeticalSortKey()
        => (IsRendered && !string.IsNullOrEmpty(PlayerName)
            ? (_folderConfig.Current.NickOverPlayerName ? GetNickAliasOrUid() : PlayerName)
            : GetNickAliasOrUid());

    public string GetDisplayName()
    {
        var condition = IsRendered && !_folderConfig.Current.NickOverPlayerName && !string.IsNullOrEmpty(PlayerName);
        return condition ? PlayerName : GetNickAliasOrUid();
    }

    public string? GetNickname() => _nicks.GetNicknameForUid(UserData.UID);
    public string GetNickAliasOrUid() => _nicks.TryGetNickname(UserData.UID, out var n) ? n : UserData.AliasOrUID;

    public IPCMoodleAccessTuple ToAccessTuple()
    {
        return new IPCMoodleAccessTuple(
            OwnPerms.MoodleAccess, (long)OwnPerms.MaxMoodleTime.TotalMilliseconds,
            PairPerms.MoodleAccess, (long)PairPerms.MaxMoodleTime.TotalMilliseconds);
    }

    public void SetMoodleData(MoodleData newData)
        => SharedData = newData;

    public async Task SetFullDataChanges(NewModUpdates newModData, VisualUpdate newIpc, bool isInitialData)
    {
        if (newIpc.PlayerChanges != null)
            await _player.UpdateAndApplyAlterations(newModData, newIpc.PlayerChanges, isInitialData);

        if (newIpc.MinionMountChanges != null)
            await _mountMinion.UpdateAndApplyIpc(newIpc.MinionMountChanges, isInitialData);

        if (newIpc.PetChanges != null)
            await _pet.UpdateAndApplyIpc(newIpc.PetChanges, isInitialData);

        if (newIpc.CompanionChanges != null)
            await _companion.UpdateAndApplyIpc(newIpc.CompanionChanges, isInitialData);
    }

    public async void SetModChanges(NewModUpdates newModData, string manipString)
        => await _player.UpdateAndApplyMods(newModData, manipString);

    public async void SetIpcChanges(VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null)
            await _player.UpdateAndApplyIpc(newIpc.PlayerChanges);
        
        if (newIpc.MinionMountChanges != null)
            await _mountMinion.UpdateAndApplyIpc(newIpc.MinionMountChanges, false);
        
        if (newIpc.PetChanges != null)
            await _pet.UpdateAndApplyIpc(newIpc.PetChanges, false);
        
        if (newIpc.CompanionChanges != null)
            await _companion.UpdateAndApplyIpc(newIpc.CompanionChanges, false);
    }

    public async void SetIpcChanges(OwnedObject obj, IpcKind kind, string newData)
    {
        await (obj switch
        {
            OwnedObject.Player => _player.UpdateAndApplyIpc(kind, newData),
            OwnedObject.MinionOrMount => _mountMinion.UpdateAndApplyIpc(kind, newData),
            OwnedObject.Pet => _pet.UpdateAndApplyIpc(kind, newData),
            OwnedObject.Companion => _companion.UpdateAndApplyIpc(kind, newData),
            _ => Task.CompletedTask,
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     After a Sundesmo is initialized / created, it will then be marked as
    ///     Online, if they are online. (Or after a reconnection, after being created) <para />
    /// 
    ///     Because of this, when a Sundesmo is marked online, abort any timeouts. <para />
    ///     
    ///     After a Sundesmo is marked online, check the sundesmos OwnedObjects for 
    ///     visibility, rendering them if applicable.
    /// </summary>
    public unsafe void MarkOnline(OnlineUser dto)
    {
        _onlineUser = dto;
        // If they are in any limbostate, abort it
        _limboManager.CancelLimbo(dto.User);
        // Inform mediator of Online update.
        Mediator.Publish(new SundesmoOnline(this));

        // If we were marked for reloading, turn it back to false.
        IsReloading = false;

        // Check each of the Sundesmo's potentially existing owned objects.
        // If any of them are rendered, we should mark them as visible and
        // bind the Objects to their data.
        SetVisibleIfRendered();
    }

    /// <summary>
    ///     Convert a temporary Sundesmo to a permanent one.
    /// </summary>
    public void MarkAsPermanent()
    {
        if (!UserPair.IsTemporary)
        {
            Logger.LogWarning($"Attempted to update a temporary sundesmo [{PlayerName}] ({GetNickAliasOrUid()}) to permanent, but they are already permanent.", LoggerType.PairManagement);
            return;
        }
        // Update the status to non-temporary.
        UserPair = UserPair with { TempAccepterUID = string.Empty };
        Logger.LogInformation($"Sundesmo [{PlayerName}] ({GetNickAliasOrUid()}) has been updated to permanent.", LoggerType.PairManagement);
    }

    /// <summary>
    ///     When logged out of unloading the plugin, data for a Sundesmo 
    ///     should unload without any limbo state. <para />
    ///     The next time they are marked as offline (which should happen 
    ///     after this call), revert, and then clear all data for the handler. <br /> 
    ///     <b>Don't dispose, so they show up as offline still.</b>
    /// </summary>
    public void MarkForUnload()
    {
        Logger.LogDebug($"Marking [{PlayerName}] ({GetNickAliasOrUid()}) as reloading.", LoggerType.PairManagement);
        IsReloading = true;
    }

    /// <summary>
    ///     Marks the sundesmo as offline, triggering the alteration data revert timeout. <para />
    ///     When this expires, all applied alterations will be reverted, regardless of visibility state.
    /// </summary>
    /// <param name="immidiateRevert"> Skips the limbo timeout and reverts instantly. </param>
    public async void MarkOffline(bool immidiateRevert = false)
    {
        _onlineUser = null;
        // Inform the TransferBarUI of their offline state so they're removed from the dictionaries.
        Mediator.Publish(new SundesmoOffline(this));

        // If the Sundesmo is marked for Reloading or an immidiate revert is forced,
        // revert the alterations and clear the data now.
        if (IsReloading || immidiateRevert)
        {
            await RevertRenderedAlterations().ConfigureAwait(false);
            // Clear if we are reloading only.
            if (IsReloading)
                await ClearAllAlterationData().ConfigureAwait(false);
        }
        // If they are rendered, we should place them into a timeout.
        else if (IsRendered)
        {
            EnterLimboState();
        }
        // Otherwise they were not rendered so we have no reason to keep any data.
        else
        {
            await ClearAllAlterationData().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Toss a rendered sundesmo into limbo state. <br/>
    ///     This should be called when the Sundesmo becomes unrenders, 
    ///     or when they go offline while still rendered.
    /// </summary>
    public bool EnterLimboState()
        => _limboManager.EnterLimbo(this, RevertRenderedAlterations);

    /// <summary>
    ///     If this sundesmo is currently in a limbo state, escape it.
    /// </summary>
    public bool ExitLimboState()
        => _limboManager.CancelLimbo(UserData);

    /// <summary>
    ///     Checks all Sundesmo Objects to see if they are rendered and/or reinitialize them.
    /// </summary>
    public async void SetVisibleIfRendered()
    {
        try
        {
            _redrawer.BeginUpdate();
            // Run all tasks in parallel.
            await Task.WhenAll(
                _player.SetVisibleIfRendered(),
                _mountMinion.SetVisibleIfRendered(),
                _pet.SetVisibleIfRendered(),
                _companion.SetVisibleIfRendered()
            ).ConfigureAwait(false);
        }
        finally
        {
            _redrawer.EndUpdate();
        }
    }

    /// <summary>
    ///     Reapply cached Alterations to all visible OwnedObjects.
    /// </summary>
    public async void ReapplyAlterations()
    {
        // Ignore if not rendered.
        if (!IsRendered)
            return;

        try
        {
            _redrawer.BeginUpdate();
            // Run all tasks in parallel.
            await Task.WhenAll(
                _player.ReapplyAlterations(),
                _mountMinion.ReapplyAlterations(),
                _pet.ReapplyAlterations(),
                _companion.ReapplyAlterations()
            ).ConfigureAwait(false);
        }
        finally
        {
            _redrawer.EndUpdate();
        }
    }

    /// <summary>
    ///     Revert the rendered alterations for all owned objects of the Sundesmo.
    /// </summary>
    public async Task RevertRenderedAlterations()
    {
        Logger.LogDebug($"Reverting alterations for [{PlayerName}] ({GetNickAliasOrUid()}).", UserData.AliasOrUID);
        await _player.RevertAlterations().ConfigureAwait(false);
        await _mountMinion.RevertAlterations().ConfigureAwait(false);
        await _pet.RevertAlterations().ConfigureAwait(false);
        await _companion.RevertAlterations().ConfigureAwait(false);
    }

    /// <summary>
    ///     Cleanup all alteration data for all owned objects of the Sundesmo.
    /// </summary>
    public async Task ClearAllAlterationData()
    {
        Logger.LogDebug($"Clearing alteration data for {PlayerName} ({GetNickAliasOrUid()}).", LoggerType.PairManagement);
        await _player.ClearAlterations().ConfigureAwait(false);
        await _mountMinion.ClearAlterations().ConfigureAwait(false);
        await _pet.ClearAlterations().ConfigureAwait(false);
        await _companion.ClearAlterations().ConfigureAwait(false);
    }

    /// <summary>
    ///     Disposes of the Sundesmo's Handlers, but not the Sundesmo itself.
    /// </summary>
    public void DisposeData()
    {
        Logger.LogDebug($"Disposing data for {PlayerName} ({GetNickAliasOrUid()})", LoggerType.PairManagement);
        // If online, just simply mark offline.
        if (IsOnline)
        {
            _onlineUser = null;
            // Inform the TransferBarUI of their offline state so they're removed from the dictionaries.
            Mediator.Publish(new SundesmoOffline(this));
        }

        // The handler disposal methods effective perform a revert + data clear + final disposal state.
        // Because of this calling mark offline prior is not necessary.
        _player.Dispose();
        _mountMinion.Dispose();
        _pet.Dispose();
        _companion.Dispose();
        _redrawer.Dispose();
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
