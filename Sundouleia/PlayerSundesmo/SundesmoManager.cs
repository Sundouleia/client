using CkCommons;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using SundouleiaAPI.Util;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.Windows;

namespace Sundouleia.Pairs;

/// <summary>
///     Manager for paired connections. <para />
///     This also applies for temporary pairs.
/// </summary>
public sealed class SundesmoManager : DisposableMediatorSubscriberBase
{
    // concurrent dictionary of all paired paired to the client. 
    private readonly ConcurrentDictionary<UserData, Sundesmo> _allSundesmos;
    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly SundesmoFactory _pairFactory;

    private Lazy<List<Sundesmo>> _directPairsInternal;  // the internal direct pairs lazy list for optimization
    public List<Sundesmo> DirectPairs => _directPairsInternal.Value; // the direct pairs the client has with other users.

    public SundesmoManager(ILogger<SundesmoManager> logger, SundouleiaMediator mediator,
        SundesmoFactory factory, MainConfig config, ServerConfigManager serverConfigs) : base(logger, mediator)
    {
        _allSundesmos = new(UserDataComparer.Instance);
        _pairFactory = factory;
        _config = config;
        _serverConfigs = serverConfigs;

        Mediator.Subscribe<ConnectedMessage>(this, _ => OnClientConnected());
        Mediator.Subscribe<ReconnectedMessage>(this, _ => OnClientConnected());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => OnClientDisconnected(_.Intent));

        Mediator.Subscribe<CutsceneEndMessage>(this, _ => ReapplyAlterations());
        Mediator.Subscribe<TargetSundesmoMessage>(this, (msg) => TargetSundesmo(msg.Sundesmo));

        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Svc.ContextMenu.OnMenuOpened += OnOpenContextMenu;
    }

    private void OnOpenContextMenu(IMenuOpenedArgs args)
    {
        Logger.LogInformation("Opening Pair Context Menu of type "+args.MenuType, LoggerType.PairManagement);
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_config.Current.ShowContextMenus) return;
        if (args.Target is not MenuTargetDefault target || target.TargetObjectId == 0) return;
        // Find the sundesmo that matches this and display the results.
        if (DirectPairs.FirstOrDefault(p => p.IsRendered && p.PlayerObjectId == target.TargetObjectId && !p.IsPaused) is not { } match)
            return;

        Logger.LogDebug($"Found matching pair for context menu: {match.GetNickAliasOrUid()}", LoggerType.PairManagement);
        // This only works when you create it prior to adding it to the args,
        // otherwise the += has trouble calling. (it would fall out of scope)
        var subMenu = new MenuItem();
        subMenu.IsSubmenu = true;
        subMenu.Name = "Sundouleia";
        subMenu.PrefixChar = 'S';
        subMenu.PrefixColor = 708;
        subMenu.OnClicked += match.OpenSundouleiaSubMenu;
        args.AddMenuItem(subMenu);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ContextMenu.OnMenuOpened -= OnOpenContextMenu;
        DisposeSundesmos();
    }

    public void AddSundesmo(UserPair dto)
    {
        var exists = _allSundesmos.ContainsKey(dto.User);
        var msg = $"User ({dto.User.UID}) {(exists ? "found, applying latest!" : "not found. Creating!")}.";
        Logger.LogDebug(msg, LoggerType.PairManagement);
        if (exists) 
            _allSundesmos[dto.User].ReapplyAlterations();
        else
            _allSundesmos[dto.User] = _pairFactory.Create(dto);
        RecreateLazy();
    }

    public void AddSundesmos(IEnumerable<UserPair> list)
    {
        var created = new List<string>();
        var refreshed = new List<string>();
        foreach (var dto in list)
        {
            if (!_allSundesmos.ContainsKey(dto.User))
            {
                _allSundesmos[dto.User] = _pairFactory.Create(dto);
                created.Add(dto.User.UID);
            }
            else
            {
                refreshed.Add(dto.User.UID);
            }
        }
        RecreateLazy();
        if (created.Count > 0) Logger.LogDebug($"Created: {string.Join(", ", created)}", LoggerType.PairManagement);
        if (refreshed.Count > 0) Logger.LogDebug($"Refreshed: {string.Join(", ", refreshed)}", LoggerType.PairManagement);
    }

    public void RemoveSundesmo(UserDto dto)
    {
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            return;

        // Remove the pair, marking it offline then disposing of its handler resources, and finally removing it from the manager.
        sundesmo.MarkOffline();
        sundesmo.DisposeData();
        _allSundesmos.TryRemove(dto.User, out _);
        RecreateLazy();
    }

    public void UpdateToPermanent(UserDto dto)
    {
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            return;
        // Update their UserPair state to permanent from temporary, if applicable.
        sundesmo.MarkAsPermanent();
    }

    public void OnClientConnected()
    {
        Logger.LogInformation("Client connected, cancelling any disconnect timeouts.", LoggerType.PairManagement);
        // Halt all timeouts that were in progress. (may cause issues since we do this after everyone goes online but we'll see)
        Parallel.ForEach(_allSundesmos, s => s.Value.EndTimeout());
    }

    /// <summary>
    ///     Mark all sundesmos as offline upon a disconnection, ensuring that everyone follows the 
    ///     same timeout flow.
    /// </summary>
    public void OnClientDisconnected(DisconnectIntent intent)
    {
        Logger.LogInformation("Client disconnected, marking all sundesmos as offline.", LoggerType.PairManagement);
        // If a hard disconnect, dispose of the data after.
        Parallel.ForEach(_allSundesmos, s =>
        {
            if ((int)intent > 1)
                s.Value.MarkForUnload();

            s.Value.MarkOffline();
            // If it was a hard disconnect, we should dispose of the data.
            if (intent is DisconnectIntent.LogoutShutdown)
                s.Value.DisposeData();
        });
        // Recreate the lazy list.
        RecreateLazy();
    }

    /// <summary>
    ///     Fully disposes of all Sundesmos and their handlers.
    ///     This will stop all application of all data exchange.
    /// </summary>
    private void DisposeSundesmos()
    {
        Logger.LogDebug("Disposing all Pairs", LoggerType.PairManagement);
        var pairCount = _allSundesmos.Count;
        foreach (var sundesmo in _allSundesmos.Values)
        {
            Logger.LogTrace($"Disposing {sundesmo.PlayerName}({sundesmo.GetNickAliasOrUid()})", LoggerType.PairManagement);
            sundesmo.MarkOffline();
            sundesmo.DisposeData();
        }
        //Parallel.ForEach(_allSundesmos, item =>
        //{
        //    item.Value.MarkOffline();
        //    item.Value.DisposeData();
        //});
        Logger.LogDebug($"Marked {pairCount} sundesmos as offline", LoggerType.PairManagement);
        RecreateLazy();
    }

    /// <summary> 
    ///     A Sundesmo has just come online. We should mark this, and also run a check for
    ///     player visibility against the Sundouleia Object Watcher. <para />
    ///     
    ///     NOTE: This does not mean the player is visible, only that they are connected.
    /// </summary>
    public void MarkSundesmoOnline(OnlineUser dto, bool notify = true)
    {
        // Attempt to get the sundesmo via the UserData.
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            throw new InvalidOperationException($"No user found [{dto}]");
        
        // They were found, so refresh any existing profile data.
        Mediator.Publish(new ClearProfileDataMessage(dto.User));

        // If they are already online simply recreate the list. This only happens from sudden DC's we cant track.
        if (sundesmo.IsOnline)
        {
            RecreateLazy();
            return;
        }

        // Init the proper first-time online message.
        if (notify && _config.Current.OnlineNotifications)
        {
            var nick = sundesmo.GetNickname();
            // Do not show if we limit it to nicked pairs and there is no nickname.
            if (!(_config.Current.NotifyLimitToNickedPairs && string.IsNullOrEmpty(nick)))
            {
                var msg = !string.IsNullOrEmpty(nick) ? $"{nick} ({dto.User.AliasOrUID}) is now online" : $"{dto.User.AliasOrUID} is now online";
                Mediator.Publish(new NotificationMessage("Sundesmo Online", msg, NotificationType.Info, TimeSpan.FromSeconds(2)));
            }
        }

        Logger.LogTrace($"Marked {sundesmo.PlayerName}({sundesmo.GetNickAliasOrUid()}) as online", LoggerType.PairManagement);
        // Mark online internally, and then recreate the whitelist display.
        sundesmo.MarkOnline(dto);
        RecreateLazy();
    }

    /// <summary>
    ///     Marks a user as reloading. Implying they are shutting down the plugin or performing an interaction
    ///     that clears their sundesmos information. <para />
    ///     Useful to help us identify when a sundesmo is in need of a full data update or not.
    /// </summary>
    public void MarkSundesmoForUnload(UserData user)
    {
        if (_allSundesmos.TryGetValue(user, out var pair))
        {
            Logger.LogTrace($"Marked {pair.PlayerName}({pair.GetNickAliasOrUid()}) as reloading", LoggerType.PairManagement);
            pair.MarkForUnload();
        }
    }

    /// <summary>
    ///     Marks a user pair as offline. This will clear their OnlineUserDTO. <para />
    ///     A Sundesmo's Chara* can still be valid while offline, and they can still download & apply changes.
    /// </summary>
    public void MarkSundesmoOffline(UserData user)
    {
        if (_allSundesmos.TryGetValue(user, out var pair))
        {
            Logger.LogTrace($"Marked {pair.PlayerName}({pair.GetNickAliasOrUid()}) as offline", LoggerType.PairManagement);
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            pair.MarkOffline();
            RecreateLazy();

        }
    }

    private void ReapplyAlterations()
    {
        foreach (var pair in _allSundesmos.Values)
            pair.ReapplyAlterations();
    }

    private void TargetSundesmo(Sundesmo s)
    {
        if (PlayerData.IsInPvP || !s.IsRendered) return;
        unsafe
        {
            if (_config.Current.FocusTargetOverTarget)
                TargetSystem.Instance()->FocusTarget = (GameObject*)s.PlayerAddress;
            else
                TargetSystem.Instance()->SetHardTarget((GameObject*)s.PlayerAddress);
        }
    }

    // While this is nice to have, I often wonder what value or purpose it serves.
    // Ideally, it would be nicer if the folders containing any immutable lists
    // these are in simply updated on certain triggers, and had their sorting algorithms
    // embedded inside of them that could trigger upon a necessary refresh.
    //
    // But this will do for now.. Until we need to change things (which may be soon)
    // because creating a mass list for all our pairs in multiple locations is a lot of allocation.
    private void RecreateLazy()
    {
        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Mediator.Publish(new RefreshFolders(true, true, false));

    }

    #region Manager Helpers
    /// <summary>
    ///     Sundesmos that we have an OnlineUser DTO of, implying they are connected.
    /// </summary>
    public List<Sundesmo> GetOnlineSundesmos() => _allSundesmos.Where(p => !string.IsNullOrEmpty(p.Value.Ident)).Select(p => p.Value).ToList();

    /// <summary>
    ///     Sundesmos that we have an OnlineUser DTO of, implying they are connected.
    /// </summary>
    public List<UserData> GetOnlineUserDatas() => _allSundesmos.Where(p => !string.IsNullOrEmpty(p.Value.Ident)).Select(p => p.Key).ToList();

    /// <summary>
    ///     The number of sundesmos that are in our render range. <para />
    ///     NOTE: This does not mean that they have applied data!
    /// </summary>
    public int GetVisibleCount() => _allSundesmos.Count(p => p.Value.IsRendered);

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos. <para />
    ///     <b>NOTE: It is possible for a visible sundesmos to be offline!</b>
    /// </summary>
    public List<UserData> GetVisible() => _allSundesmos.Where(p => p.Value.IsRendered).Select(p => p.Key).ToList();

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos that are connected.
    /// </summary>
    public List<UserData> GetVisibleConnected() => _allSundesmos.Where(p => p.Value.IsRendered && p.Value.IsOnline).Select(p => p.Key).ToList();

    /// <summary>
    ///     If a Sundesmo exists given their UID.
    /// </summary>
    public bool ContainsSundesmo(string uid) => _allSundesmos.ContainsKey(new(uid));

    /// <summary>
    ///     Useful for cases where you have the UID but you dont have the pair object and 
    ///     need a way to get the nickname/alias without iterating through them all.
    /// </summary>
    public bool TryGetNickAliasOrUid(string uid, [NotNullWhen(true)] out string? nickAliasUid)
    {
        nickAliasUid = _serverConfigs.GetNicknameForUid(uid) ?? _allSundesmos.Keys.FirstOrDefault(p => p.UID == uid)?.AliasOrUID;
        return !string.IsNullOrWhiteSpace(nickAliasUid);
    }

    /// <summary>
    ///     Attempt to retrieve a sundesmo by <see cref="UserData"/>. If failed, null is returned.
    /// </summary>
    public Sundesmo? GetUserOrDefault(UserData user) => _allSundesmos.TryGetValue(user, out var sundesmo) ? sundesmo : null;

    #endregion Manager Helpers

    #region Updates
    // Should happen only on initial loads.
    public void ReceiveIpcUpdateFull(UserData target, NewModUpdates newModData, VisualUpdate newIpc, bool isInitialData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"Received update for {sundesmo.GetNickAliasOrUid()}'s mod and appearance data!", LoggerType.Callbacks);
        sundesmo.SetFullDataChanges(newModData, newIpc, isInitialData).ConfigureAwait(false);
    }

    // Happens whenever mods should be added or removed.
    public void ReceiveIpcUpdateMods(UserData target, NewModUpdates newModData, string manipString)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"Received update for {sundesmo.GetNickAliasOrUid()}'s mod data!", LoggerType.Callbacks);
        sundesmo.SetModChanges(newModData, manipString);
    }

    // Happens whenever non-mod visuals are updated.
    public void ReceiveIpcUpdateOther(UserData target, VisualUpdate newIpc)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"{sundesmo.GetNickAliasOrUid()}'s appearance data updated!", LoggerType.Callbacks);
        sundesmo.SetIpcChanges(newIpc);
    }

    // Happens whenever a single non-mod appearance item is updated.
    public void ReceiveIpcUpdateSingle(UserData target, OwnedObject relatedObject, IpcKind type, string newData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"{sundesmo.GetNickAliasOrUid()}'s [{relatedObject}] updated its [{type}] data!", LoggerType.Callbacks);
        sundesmo.SetIpcChanges(relatedObject, type, newData);
    }

    // A pair updated one of their global permissions, so handle the change properly.
    public void PermChangeGlobal(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // Fail if the change could not be properly set.
        if (!PropertyChanger.TrySetProperty(sundesmo.PairGlobals, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        // Log change and lazily recreate the pairlist.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s GlobalPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();
    }

    public void PermChangeGlobal(UserData target, GlobalPerms newGlobals)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // cache prev globals and update them.
        var prevGlobals = sundesmo.PairGlobals with { };
        sundesmo.UserPair.Globals = newGlobals;

        // Log change and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s GlobalPerms updated in bulk]", LoggerType.PairDataTransfer);
        RecreateLazy();
    }

    public void PermChangeUnique(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // If we need to cache the previous state of anything here do so.
        var prevPause = sundesmo.OwnPerms.PauseVisuals;

        // Perform change.
        if (!PropertyChanger.TrySetProperty(sundesmo.OwnPerms, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        // Log change and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s OwnPairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Clear profile is pause toggled.
        if (prevPause != sundesmo.OwnPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }

    public void PermChangeUniqueOther(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // If we need to cache the previous state of anything here do so.
        var prevPause = sundesmo.PairPerms.PauseVisuals;

        if (!PropertyChanger.TrySetProperty(sundesmo.PairPerms, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s PairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Toggle pausing if pausing changed.
        if (prevPause != sundesmo.PairPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }

    public void PermBulkChangeUnique(UserData target, PairPerms newPerms)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // cache prev state and update them.
        var prevPerms = sundesmo.OwnPerms with { };
        sundesmo.UserPair.OwnPerms = newPerms;

        // Log and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s OwnPerms updated in bulk.]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Clear profile if pausing changed.
        if (prevPerms.PauseVisuals != newPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }

    #endregion Updates
}
