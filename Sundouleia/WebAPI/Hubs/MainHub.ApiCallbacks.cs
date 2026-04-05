using CkCommons;
using Dalamud.Interface.ImGuiNotification;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.VisualBasic.ApplicationServices;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;

namespace Sundouleia.WebAPI;

// This section of the MainHub focuses on responses received by the Server.
// We use this to perform actions to our client's data.
public partial class MainHub
{
    #region Message / Info Callbacks
    /// <summary>
    ///   Called when the server sends a message to the client.
    /// </summary>
    public Task Callback_ServerMessage(MessageSeverity messageSeverity, string message)
    {
        if (messageSeverity == MessageSeverity.Information && _suppressNextNotification)
        {
            _suppressNextNotification = false;
            return Task.CompletedTask;
        }

        var (title, type) = messageSeverity switch
        {
            MessageSeverity.Error => ($"Error from {ServerHubConfig.CurrentHubName}", NotificationType.Error),
            MessageSeverity.Warning => ($"Warning from {ServerHubConfig.CurrentHubName}", NotificationType.Warning),
            _ => ($"Info from {ServerHubConfig.CurrentHubName}", NotificationType.Info),
        };

        Mediator.Publish(new NotificationMessage(title, message, type, TimeSpan.FromSeconds(7.5)));
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Sometimes Corby just wants to do a little bullying.
    /// </summary>
    public Task Callback_HardReconnectMessage(MessageSeverity messageSeverity, string message, ServerState newServerState)
    {
        if (messageSeverity == MessageSeverity.Information && _suppressNextNotification)
            _suppressNextNotification = false;
        else
        {
            var (title, type, duration) = messageSeverity switch
            {
                MessageSeverity.Error => ($"Error from {ServerHubConfig.CurrentHubName}", NotificationType.Error, 7.5),
                MessageSeverity.Warning => ($"Warning from {ServerHubConfig.CurrentHubName}", NotificationType.Warning, 7.5),
                _ => ($"Info from {ServerHubConfig.CurrentHubName}", NotificationType.Info, 5.0),
            };
            Mediator.Publish(new NotificationMessage(title, message, type, TimeSpan.FromSeconds(duration)));
        }
        // we need to update the api server state to be stopped if connected
        if (IsConnected)
        {
            _ = Task.Run(async () =>
            {
                // pause the server state
                var prevState = _accounts.ConnectionKind;
                _accounts.ConnectionKind = ConnectionKind.FullPause;
                _suppressNextNotification = true;
                // If forcing a hard reconnect, fully unload the client & their sundesmos.
                await Disconnect(ServerState.Disconnected, DisconnectIntent.Reload).ConfigureAwait(false);
                // Clear our token cache between, incase we were banned.
                _tokenProvider.ResetTokenCache();
                // Revert full pause status and create a new connection.
                _accounts.ConnectionKind = prevState; // Can cause issues where it doesnt restore after... Maybe seperate intent?
                _accounts.Save();
                _suppressNextNotification = true;

                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                await Connect().ConfigureAwait(false);
            });
        }
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Moderation tool for radar chats.
    /// </summary>
    public Task Callback_RadarUserFlagged(string flaggedUserUid)
    {
        // Some logic in here that informs the radar service that
        // a user was flagged and to bomb them from the chat.
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Gets total online users.
    /// </summary>
    public Task Callback_ServerInfo(ServerInfoResponse serverInfo)
    {
        _serverInfo = serverInfo;
        return Task.CompletedTask;
    }
    #endregion Message / Info Callbacks

    #region Pair/Request Callbacks
    /// <summary>
    ///   To add a new pair (Sundesmo).
    /// </summary>
    public Task Callback_AddPair(UserPair dto)
    {
        Logger.LogDebug($"Cb_AddPair: {dto}", LoggerType.Callbacks);
        Generic.Safe(() =>
        {
            _sundesmos.AddSundesmo(dto);
            _radar.RefreshUser(dto.User);
        });
        return Task.CompletedTask;
    }

    /// <summary>
    ///   To remove a Sundesmo.
    /// </summary>
    public Task Callback_RemovePair(UserDto dto)
    {
        Logger.LogDebug($"Cb_RemovePair: {dto}", LoggerType.Callbacks);
        Generic.Safe(() =>
        {
            _sundesmos.RemoveSundesmo(dto);
            _radar.RefreshUser(dto.User);
        });
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Change a temporary sundesmo to a permanent one.
    /// </summary>
    public Task Callback_PersistPair(UserDto dto)
    {
        Logger.LogDebug($"Cb_UpdatePairToPermanent: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.UpdateToPermanent(dto));
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Occurs upon recieving a pair request from someone else.
    /// </summary>
    /// <remarks> This is not called in responce to your own sent requests. </remarks>
    public Task Callback_AddRequest(SundesmoRequest dto)
    {
        Logger.LogDebug($"Cb_AddPairRequest: {dto}", LoggerType.Callbacks);
        _requests.AddNewRequest(dto);
        _radar.RefreshUser(dto.User);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   When a pending request was rejected, or pending request was canceled.
    /// </summary>
    /// <remarks> This is not called in responce to your own sent requests. </remarks>
    public Task Callback_RemoveRequest(SundesmoRequest dto)
    {
        Logger.LogDebug($"Cb_RemoveRequest: {dto}", LoggerType.Callbacks);
        _requests.RemoveRequest(dto);
        _radar.RefreshUser(dto.User);
        return Task.CompletedTask;
    }
    #endregion Pair/Request Callbacks

    #region Moderation Callbacks
    // Remove? Or Modify later. Currently in limbo state.
    public Task Callback_Blocked(UserDto dto)
        => Task.CompletedTask;

    public Task Callback_Unblocked(UserDto dto)
        => Task.CompletedTask;
    #endregion Moderation Callbacks

    #region Loci Callbacks
    public Task Callback_PairLociDataUpdated(LociDataUpdate dto)
    {
        Logger.LogDebug($"Cb_PairLociDataUpdated: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveLociData(dto.User, dto.Data));
        return Task.CompletedTask;
    }

    public Task Callback_PairLociStatusesUpdate(LociStatusesUpdate dto)
    {
        Logger.LogDebug($"Cb_PairLociStatusesUpdate: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveLociStatuses(dto.User, dto.Statuses));
        return Task.CompletedTask;
    }

    public Task Callback_PairLociPresetsUpdate(LociPresetsUpdate dto)
    {
        Logger.LogDebug($"Cb_PairLociPresetsUpdate: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveLociPresets(dto.User, dto.Presets));
        return Task.CompletedTask;
    }

    public Task Callback_PairLociStatusModified(LociStatusModified dto)
    {
        Logger.LogDebug($"Cb_PairLociStatusModified: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveLociStatusUpdate(dto.User, dto.Status, dto.Deleted));
        return Task.CompletedTask;
    }

    public Task Callback_PairLociPresetModified(LociPresetModified dto)
    {
        Logger.LogDebug($"Cb_PairLociPresetModified: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveLociPresetUpdate(dto.User, dto.Preset, dto.Deleted));
        return Task.CompletedTask;
    }
    public async Task Callback_ApplyLociDataById(ApplyLociDataById dto)
    {
        Logger.LogDebug($"Cb_ApplyLociDataById: {dto.User.DisplayName}", LoggerType.Callbacks);
        // Fail if not a valid pair or not rendered.
        if (_sundesmos.GetUserOrDefault(dto.User) is not { } pair)
            Logger.LogWarning($"Received ApplyLociDataById for an unpaired user: {dto.User.DisplayName}");
        else if (!pair.IsRendered)
            Logger.LogWarning($"Received ApplyLociDataById for a sundesmo not rendered: {dto.User.DisplayName}");
        else
            await _ipc.Loci.ApplyStatus(dto.Ids.ToList()).ConfigureAwait(false);
    }

    public async Task Callback_ApplyLociStatus(ApplyLociStatus dto)
    {
        Logger.LogDebug($"Cb_ApplyLociStatus: {dto.User.DisplayName}", LoggerType.Callbacks);
        // Fail if not a valid pair.
        if (_sundesmos.GetUserOrDefault(dto.User) is not { } pair)
            Logger.LogWarning($"Received ApplyLociStatus for an unpaired user: {dto.User.DisplayName}");
        else if (!pair.IsRendered)
            Logger.LogWarning($"Received ApplyLociStatus for a sundesmo not rendered: {dto.User.DisplayName}" );
        else
        {
            Mediator.Publish(new EventMessage(new(pair.GetNickAliasOrUid(), pair.UserData.UID, DataEventType.LociDataApplied, "Applied by Pair.")));
            await _ipc.Loci.ApplyStatusInfo(dto.Statuses.Select(s => s.ToTuple()).ToList()).ConfigureAwait(false);
        }
    }

    public async Task Callback_RemoveLociData(RemoveLociData dto)
    {
        Logger.LogDebug($"Cb_RemoveLociData: {dto.User.DisplayName}", LoggerType.Callbacks);
        // Fail if not a valid pair or not rendered.
        if (_sundesmos.GetUserOrDefault(dto.User) is not { } pair)
            Logger.LogWarning($"Received RemoveLociData for an unpaired user: {dto.User.DisplayName}");
        else if (!pair.IsRendered)
            Logger.LogWarning($"Received RemoveLociData for a sundesmo not rendered: {dto.User.DisplayName}");
        else
            await _ipc.Loci.BombStatus(dto.Ids.ToList()).ConfigureAwait(false);
    }
    #endregion Locis Callbacks

    #region Data Update Callbacks
    /// <summary>
    ///     Updates both mods and other visual data. Should only be used on 
    ///     initial load, or during a full update. <para />
    ///     Mods will include what mods to add and what mods to remove from the existing collection.
    /// </summary>
    public Task Callback_IpcUpdateFull(IpcUpdateFull dto)
    {
        Logger.LogDebug($"Cb_IpcUpdateFull: {dto.User.DisplayName}", LoggerType.Callbacks);
        try
        {
            _sundesmos.ReceiveIpcUpdateFull(dto.User, dto.ModData, dto.IpcData, dto.IsInitialData);
        }
        catch (Bagagwa ex)
        {
            Logger.LogError($"Error in Callback_IpcUpdateFull for {dto.User.DisplayName}: {ex}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handle mod updates only, by adding / removing the respective mods 
    ///     from the existing collection.
    /// </summary>
    public Task Callback_IpcUpdateMods(IpcUpdateMods dto)
    {
        Logger.LogDebug($"Cb_IpcUpdateMods: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateMods(dto.User, dto.ModData, dto.ManipString));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Processes only non-mod updates to the player. <para />
    ///     Does not depend on file handle management to apply 
    ///     changes and can be performed more frequently.
    /// </summary>
    public Task Callback_IpcUpdateOther(IpcUpdateOther dto)
    {
        Logger.LogDebug($"Cb_IpcUpdateOther: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateOther(dto.User, dto.IpcData));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Perform an update on a single non-mod IPC change (heels, honorific ext.) <para />
    ///     Useful when only changing one thing and want to do near instantaneous updates.
    /// </summary>
    public Task Callback_IpcUpdateSingle(IpcUpdateSingle dto)
    {
        Logger.LogDebug($"Cb_IpcUpdateSingle: {dto.User.DisplayName}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateSingle(dto.User, dto.ObjType, dto.Type, dto.NewData));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo have updated a permission in their GlobalPerms.
    /// </summary>
    public Task Callback_ChangeGlobalPerm(ChangeGlobalPerm dto)
    {
        Logger.LogDebug($"Cb_ChangeGlobalPerm: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeGlobal(dto.User, dto.PermName, dto.NewValue));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo updated their GlobalPerms in bulk.
    /// </summary>
    public Task Callback_ChangeAllGlobal(ChangeAllGlobal dto)
    {
        Logger.LogDebug($"Cb_ChangeAllGlobal: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeGlobal(dto.User, dto.NewPerms));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo have updated a permission in their PairPerms. <para />
    ///     Only ever called when a sundesmo changes their own permissions for our client. <para />
    ///     <b> THIS IS NOT CALLED WHEN WE CHANGE A PAIRPERM FOR A SUNDESMO. </b>
    /// </summary>
    public Task Callback_ChangeUniquePerm(ChangeUniquePerm dto)
    {
        Logger.LogDebug($"Cb_ChangeUniquePerm: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeUnique(dto.User, dto.PermName, dto.NewValue));
        return Task.CompletedTask;
    }

    public Task Callback_ChangeUniquePerms(ChangeUniquePerms dto)
    {
        Logger.LogDebug($"Cb_ChangeUniquePerms: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeUnique(dto.User, dto.Changes));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo updated their PairPerms in bulk.
    /// </summary>
    public Task Callback_ChangeAllUnique(ChangeAllUnique dto)
    {
        Logger.LogDebug($"Cb_ChangeAllUnique: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeUnique(dto.User, dto.NewPerms));
        return Task.CompletedTask;
    }
    #endregion Data Update Callbacks

    #region Chat and Radar Callbacks
    /// <summary>
    ///   Aquire a validated message processed by the server for this RadarChat instance.
    /// </summary>
    public Task Callback_RadarChatMessage(LoggedRadarChatMessage dto)
    {
        // Do something here for appending it to the RadarChat Instance.
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Lets us know when a chat user updated their permissions. <para />
    ///   Could also be used for joining the chat, but there isnt anything doing that currently (yet™).
    /// </summary>
    public Task Callback_RadarChatAddUpdateUser(RadarChatMember dto)
    {
        Logger.LogDebug($"Cb_RadarChatAddUpdateUser Called for: {dto.User.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Called when a user joins the Radar location we are currently in. (Or when permissions update)
    /// </summary>
    /// <remarks> Ensure the ident of the stored actor reflects the current ident passed by this call. </remarks>
    public Task Callback_RadarAddUpdateUser(RadarMember dto)
    {
        Logger.LogDebug($"Cb_RadarAddUpdateUser Called for: {dto.User.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Called when another user leaves the Radar instance we are present in.
    /// </summary>
    public Task Callback_RadarRemoveUser(UserDto dto)
    {
        Logger.LogDebug($"Cb_RadarUserFlagged Called for: {dto.User.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Each World-Territory has an associated RadarGroup (not SanctionedGroup). <br/>
    ///   This informs us when someone was added to the group, or updated their permissions in it.
    /// </summary>
    public Task Callback_RadarGroupAddUpdateUser(RadarGroupMember dto)
    {
        Logger.LogDebug($"Cb_RadarGroupAddUpdateUser Called for: {dto.User.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Each World-Territory has an associated RadarGroup (not SanctionedGroup). <br/>
    ///   This informs us when someone left the RadarGroup. <para />
    /// </summary>
    public Task Callback_RadarGroupRemoveUser(UserDto dto)
    {
        Logger.LogDebug($"Cb_RadarGroupRemoveUser Called for: {dto.User.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }

    /// <summary>
    ///   Sent from another chat participant in any active Non-RadarChat you are in. <para/>
    ///   This includes chats attached to groups you own / are a part of, or SanctionedGroup chats. 
    /// </summary>
    public Task Callback_ChatMsgReceived(ReceivedChatMessage dto)
    {
        Logger.LogDebug($"Cb_ChatMessageReceived Called for: {dto.Sender.AnonName}", LoggerType.Callbacks);
        return Task.CompletedTask;
    }
    #endregion Chat and Radar Callbacks

    #region Status Update Callbacks
    public Task Callback_UserIsUnloading(UserDto dto)
    {
        Logger.LogDebug($"Cb_UserIsUnloading: [{dto.User.DisplayName}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoForUnload(dto.User));
        return Task.CompletedTask;
    }


    /// <summary>
    ///     Whenever one of our Sundesmo disconnects from Sundouleia.
    /// </summary>
    public Task Callback_UserOffline(UserDto dto)
    {
        Logger.LogDebug($"Cb_UserOffline: [{dto.User.DisplayName}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoOffline(dto.User)) ;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo connects to Sundouleia.
    /// </summary>
    public Task Callback_UserOnline(OnlineUser dto)
    {
        Logger.LogDebug($"Cb_UserOnline: [{dto.User.DisplayName}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoOnline(dto));
        return Task.CompletedTask;
    }

    public Task Callback_UserVanityUpdate(UserDto dto)
    {
        Logger.LogDebug($"Cb_UserVanityUpdate: [{dto.User.DisplayName}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.UpdateVanityData(dto.User));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Another user, paired or not, has a profile update. <para />
    ///     If we received this we can assume that we have at 
    ///     some point loaded this profile.
    /// </summary>
    public Task Callback_ProfileUpdated(UserDto dto)
    {
        Logger.LogDebug($"Cb_ProfileUpdated: [{dto.User.DisplayName}]", LoggerType.Callbacks);
        Mediator.Publish(new ClearProfileDataMessage(dto.User));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     When verifying your account via the discord bot, this will pass in
    ///     the verification code that should display in game for you to respond to 
    ///     the bot with.
    /// </summary>
    public Task Callback_ShowVerification(VerificationCode dto)
    {
        Logger.LogDebug("Callback_ShowVerification", LoggerType.Callbacks);
        Mediator.Publish(new VerificationPopupMessage(dto));
        return Task.CompletedTask;
    }
    #endregion Status Update Callbacks

    /* --------------------------------- void methods from the API to call the hooks --------------------------------- */
    public void OnServerMessage(Action<MessageSeverity, string> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ServerMessage), act);
    }

    public void OnHardReconnectMessage(Action<MessageSeverity, string, ServerState> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_HardReconnectMessage), act);
    }

    public void OnRadarUserFlagged(Action<string> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarUserFlagged), act);
    }

    public void OnServerInfo(Action<ServerInfoResponse> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ServerInfo), act);
    }

    public void OnAddPair(Action<UserPair> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_AddPair), act);
    }

    public void OnRemovePair(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RemovePair), act);
    }

    public void OnPersistPair(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PersistPair), act);
    }

    public void OnAddRequest(Action<SundesmoRequest> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_AddRequest), act);
    }

    public void OnRemoveRequest(Action<SundesmoRequest> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RemoveRequest), act);
    }

    public void OnBlocked(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_Blocked), act);
    }

    public void OnUnblocked(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_Unblocked), act);
    }

    public void OnPairLociDataUpdated(Action<LociDataUpdate> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PairLociDataUpdated), act);
    }

    public void OnPairLociStatusesUpdate(Action<LociStatusesUpdate> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PairLociStatusesUpdate), act);
    }

    public void OnPairLociPresetsUpdate(Action<LociPresetsUpdate> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PairLociPresetsUpdate), act);
    }

    public void OnPairLociStatusModified(Action<LociStatusModified> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PairLociStatusModified), act);
    }

    public void OnPairLociPresetModified(Action<LociPresetModified> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_PairLociPresetModified), act);
    }

    public void OnApplyLociDataById(Action<ApplyLociDataById> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ApplyLociDataById), act);
    }

    public void OnApplyLociStatus(Action<ApplyLociStatus> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ApplyLociStatus), act);
    }

    public void OnRemoveLociData(Action<RemoveLociData> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RemoveLociData), act);
    }

    public void OnIpcUpdateFull(Action<IpcUpdateFull> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_IpcUpdateFull), act);
    }

    public void OnIpcUpdateMods(Action<IpcUpdateMods> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_IpcUpdateMods), act);
    }

    public void OnIpcUpdateOther(Action<IpcUpdateOther> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_IpcUpdateOther), act);
    }

    public void OnIpcUpdateSingle(Action<IpcUpdateSingle> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_IpcUpdateSingle), act);
    }

    public void OnSingleChangeGlobal(Action<ChangeGlobalPerm> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChangeGlobalPerm), act);
    }

    public void OnBulkChangeGlobal(Action<ChangeAllGlobal> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChangeAllGlobal), act);
    }

    public void OnChangeUniquePerm(Action<ChangeUniquePerm> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChangeUniquePerm), act);
    }

    public void OnChangeUniquePerms(Action<ChangeUniquePerms> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChangeUniquePerms), act);
    }

    public void OnChangeAllUnique(Action<ChangeAllUnique> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChangeAllUnique), act);
    }

    public void OnRadarChatMessage(Action<LoggedRadarChatMessage> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarChatMessage), act);
    }

    public void OnRadarChatAddUpdateUser(Action<RadarChatMember> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarChatAddUpdateUser), act);
    }

    public void OnRadarAddUpdateUser(Action<RadarMember> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarAddUpdateUser), act);
    }

    public void OnRadarRemoveUser(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarRemoveUser), act);
    }

    public void OnRadarGroupAddUpdateUser(Action<RadarGroupMember> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarGroupAddUpdateUser), act);
    }

    public void OnRadarGroupRemoveUser(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarGroupRemoveUser), act);
    }

    public void OnChatMsgReceived(Action<ReceivedChatMessage> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ChatMsgReceived), act);
    }

    public void OnUserIsUnloading(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_UserIsUnloading), act);
    }

    public void OnUserOffline(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_UserOffline), act);
    }

    public void OnUserOnline(Action<OnlineUser> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_UserOnline), act);
    }

    public void OnUserVanityUpdate(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_UserVanityUpdate), act);
    }

    public void OnProfileUpdated(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ProfileUpdated), act);
    }

    public void OnShowVerification(Action<VerificationCode> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_ShowVerification), act);
    }
}
