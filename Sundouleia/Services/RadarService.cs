using CkCommons;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
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
    private readonly RequestsManager _requests;
    private readonly RadarManager _manager;
    private readonly CharaObjectWatcher _watcher;

    public RadarService(ILogger<RadarService> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, RequestsManager requests, RadarManager manager, 
        CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _requests = requests;
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
        Mediator.Subscribe<DisconnectedMessage>(this, _ => _manager.ClearUsers());

        // Listen to zone changes.
        Svc.ContextMenu.OnMenuOpened += OnRadarContextMenu;
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

    private void OnRadarContextMenu(IMenuOpenedArgs args)
    {
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_config.Current.ShowContextMenus) return;
        if (args.Target is not MenuTargetDefault target || target.TargetObjectId == 0) return;

        // Locate the user to display it in.
        Logger.LogTrace("Context menu opened, checking for radar user.", LoggerType.RadarManagement);
        foreach (var radarUser in _manager.RenderedUnpairedUsers.ToList())
        {
            if (!radarUser.IsValid)
                continue;

            if (target.TargetObjectId != radarUser.PlayerObjectId)
                continue;

            Logger.LogDebug($"Context menu target matched radar user {radarUser.AnonymousName}.", LoggerType.RadarManagement);
            args.AddMenuItem(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Send Temporary Request").Build(),
                PrefixChar = 'S',
                PrefixColor = 708,
                OnClicked = (a) => SendTempRequest(radarUser),
            });
        }
    }

    private async void SendTempRequest(RadarUser user)
    {
        var msg = $"Temporary Pair Request from {user.AnonymousName}";
        var ret = await _hub.UserSendRequest(new(new(user.UID), true, msg)).ConfigureAwait(false);
        if (ret.ErrorCode is not SundouleiaApiEc.Success)
        {
            Logger.LogWarning($"Failed to send temporary pair request to {user.AnonymousName} [{ret.ErrorCode}]", LoggerType.RadarData);
            return;
        }
        Logger.LogInformation($"Temporary pair request sent to {user.AnonymousName}.", LoggerType.RadarData);
        _requests.AddRequest(ret.Value!);
    }

    private async void OnLogin()
    {
        await SundouleiaEx.WaitForPlayerLoading();
        CurrWorld = PlayerData.CurrentWorldIdInstanced;
        CurrWorldName = PlayerData.CurrentWorldNameInstanced;
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
        await _hub.RadarZoneLeave().ConfigureAwait(false);
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
        if (radarUser.User.UID == MainHub.UID)
            return;

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
        if (radarUser.User.UID == MainHub.UID)
            return;

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