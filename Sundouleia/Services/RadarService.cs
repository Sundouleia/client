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
///     Resolvers for current radar users and radar zone changing.
/// </summary>
public class RadarService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly RadarManager _manager;
    private readonly CharaObjectWatcher _watcher;

    public RadarService(ILogger<RadarService> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, RadarManager manager, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _manager = manager;
        _watcher = watcher;

        Mediator.Subscribe<WatchedObjectCreated>(this, _ => OnObjectCreated(_.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, _ => OnObjectDeleted(_.Address));
        Mediator.Subscribe<RadarConfigChanged>(this, _ => OnConfigChanged(_.OptionName));
        Mediator.Subscribe<ConnectedMessage>(this, async _ =>
        {
            if (_config.Current.RadarEnabled && Svc.ClientState.IsLoggedIn)
            {
                await SundouleiaEx.WaitForPlayerLoading();
                await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
            }
        });

        // Listen to zone changes.
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        Svc.ClientState.Login += OnLogin;
        Svc.ClientState.Logout += OnLogout;

        if (Svc.ClientState.IsLoggedIn)
            OnLogin();
    }

    public static ushort CurrWorld { get; private set; } = 0;
    public static string CurrWorldName { get; private set; } = string.Empty;
    public static ushort CurrZone { get; private set; } = 0;
    public static string CurrZoneName => PlayerContent.GetTerritoryName(CurrZone);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.Logout -= OnLogout;
    }

    private async void OnLogin()
    {
        await SundouleiaEx.WaitForPlayerLoading();
        CurrWorld = PlayerData.CurrentWorldIdInstanced;
        CurrWorldName = PlayerData.CurrentWorldInstanced;
        CurrZone = PlayerContent.TerritoryIdInstanced;
        Mediator.Publish(new RadarTerritoryChanged(0, CurrZone));
    }

    private async void OnLogout(int type, int code)
    {
        CurrWorld = 0;
        CurrWorldName = string.Empty;
        CurrZone = 0;

        if (!_config.Current.RadarEnabled)
            return;
        Logger.LogInformation("User logged out, leaving radar zone and clearing users.", LoggerType.RadarData);
        await Generic.Safe(_hub.RadarZoneLeave).ConfigureAwait(false);
        _manager.ClearUsers();
    }

    private RadarZoneUpdate GetZoneUpdate()
    {
        var world = PlayerData.CurrentWorldIdInstanced;
        var zone = CurrZone;
        var joinChats = _config.Current.RadarJoinChats;
        var hashedCID = _config.Current.RadarSendPings ? SundouleiaSecurity.GetClientIdentHashThreadSafe() : string.Empty;
        return new(world, zone, joinChats, hashedCID);
    }

    private async void OnTerritoryChanged(ushort newTerritory)
    {
        // Ignore territories from login zone / title screen (if any even exist)
        if (!Svc.ClientState.IsLoggedIn)
            return;

        Mediator.Publish(new RadarTerritoryChanged(CurrZone, newTerritory));
        
        // If we do not want to send radar updates, then dont.
        if (!_config.Current.RadarEnabled)
        {
            CurrZone = newTerritory;
            return;
        }

        Logger.LogInformation($"Territory changed from {CurrZone} to {newTerritory}", LoggerType.RadarData);
        CurrZone = newTerritory;
        
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
                Logger.LogWarning($"Failed to join radar zone {CurrZone} [{zoneInfo.ErrorCode}] [Users Null?: {zoneInfo.Value is null}]");
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

    /// <summary>
    ///     Whenever a new object is rendered. Should check against the list of current radar users.
    /// </summary>
    private unsafe void OnObjectCreated(IntPtr address)
    {
        // Obtain the list of all users except the valid ones to get the invalid ones.
        var invalidUsers = _manager.RadarUsers.Where(u => (!u.IsValid && u.CanSendRequests)).ToList();
        // If there are no invalid users, we can skip processing.
        if (invalidUsers.Count == 0)
            return;

        // Try to locate a match for this object.
        foreach (var invalid in invalidUsers)
            if (_watcher.TryGetExisting(invalid.HashedIdent, out IntPtr match) && match == address)
            {
                Logger.LogDebug($"(Radar) Unresolved user [{invalid.DisplayName}] now visible.", LoggerType.RadarData);
                _manager.UpdateVisibility(new(invalid.UID), address);
                break;
            }
    }

    /// <summary>
    ///     Whenever an object is deleted. Or 'unrendered', set visibility to false, but do not remove.
    /// </summary>
    private unsafe void OnObjectDeleted(IntPtr address)
    {
        // Locate the user matching this address.
        if (_manager.RadarUsers.FirstOrDefault(u => u.Address == address) is not { } match)
            return;
        // Update their visibility.
        Logger.LogDebug($"(Radar) Resolved user [{match.DisplayName}] no longer visible.", LoggerType.RadarData);
        _manager.UpdateVisibility(new(match.UID), IntPtr.Zero);
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