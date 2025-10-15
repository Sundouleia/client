using CkCommons;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
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
        Mediator.Subscribe<RadarAddOrUpdateUser>(this, _ => OnAddOrUpdateUser(_.UpdatedUser));
        Mediator.Subscribe<RadarRemoveUser>(this, _ => OnRemoveUser(_.User));
        Mediator.Subscribe<ConnectedMessage>(this, async _ =>
        {
            if (_config.Current.RadarEnabled && Svc.ClientState.IsLoggedIn)
            {
                await SundouleiaEx.WaitForPlayerLoading();
                await JoinZoneAndAssignUsers(GetZoneUpdate()).ConfigureAwait(false);
            }
        });

        // Set defaults.
        CurrentZone = PlayerContent.TerritoryID;
        // Listen to zone changes.
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        Svc.ClientState.Logout += (_, __) => _manager.ClearUsers();
    }

    public ushort CurrentZone { get; private set; } = 0;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.ClientState.Logout -= (_, __) => _manager.ClearUsers();
    }
     
    private RadarZoneUpdate GetZoneUpdate()
    {
        var world = PlayerData.CurrentWorldIdInstanced;
        var zone = CurrentZone;
        var joinChats = _config.Current.RadarJoinChats;
        var hashedCID = _config.Current.RadarSendPings ? SundouleiaSecurity.GetClientIdentHashThreadSafe() : string.Empty;
        return new(world, zone, joinChats, hashedCID);
    }

    private async void OnTerritoryChanged(ushort newTerritory)
    {
        // Ignore territories from login zone / title screen (if any even exist)
        if (!Svc.ClientState.IsLoggedIn)
            return;

        // If we do not want to send radar updates, then dont.
        if (!_config.Current.RadarEnabled)
        {
            CurrentZone = newTerritory;
            return;
        }


        Logger.LogInformation($"Territory changed from {CurrentZone} to {newTerritory}", LoggerType.RadarData);
        CurrentZone = newTerritory;

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
                Logger.LogWarning($"Failed to join radar zone {CurrentZone} [{zoneInfo.ErrorCode}] [Users Null?: {zoneInfo.Value is null}]");
                return;
            }

            GameDataSvc.WorldData.TryGetValue(info.WorldId, out var worldName);
            var territoryName = PlayerContent.TerritoryName;
            Logger.LogInformation($"RadarZone [{worldName} | {territoryName} ({info.TerritoryId})] joined. There are {zoneInfo.Value.CurrentUsers.Count} other Sundesmos.", LoggerType.RadarData);
            foreach (var radarUser in zoneInfo.Value.CurrentUsers.ToList())
                ResolveUser(radarUser);
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
        var invalidUsers = _manager.AllUsers.Except(_manager.RenderedUsers).ToList();
        // If there are no invalid users, we can skip processing.
        if (invalidUsers.Count == 0)
            return;

        // Try to locate a match for this object.
        foreach (var invalid in invalidUsers)
            if (_watcher.TryGetExisting(invalid.HashedIdent, out IntPtr foundAddress) && foundAddress == address)
            {
                Logger.LogDebug($"(Radar) Unresolved user [{invalid.AnonymousName}] now visible.", LoggerType.RadarData);
                _manager.UpdateVisibility(new(invalid.UID), address);
                break;
            }
    }

    /// <summary>
    ///     Whenever an object is deleted. Remove them from the radar users or manager user list.
    /// </summary>
    private unsafe void OnObjectDeleted(IntPtr address)
    {
        // Locate the user matching this address.
        if (_manager.RenderedUsers.FirstOrDefault(u => u.Address == address) is not { } match)
            return;
        // Update their visibility.
        Logger.LogDebug($"(Radar) Resolved user [{match.AnonymousName}] no longer visible.", LoggerType.RadarData);
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

    // Add or update a user in our radar.
    private void OnAddOrUpdateUser(OnlineUser radarUser)
    {
        if (!_manager.HasUser(radarUser))
        {
            Logger.LogDebug($"(Radar) AddOrUpdate from [{radarUser.User.AnonName}] detected, who is not in our manager. Resolving!", LoggerType.RadarData);
            ResolveUser(radarUser);
            return;
        }
        // Otherwise, update them.
        Logger.LogDebug($"(Radar) AddOrUpdate from [{radarUser.User.AnonName}] detected, updating their state.", LoggerType.RadarData);
        _manager.UpdateUserState(radarUser);
    }

    // Remove a user completely from our radar.
    private void OnRemoveUser(UserData toRemove)
    {
        Logger.LogDebug($"(Radar) RemoveUser triggered, atomizing them from the manager.", LoggerType.RadarData);
        _manager.RemoveRadarUser(toRemove);
    }

    private void ResolveUser(OnlineUser radarUser)
    {
        // Ignore existing users as we have already resolved their current state.
        if (_manager.HasUser(radarUser))
            return;

        try
        {
            // Attempt to resolve them via the watcher.
            var isValid = _watcher.TryGetExisting(radarUser.Ident, out IntPtr address);
            // Regardless of the outcome, append it to the manager list, the address determines validity.
            Logger.LogDebug($"(Radar) Adding [{radarUser.User.AnonName}] as a {(isValid ? "Valid" : "Invalid")} user.", LoggerType.RadarData);
            _manager.AddRadarUser(radarUser, address);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Exception occurred while resolving radar user [{radarUser.User.AnonName}]");
        }
    }
}