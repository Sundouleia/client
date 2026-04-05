using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar.Factories;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Radar;

/// <summary>
///   Manages the Public radar users for the current location.
///   Updated by the radar distributor, and manged by callbacks.
/// </summary>
public sealed class RadarManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly RadarFactory _radarFactory;
    private readonly RequestsManager _requests;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaWatcher _watcher;

    // Keyed by UserData, ensure this info is private.
    // private ConcurrentDictionary<UserData, RadarPublicUser> _ = new(UserDataComparer.Instance);
    private ConcurrentDictionary<UserData, RadarPublicUser> _allPublicUsers = new(UserDataComparer.Instance);
    private Lazy<List<RadarPublicUser>> _publicUsersInternal;

    public RadarManager(ILogger<RadarManager> logger, SundouleiaMediator mediator,
        MainConfig config, RadarFactory factory, RequestsManager requests, 
        SundesmoManager sundesmos, CharaWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _radarFactory = factory;
        _requests = requests;
        _sundesmos = sundesmos;
        _watcher = watcher;

        _publicUsersInternal = new Lazy<List<RadarPublicUser>>(() => _allPublicUsers.Values.ToList());

        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearUsers());

        Svc.ContextMenu.OnMenuOpened += OnRadarContextMenu;
    }

    // Expose the RadarUser, keeping the UserData private.
    public List<RadarPublicUser> RadarUsers => _publicUsersInternal.Value;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ContextMenu.OnMenuOpened -= OnRadarContextMenu;
    }

    #region Events
    private void OnRadarContextMenu(IMenuOpenedArgs args)
    {
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_config.Current.ShowContextMenus) return;
        if (args.Target is not MenuTargetDefault target || target.TargetObjectId == 0) return;

        // Locate the user to display it in.
        Logger.LogTrace("Context menu opened, checking for radar user.", LoggerType.RadarManagement);
        foreach (var (userData, radarUser) in _allPublicUsers)
        {
            // If they are a pair, or in requests, skip.
            if (radarUser.IsPaired || radarUser.InRequests) continue;
            // If they do not match the targetObjectId, skip.
            if (target.TargetObjectId != radarUser.PlayerObjectId) continue;
            
            // Otherwise, we found a match, so log it.
            Logger.LogDebug($"Context menu target matched radar user {radarUser.DisplayName}.", LoggerType.RadarManagement);
            args.AddMenuItem(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Send Temporary Request").Build(),
                PrefixChar = 'S',
                PrefixColor = 527,
                OnClicked = _ => Mediator.Publish(new SendTempRequestMessage(userData)),
            });
        }
    }

    /// <summary>
    ///     Whenever a new object is rendered. Should check against the list of current radar users.
    /// </summary>
    private unsafe void OnObjectCreated(IntPtr address)
    {
        // Obtain the list of all users except the valid ones to get the invalid ones.
        var invalidUsers = RadarUsers.Where(u => (!u.IsValid && u.CanSendRequests)).ToList();
        // If there are no invalid users, we can skip processing.
        if (invalidUsers.Count == 0)
            return;

        // Try to locate a match for this object.
        foreach (var invalid in invalidUsers)
            if (_watcher.TryGetExisting(invalid.HashedIdent, out IntPtr match) && match == address)
            {
                Logger.LogDebug($"(Radar) Unresolved user [{invalid.DisplayName}] now visible.", LoggerType.RadarData);
                UpdateVisibility(new(invalid.UID), address);
                break;
            }
    }

    /// <summary>
    ///     Whenever an object is deleted. Or 'unrendered', set visibility to false, but do not remove.
    /// </summary>
    private unsafe void OnObjectDeleted(IntPtr address)
    {
        // Locate the user matching this address.
        if (RadarUsers.FirstOrDefault(u => u.Address == address) is not { } match)
            return;
        // Update their visibility.
        Logger.LogDebug($"(Radar) Resolved user [{match.DisplayName}] no longer visible.", LoggerType.RadarData);
        UpdateVisibility(new(match.UID), IntPtr.Zero);
    }
    #endregion Events

    // Creates or re-initializes all radar users for the current area.
    public void CreateOrReinitUsers(IEnumerable<RadarMember> userList)
    {
        var created = new List<string>();
        var refreshed = new List<string>();
        foreach (var user in userList)
        {
            if (_allPublicUsers.TryGetValue(user.User, out var existing))
            {
                // Dont do anything for the moment?
                refreshed.Add(user.User.AnonName);
            }
            else
            {
                _allPublicUsers[user.User] = _radarFactory.Create(user);
                created.Add(user.User.AnonName);
            }

            // Maybe attempt an initial visibility check here, or add the check internally.
        }

        RecreateLazy();
        if (created.Count > 0) Logger.LogDebug($"Created: {string.Join(", ", created)}", LoggerType.PairManagement);
        if (refreshed.Count > 0) Logger.LogDebug($"Refreshed: {string.Join(", ", refreshed)}", LoggerType.PairManagement);
    }

    public void AddUpdateRadarUser(RadarMember radarUser)
    {

    }

    // Add a user, regardless of visibility.
    public void AddOrUpdateUser(OnlineUser user, IntPtr address)
    {
        if (user.User.UID == MainHub.UID)
            return; // Ignore Self.

        //// For Updates
        //Logger.LogDebug($"Updating {user.User.AnonName} with address {address:X}.", LoggerType.RadarManagement);
        //if (_allPublicUsers.TryGetValue(user.User, out var existing))
        //{
        //    Logger.LogDebug($"Updating radar user {user.User.AnonName}.", LoggerType.RadarManagement);
        //    existing.UpdateOnlineUser(user);
        //}
        //// For Adding
        //else
        //{
        //    Logger.LogDebug($"Creating new radar user {user.User.AnonName}.", LoggerType.RadarManagement);
        //    // Attempt to fetch their visibility if the address was zero in the call.
        //    if (address == IntPtr.Zero && !string.IsNullOrEmpty(user.Ident))
        //        _watcher.TryGetExisting(user.Ident, out address);

        //    // Attempt to add the user. If it was successful, try updating the sundesmo.
        //    _allPublicUsers.TryAdd(user.User, new RadarPublicUser(_sundesmos, _requests, user, address));
        //}
        // Could have removed hashedIdent from the User, so we should remove them from the list.
        RecreateLazy();
    }

    public void UpdateVisibility(UserData user, IntPtr address)
    {
        if (!_allPublicUsers.TryGetValue(user, out var existing))
            return;
        // Update visibility.
        Logger.LogDebug($"(Radar) {existing.DisplayName} is now {(address != IntPtr.Zero ? "rendered" : "unrendered")}.", LoggerType.RadarManagement);
        existing.UpdateVisibility(address);
        RecreateLazy(true);
    }

    // Remove a user, regardless of visibility.
    public void RemoveUser(UserData user)
    {
        Logger.LogDebug($"(Radar) A user was removed.", LoggerType.RadarManagement);
        _allPublicUsers.TryRemove(user, out _);
        RecreateLazy();
    }

    public void RefreshUser(UserData user)
    {
        if (!_allPublicUsers.TryGetValue(user, out var existing))
            return;
        existing.RefreshSundesmo();
        RecreateLazy();
    }

    public void RefreshUsers()
    {
        foreach (var radarUser in _allPublicUsers.Values)
            radarUser.RefreshSundesmo();
        RecreateLazy();
    }

    public void ClearUsers()
    {
        Logger.LogDebug("Clearing all valid radar users.", LoggerType.RadarManagement);
        // Nothing to dispose of (yet), so just clear.
        _allPublicUsers.Clear();
        RecreateLazy();
    }

    private void RecreateLazy(bool reorderOnly = false)
    {
        _publicUsersInternal = new Lazy<List<RadarPublicUser>>(() => _allPublicUsers.Values.ToList());
        Mediator.Publish(new FolderUpdateRadar());
    }
}