using CkCommons;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Radar.Chat;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.Services;

/// <summary>
///   Distributes logic related to PublicRadar, RadarGroup, and RadarChat. <para />
///   Updates on location change, can also be used to invoke manual updates.
/// </summary>
public class RadarDistributor : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly ChatConfig _chatConfig;
    private readonly RadarChatLog _radarChat;
    private readonly RadarManager _manager;
    private readonly CharaWatcher _watcher;

    public RadarDistributor(ILogger<RadarDistributor> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, ChatConfig chatConfig, RadarChatLog chatlog,
        RadarManager manager, CharaWatcher watcher)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _chatConfig = chatConfig;
        _radarChat = chatlog;
        _manager = manager;
        _watcher = watcher;

        Mediator.Subscribe<RadarConfigChanged>(this, _ => OnConfigChanged(_.OptionName));
        Mediator.Subscribe<ConnectedMessage>(this, _ => WaitAndUpdateRadarData());
        Mediator.Subscribe<TerritoryChanged>(this, _ => UpdateRadarData(_.PrevTerritory, _.NewTerritory));
    }

    private async void WaitAndUpdateRadarData()
    {
        if (!Svc.ClientState.IsLoggedIn)
            return;
        // Wait for the player to load in, then update the radar data.
        await SundouleiaEx.WaitForPlayerLoading();
        UpdateRadarData(0, PlayerContent.TerritoryID);
    }

    private async void UpdateRadarData(ushort prevTerritory, ushort newTerritory)
    {
        // Ignore if nothing enabled.
        if (!_config.Current.Radar && !_config.Current.RadarGroup && !_chatConfig.Current.RadarChat)
            return;

        if (!MainHub.IsConnectionDataSynced)
            return;

        try
        {
            var locMeta = LocationSvc.GetLocationMeta();
            var doChat = _chatConfig.Current.RadarChat;
            var doPublic = _config.Current.Radar;
            var doGroup = _config.Current.RadarGroup;
            // Otherwise, compile the DTO to send.
            var zoneDto = new LocationUpdate(MainHub.OwnUserData, locMeta, doChat, doPublic, doGroup);
            if (doChat) zoneDto.ChatFlags = _chatConfig.Current.ChatFlags;
            if (doPublic) zoneDto.PublicFlags = _config.Current.RadarPerms;
            if (doGroup) zoneDto.GroupFlags = _config.Current.RadarGroupPerms;

            // Invoke the update to the server.
            var updateResult = await _hub.UpdateLocation(zoneDto).ConfigureAwait(false);
            if (updateResult.ErrorCode is not SundouleiaApiEc.Success)
                Logger.LogWarning($"Failed to update radar location on territory change from {prevTerritory} to {newTerritory} [{updateResult.ErrorCode}].");
            else
                Logger.LogInformation($"Updated radar location on territory change from {prevTerritory} to {newTerritory}. Chat: {doChat} | Public: {doPublic} | Group: {doGroup}", LoggerType.RadarData);
            
            // Handle the updates based on what we got.

            // Chat
            _radarChat.CreateOrReinitChatlog(updateResult.Value!.ChatHistory);

            // Public Radar
            _manager.CreateOrReinitUsers(updateResult.Value!.RadarUsers);

            // Group Radar
            // _manager.UpdateGroupUsers(updateResult.Value!.RadarGroupUsers);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Exception while updating radar data on territory change from {prevTerritory} to {newTerritory}.");
        }
    }

    public async Task UpdatePublicPermissions()
    {

    }

    public async Task UpdateGroupPermissions()
    {

    }

    public async Task UpdateChatPermissions()
    {
        // Would need to invoke the resulting change if so.
    }

    // Leaves the group as well
    public async Task LeaveRadar()
    {

    }

    public async Task LeaveRadarGroup()
    {

    }

    public async Task LeaveChat()
    {

    }

    // Config options related to radar state changed. Send update to server.
    private async void OnConfigChanged(string changedOption)
    {
        //if (!Svc.ClientState.IsLoggedIn) return;

        //switch (changedOption)
        //{
        //    case nameof(ConfigStorage.RadarEnabled):
        //        if (_config.Current.RadarEnabled)
        //        {
        //            Logger.LogDebug($"Radar enabled, joining current radar zone.", LoggerType.RadarData);
        //            await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
        //        }
        //        else
        //        {
        //            Logger.LogDebug("Radar disabled, leaving current radar zone and clearing users.", LoggerType.RadarData);
        //            await _hub.RadarZoneLeave().ConfigureAwait(false);
        //            _manager.ClearUsers();
        //        }
        //        return;

        //    case nameof(ConfigStorage.RadarJoinChats):
        //    case nameof(ConfigStorage.RadarSendPings):
        //        // Collect the radar state, and send the update to the server.
        //        Logger.LogDebug("Config changed, sending radar update to server.", LoggerType.RadarData);
        //        var joinChats = _config.Current.RadarJoinChats;
        //        var hashedIdent = _config.Current.RadarSendPings ? SundouleiaSecurity.GetClientIdentHashThreadSafe() : string.Empty;
        //        await _hub.RadarUpdateState(new(joinChats, hashedIdent)).ConfigureAwait(false);
        //        return;
        //}
    }
}