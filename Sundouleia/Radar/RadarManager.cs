using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Radar;

/// <summary>
///     Manages the current radar zone and all resolved, valid users. <para />
///     identifies players valid for chatting and list display, with additional info.
/// </summary>
public sealed class RadarManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly RequestsManager _requests;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    // Keep keyed UserData private, preventing unwanted access.
    private ConcurrentDictionary<UserData, RadarUser> _allRadarUsers = new(UserDataComparer.Instance);
    private Lazy<List<RadarUser>> _usersInternal;

    public RadarManager(ILogger<RadarManager> logger, SundouleiaMediator mediator,
        MainConfig config, RequestsManager requests, SundesmoManager sundesmos, 
        CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _requests = requests;
        _sundesmos = sundesmos;
        _watcher = watcher;

        _usersInternal = new Lazy<List<RadarUser>>(() => _allRadarUsers.Values.ToList());

        Mediator.Subscribe<WatchedObjectCreated>(this, _ => OnObjectCreated(_.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, _ => OnObjectDeleted(_.Address));
        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearUsers());

        Svc.ContextMenu.OnMenuOpened += OnRadarContextMenu;
    }

    // Expose the RadarUser, keeping the UserData private.
    public List<RadarUser> RadarUsers => _usersInternal.Value;

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
        foreach (var (userData, radarUser) in _allRadarUsers)
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

    public bool ContainsUser(UserData user)
        => _allRadarUsers.ContainsKey(user);

    public bool IsUserRendered(UserData user)
        => _allRadarUsers.TryGetValue(user, out var match) && match.IsValid;

    public bool TryGetUser(UserData user, [NotNullWhen(true)] out RadarUser? radarUser)
        => _allRadarUsers.TryGetValue(user, out radarUser);

    // Add a user, regardless of visibility.
    public void AddOrUpdateUser(OnlineUser user, IntPtr address)
    {
        if (user.User.UID == MainHub.UID)
            return; // Ignore Self.

        // For Updates
        Logger.LogDebug($"Updating {user.User.AnonName} with address {address:X}.", LoggerType.RadarManagement);
        if (_allRadarUsers.TryGetValue(user.User, out var existing))
        {
            Logger.LogDebug($"Updating radar user {user.User.AnonName}.", LoggerType.RadarManagement);
            existing.UpdateOnlineUser(user);
        }
        // For Adding
        else
        {
            Logger.LogDebug($"Creating new radar user {user.User.AnonName}.", LoggerType.RadarManagement);
            // Attempt to fetch their visibility if the address was zero in the call.
            if (address == IntPtr.Zero && !string.IsNullOrEmpty(user.Ident))
                _watcher.TryGetExisting(user.Ident, out address);

            // Attempt to add the user. If it was successful, try updating the sundesmo.
            _allRadarUsers.TryAdd(user.User, new RadarUser(_sundesmos, _requests, user, address));
        }
        // Could have removed hashedIdent from the User, so we should remove them from the list.
        RecreateLazy();
    }

    public void UpdateVisibility(UserData user, IntPtr address)
    {
        if (!_allRadarUsers.TryGetValue(user, out var existing))
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
        _allRadarUsers.TryRemove(user, out _);
        RecreateLazy();
    }

    public void RefreshUser(UserData user)
    {
        if (!_allRadarUsers.TryGetValue(user, out var existing))
            return;
        existing.RefreshSundesmo();
        RecreateLazy();
    }

    public void RefreshUsers()
    {
        foreach (var radarUser in _allRadarUsers.Values)
            radarUser.RefreshSundesmo();
        RecreateLazy();
    }

    public void ClearUsers()
    {
        Logger.LogDebug("Clearing all valid radar users.", LoggerType.RadarManagement);
        // Nothing to dispose of (yet), so just clear.
        _allRadarUsers.Clear();
        RecreateLazy();
    }

    private void RecreateLazy(bool reorderOnly = false)
    {
        _usersInternal = new Lazy<List<RadarUser>>(() => _allRadarUsers.Values.ToList());
        Mediator.Publish(new FolderUpdateRadar());
    }
}