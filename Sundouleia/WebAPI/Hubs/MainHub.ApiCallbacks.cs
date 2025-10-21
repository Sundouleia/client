using CkCommons;
using Dalamud.Interface.ImGuiNotification;
using Microsoft.AspNetCore.SignalR.Client;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using SundouleiaAPI.Network;

namespace Sundouleia.WebAPI;

// This section of the MainHub focuses on responses received by the Server.
// We use this to perform actions to our client's data.
public partial class MainHub
{
    #region Message / Info Callbacks
    /// <summary>
    ///     Called when the server sends a message to the client.
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
            MessageSeverity.Error => ($"Error from {MAIN_SERVER_NAME}", NotificationType.Error),
            MessageSeverity.Warning => ($"Warning from {MAIN_SERVER_NAME}", NotificationType.Warning),
            _ => ($"Info from {MAIN_SERVER_NAME}", NotificationType.Info),
        };

        Mediator.Publish(new NotificationMessage(title, message, type, TimeSpan.FromSeconds(7.5)));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Sometimes Corby just wants to do a little bullying.
    /// </summary>
    public Task Callback_HardReconnectMessage(MessageSeverity messageSeverity, string message, ServerState newServerState)
    {
        if (messageSeverity == MessageSeverity.Information && _suppressNextNotification)
            _suppressNextNotification = false;
        else
        {
            var (title, type, duration) = messageSeverity switch
            {
                MessageSeverity.Error => ($"Error from {MAIN_SERVER_NAME}", NotificationType.Error, 7.5),
                MessageSeverity.Warning => ($"Warning from {MAIN_SERVER_NAME}", NotificationType.Warning, 7.5),
                _ => ($"Info from {MAIN_SERVER_NAME}", NotificationType.Info, 5.0),
            };
            Mediator.Publish(new NotificationMessage(title, message, type, TimeSpan.FromSeconds(duration)));
        }
        // we need to update the api server state to be stopped if connected
        if (IsConnected)
        {
            _ = Task.Run(async () =>
            {
                // pause the server state
                _serverConfigs.AccountStorage.FullPause = true;
                _serverConfigs.Save();
                _suppressNextNotification = true;
                // If forcing a hard reconnect, fully unload the client & their sundesmos.
                await Disconnect(ServerState.Disconnected, true, true).ConfigureAwait(false);
                // Clear our token cache between, incase we were banned.
                _tokenProvider.ResetTokenCache();
                // Revert full pause status and create a new connection.
                _serverConfigs.AccountStorage.FullPause = false;
                _serverConfigs.Save();
                _suppressNextNotification = true;

                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                await Connect().ConfigureAwait(false);
            });
        }
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Moderation tool for radar chats.
    /// </summary>
    public Task Callback_RadarUserFlagged(string flaggedUserUid)
    {
        // Some logic in here that informs the radar service that
        // a user was flagged and to bomb them from the chat.
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Gets total online users.
    /// </summary>
    public Task Callback_ServerInfo(ServerInfoResponse serverInfo)
    {
        _serverInfo = serverInfo;
        return Task.CompletedTask;
    }
    #endregion Message / Info Callbacks

    #region Pair/Request Callbacks
    /// <summary>
    ///     To add a new pair (Sundesmo).
    /// </summary>
    public Task Callback_AddPair(UserPair dto)
    {
        Logger.LogDebug($"Callback_AddPair: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.AddSundesmo(dto));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     To remove a Sundesmo.
    /// </summary>
    public Task Callback_RemovePair(UserDto dto)
    {
        Logger.LogDebug($"Callback_RemovePair: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.RemoveSundesmo(dto));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     That there is a new pending request to add to the requests manager. <para />
    ///     Note that request sent by ourselves is not returned here, this callback
    ///     is only for requests sent by others. We can safely assume all requests 
    ///     have us as the target.
    /// </summary>
    public Task Callback_AddRequest(SundesmoRequest dto)
    {
        Logger.LogDebug($"Callback_AddPairRequest: {dto}", LoggerType.Callbacks);
        _requests.AddRequest(dto);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Sent by server when another user canceled or rejected 
    ///     a pending request you have sent. 
    /// </summary>
    public Task Callback_RemoveRequest(SundesmoRequest dto)
    {
        Logger.LogDebug($"Callback_RemoveRequest: {dto}", LoggerType.Callbacks);
        _requests.RemoveRequest(dto);
        return Task.CompletedTask;
    }
    #endregion Pair/Request Callbacks

    #region Moderation Callbacks
    public Task Callback_Blocked(UserDto dto)
    {
        Logger.LogDebug($"Callback_Blocked: {dto.User.AliasOrUID} has blocked you.", LoggerType.Callbacks);
        // Do something here.
        return Task.CompletedTask;
    }

    public Task Callback_Unblocked(UserDto dto)
    {
        Logger.LogDebug($"Callback_Unblocked: {dto.User.AliasOrUID} has unblocked you.", LoggerType.Callbacks);
        // Do something here.
        return Task.CompletedTask;
    }
    #endregion Moderation Callbacks

    #region Data Update Callbacks
    /// <summary>
    ///     Updates both mods and other visual data. Should only be used on 
    ///     initial load, or during a full update. <para />
    ///     Mods will include what mods to add and what mods to remove from the existing collection.
    /// </summary>
    public Task Callback_IpcUpdateFull(IpcUpdateFull dto)
    {
        Logger.LogDebug($"Callback_IpcUpdateFull: {dto.User.AliasOrUID}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateFull(dto.User, dto.ModData, dto.IpcData));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handle mod updates only, by adding / removing the respective mods 
    ///     from the existing collection.
    /// </summary>
    public Task Callback_IpcUpdateMods(IpcUpdateMods dto)
    {
        Logger.LogDebug($"Callback_IpcUpdateMods: {dto.User.AliasOrUID}", LoggerType.Callbacks);
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
        Logger.LogDebug($"Callback_IpcUpdateOther: {dto.User.AliasOrUID}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateOther(dto.User, dto.IpcData));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Perform an update on a single non-mod IPC change (heels, honorific ext.) <para />
    ///     Useful when only changing one thing and want to do near instantaneous updates.
    /// </summary>
    public Task Callback_IpcUpdateSingle(IpcUpdateSingle dto)
    {
        Logger.LogDebug($"Callback_IpcUpdateSingle: {dto.User.AliasOrUID}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.ReceiveIpcUpdateSingle(dto.User, dto.ObjType, dto.Type, dto.NewData));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo have updated a permission in their GlobalPerms.
    /// </summary>
    public Task Callback_SingleChangeGlobal(SingleChangeGlobal dto)
    {
        Logger.LogDebug($"Callback_SingleChangeGlobal: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeGlobal(dto.User, dto.PermName, dto.NewValue));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo updated their GlobalPerms in bulk.
    /// </summary>
    public Task Callback_BulkChangeGlobal(BulkChangeGlobal dto)
    {
        Logger.LogDebug($"Callback_BulkChangeGlobal: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeGlobal(dto.User, dto.NewPerms));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo have updated a permission in their PairPerms. <para />
    ///     Only ever called when a sundesmo changes their own permissions for our client. <para />
    ///     <b> THIS IS NOT CALLED WHEN WE CHANGE A PAIRPERM FOR A SUNDESMO. </b>
    /// </summary>
    public Task Callback_SingleChangeUnique(SingleChangeUnique dto)
    {
        Logger.LogDebug($"Callback_SingleChangeUnique: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermChangeUniqueOther(dto.User, dto.PermName, dto.NewValue));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo updated their PairPerms in bulk.
    /// </summary>
    public Task Callback_BulkChangeUnique(BulkChangeUnique dto)
    {
        Logger.LogDebug($"Callback_BulkChangeUnique: {dto}", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.PermBulkChangeUnique(dto.User, dto.NewPerms));
        return Task.CompletedTask;
    }
    #endregion Data Update Callbacks

    #region Radar Callbacks
    /// <summary>
    ///     Whenever another Sundouleia User has entered our current radar zone. <para />
    ///     Can also fire upon them updating their user info, such as if they
    ///     are sharing their ident for requests or not if already present.
    /// </summary>
    public Task Callback_RadarAddUpdateUser(OnlineUser dto)
    {
        Logger.LogDebug($"Callback_RadarAddUpdateUser Called", LoggerType.Callbacks);
        Mediator.Publish(new RadarAddOrUpdateUser(dto));
        return Task.CompletedTask;
    }

    public Task Callback_RadarRemoveUser(UserDto dto)
    {
        Logger.LogDebug($"Callback_RadarRemoveUser Called", LoggerType.Callbacks);
        Mediator.Publish(new RadarRemoveUser(dto.User));
        return Task.CompletedTask;
    }

    public Task Callback_RadarChat(RadarChatMessage dto)
    {
        // If for some ungodly reason we get this message from a different world / territory, ignore it.
        if (dto.WorldId != RadarService.CurrWorld || dto.TerritoryId != RadarService.CurrZone)
        {
            Logger.LogWarning($"Callback_RadarChat: Ignoring message from different world / zone. {dto.WorldId}/{dto.TerritoryId}", LoggerType.Callbacks);
            return Task.CompletedTask;
        }
        Logger.LogDebug($"Callback_RadarChat Message Received", LoggerType.Callbacks);
        Mediator.Publish(new NewRadarChatMessage(dto, dto.Sender == OwnUserData));
        return Task.CompletedTask;
    }
    #endregion Radar Callbacks

    #region Status Update Callbacks
    public Task Callback_UserIsUnloading(UserDto dto)
    {
        Logger.LogDebug($"Callback_UserIsUnloading: [{dto.User.AliasOrUID}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoForUnload(dto.User));
        return Task.CompletedTask;
    }


    /// <summary>
    ///     Whenever one of our Sundesmo disconnects from Sundouleia.
    /// </summary>
    public Task Callback_UserOffline(UserDto dto)
    {
        Logger.LogDebug($"Callback_UserOffline: [{dto.User.AliasOrUID}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoOffline(dto.User)) ;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Whenever one of our Sundesmo connects to Sundouleia.
    /// </summary>
    public Task Callback_UserOnline(OnlineUser dto)
    {
        Logger.LogDebug($"Callback_UserOnline: [{dto.User.AliasOrUID}]", LoggerType.Callbacks);
        Generic.Safe(() => _sundesmos.MarkSundesmoOnline(dto));
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Another user, paired or not, has a profile update. <para />
    ///     If we received this we can assume that we have at 
    ///     some point loaded this profile.
    /// </summary>
    public Task Callback_ProfileUpdated(UserDto dto)
    {
        Logger.LogDebug($"Callback_ProfileUpdated: [{dto.User.AliasOrUID}]", LoggerType.Callbacks);
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

    public void OnSingleChangeGlobal(Action<SingleChangeGlobal> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_SingleChangeGlobal), act);
    }

    public void OnBulkChangeGlobal(Action<BulkChangeGlobal> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_BulkChangeGlobal), act);
    }

    public void OnSingleChangeUnique(Action<SingleChangeUnique> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_SingleChangeUnique), act);
    }

    public void OnBulkChangeUnique(Action<BulkChangeUnique> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_BulkChangeUnique), act);
    }

    public void OnRadarAddUpdateUser(Action<OnlineUser> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarAddUpdateUser), act);
    }

    public void OnRadarRemoveUser(Action<UserDto> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarRemoveUser), act);
    }

    public void OnRadarChat(Action<RadarChatMessage> act)
    {
        if (_apiHooksInitialized) return;
        _hubConnection!.On(nameof(Callback_RadarChat), act);
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
