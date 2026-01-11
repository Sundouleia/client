using CkCommons;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.Services;

/// <summary>
///     Distributes RadarChanges.
/// </summary>
public class RadarDistributor : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly RadarManager _manager;
    private readonly CharaObjectWatcher _watcher;

    public RadarDistributor(ILogger<RadarDistributor> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, RadarManager manager, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _manager = manager;
        _watcher = watcher;

        Mediator.Subscribe<RadarConfigChanged>(this, _ => OnConfigChanged(_.OptionName));
        Mediator.Subscribe<ConnectedMessage>(this, async _ =>
        {
            if (_config.Current.RadarEnabled && Svc.ClientState.IsLoggedIn)
            {
                await SundouleiaEx.WaitForPlayerLoading();
                await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
            }
        });
        Mediator.Subscribe<DisconnectedMessage>(this, _ => _manager.ClearUsers());
        Mediator.Subscribe<TerritoryChanged>(this, _ => OnTerritoryChanged(_.PrevTerritory, _.NewTerritory));

        // Listen for logout event to properly disconnect.
        Svc.ClientState.Logout += OnLogout;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.Logout -= OnLogout;
    }


    private async void OnLogout(int type, int code)
    {
        if (!_config.Current.RadarEnabled)
            return;
        Logger.LogInformation("User logged out, leaving radar zone and clearing users.", LoggerType.RadarData);
        await Generic.Safe(_hub.RadarZoneLeave).ConfigureAwait(false);
        _manager.ClearUsers();
    }

    private RadarZoneUpdate GetZoneUpdate()
    {
        var world = PlayerData.CurrentWorldId;
        var zone = LocationSvc.Current.TerritoryId;
        var joinChats = _config.Current.RadarJoinChats;
        var hashedCID = _config.Current.RadarSendPings ? SundouleiaSecurity.GetClientIdentHashThreadSafe() : string.Empty;
        return new(world, zone, joinChats, hashedCID);
    }

    private async void OnTerritoryChanged(ushort prevTerritory, ushort newTerritory)
    {       
        // If we do not want to send radar updates, then dont.
        if (!_config.Current.RadarEnabled)
            return;

        Logger.LogInformation($"Territory changed from {prevTerritory} to {newTerritory}", LoggerType.RadarData);      
        // Leave the current radar zone, notifying all users of the disconnect.
        await _hub.RadarZoneLeave().ConfigureAwait(false);
        // Clear all current radar users from the manager.
        _manager.ClearUsers();
        // await for us to finish loading (not entirely necessary but nice to have)
        await SundouleiaEx.WaitForPlayerLoading();
        // Join the new zone and retrieve the zone information.
        await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
    }

    public async Task JoinZoneAndAssignUsers(RadarZoneUpdate info)
    {
        try
        {
            var zoneInfo = await _hub.RadarZoneJoin(info).ConfigureAwait(false);
            if (zoneInfo.ErrorCode is not SundouleiaApiEc.Success || zoneInfo.Value is null)
            {
                Logger.LogWarning($"Failed to join radar zone {LocationSvc.Current.TerritoryId} [{zoneInfo.ErrorCode}] [Users Null?: {zoneInfo.Value is null}]");
                return;
            }

            GameDataSvc.WorldData.TryGetValue(info.WorldId, out var worldName);
            var territoryName = PlayerContent.TerritoryName;
            Logger.LogInformation($"RadarZone [{worldName} | {territoryName} ({info.TerritoryId})] joined. There are {zoneInfo.Value.CurrentUsers.Count} other Sundesmos.", LoggerType.RadarData);
            foreach (var radarUser in zoneInfo.Value.CurrentUsers.ToList())
                _manager.AddOrUpdateUser(radarUser, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception occurred while joining radar zone.");
        }
    }

    // Config options related to radar state changed. Send update to server.
    private async void OnConfigChanged(string changedOption)
    {
        if (!Svc.ClientState.IsLoggedIn) return;

        switch (changedOption)
        {
            case nameof(ConfigStorage.RadarEnabled):
                if (_config.Current.RadarEnabled)
                {
                    Logger.LogDebug($"Radar enabled, joining current radar zone.", LoggerType.RadarData);
                    await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
                }
                else
                {
                    Logger.LogDebug("Radar disabled, leaving current radar zone and clearing users.", LoggerType.RadarData);
                    await _hub.RadarZoneLeave().ConfigureAwait(false);
                    _manager.ClearUsers();
                }
                return;

            case nameof(ConfigStorage.RadarJoinChats):
            case nameof(ConfigStorage.RadarSendPings):
                // Collect the radar state, and send the update to the server.
                Logger.LogDebug("Config changed, sending radar update to server.", LoggerType.RadarData);
                var joinChats = _config.Current.RadarJoinChats;
                var hashedIdent = _config.Current.RadarSendPings ? SundouleiaSecurity.GetClientIdentHashThreadSafe() : string.Empty;
                await _hub.RadarUpdateState(new(joinChats, hashedIdent)).ConfigureAwait(false);
                return;
        }
    }
}