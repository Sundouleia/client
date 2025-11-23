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
using TerraFX.Interop.Windows;

namespace Sundouleia.Radar;

/// <summary>
///     Manages the current radar zone and all resolved, valid users. <para />
///     identifies players valid for chatting and list display, with additional info.
/// </summary>
public sealed class RadarManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    // Keep keyed UserData private, preventing unwanted access.
    private ConcurrentDictionary<UserData, RadarUser> _allRadarUsers = new(UserDataComparer.Instance);
    private Lazy<List<RadarUser>> _usersInternal;

    public RadarManager(ILogger<RadarManager> logger, SundouleiaMediator mediator,
        MainConfig config, SundesmoManager sundesmos, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _sundesmos = sundesmos;
        _watcher = watcher;

        _usersInternal = new Lazy<List<RadarUser>>(() => _allRadarUsers.Values.ToList());

        Mediator.Subscribe<RadarAddOrUpdateUser>(this, _ => AddOrUpdateUser(_.UpdatedUser, IntPtr.Zero));
        Mediator.Subscribe<RadarRemoveUser>(this, _ => RemoveUser(_.User));
        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearUsers());
        Svc.ContextMenu.OnMenuOpened += OnRadarContextMenu;

#if DEBUG
        // Generate some dummy entries.
        Mediator.Subscribe<ConnectedMessage>(this, _ =>
        {
            for (int i = 0; i < 5; i++)
            {
                var toAdd = new RadarUser(_sundesmos, new(new($"Dummy Sender {i}"), $"RandomIdent{i}"), IntPtr.Zero);
                _allRadarUsers.TryAdd(new($"Dummy Sender {i}"), toAdd);
            }
            RecreateLazy();
        });
#endif
    }

    // Expose the RadarUser, keeping the UserData private.
    public List<RadarUser> RadarUsers => _usersInternal.Value;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ContextMenu.OnMenuOpened -= OnRadarContextMenu;
    }

    private void OnRadarContextMenu(IMenuOpenedArgs args)
    {
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_config.Current.ShowContextMenus) return;
        if (args.Target is not MenuTargetDefault target || target.TargetObjectId == 0) return;

        // Locate the user to display it in.
        Logger.LogTrace("Context menu opened, checking for radar user.", LoggerType.RadarManagement);
        foreach (var (userData, radarUser) in _allRadarUsers)
        {
            // If they are already a pair, skip.
            if (_sundesmos.ContainsSundesmo(radarUser.UID)) continue;
            // If they are not valid / rendered, skip.
            if (!radarUser.IsValid) continue;
            // If they do not match the targetObjectId, skip.
            if (target.TargetObjectId != radarUser.PlayerObjectId) continue;
            
            // Otherwise, we found a match, so log it.
            Logger.LogDebug($"Context menu target matched radar user {radarUser.AnonymousName}.", LoggerType.RadarManagement);
            args.AddMenuItem(new MenuItem()
            {
                Name = new SeStringBuilder().AddText("Send Temporary Request").Build(),
                PrefixChar = 'S',
                PrefixColor = 708,
                OnClicked = _ => Mediator.Publish(new SendTempRequestMessage(userData)),
            });
        }
    }

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

        Logger.LogDebug($"Adding radar user {user.User.AnonName} with address {address:X}.", LoggerType.RadarManagement);
        if (_allRadarUsers.TryGetValue(user.User, out var existing))
        {
            Logger.LogDebug($"Updating radar user {user.User.AnonName}.", LoggerType.RadarManagement);
            existing.UpdateOnlineUser(user);
        }
        else
        {
            Logger.LogDebug($"Creating new radar user {user.User.AnonName}.", LoggerType.RadarManagement);
            // Attempt to fetch their visibility if the address was zero in the call.
            if (address == IntPtr.Zero && !string.IsNullOrEmpty(user.Ident))
                _watcher.TryGetExisting(user.Ident, out address);
            // Not create the user.
            _allRadarUsers.TryAdd(user.User, new RadarUser(_sundesmos, user, address));
        }
        // Could have removed hashedIdent from the User, so we should remove them from the list.
        RecreateLazy();
    }

    public void UpdateVisibility(UserData user, IntPtr address)
    {
        if (!_allRadarUsers.TryGetValue(user, out var existing))
            return;
        // Update visibility.
        Logger.LogDebug($"(Radar) {existing.AnonymousName} is now {(address != IntPtr.Zero ? "rendered" : "unrendered")}.", LoggerType.RadarManagement);
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