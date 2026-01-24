using CkCommons;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using GagspeakAPI.Data;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using SundouleiaAPI.Util;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Pairs;

/// <summary>
///     Manager for paired connections. <para />
///     This also applies for temporary pairs.
/// </summary>
public sealed class SundesmoManager : DisposableMediatorSubscriberBase
{
    // concurrent dictionary of all paired paired to the client. 
    private readonly ConcurrentDictionary<UserData, Sundesmo> _allSundesmos = new(UserDataComparer.Instance);
    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly NicksConfig _nicks;
    private readonly SundesmoFactory _pairFactory;
    private readonly LimboStateManager _limboManager;

    private Lazy<List<Sundesmo>> _directPairsInternal;  // the internal direct pairs lazy list for optimization
    public List<Sundesmo> DirectPairs => _directPairsInternal.Value; // the direct pairs the client has with other users.

    public SundesmoManager(ILogger<SundesmoManager> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folders, NicksConfig nicks, SundesmoFactory factory,
        LimboStateManager limboManager)
        : base(logger, mediator)
    {
        _config = config;
        _folderConfig = folders;
        _nicks = nicks;
        _pairFactory = factory;
        _limboManager = limboManager;

        Mediator.Subscribe<DisconnectedMessage>(this, _ => OnClientDisconnected(_.Intent));
        Mediator.Subscribe<CutsceneEndMessage>(this, _ => ReapplyAllRendered());
        Mediator.Subscribe<TargetSundesmoMessage>(this, (msg) => TargetSundesmo(msg.Sundesmo));

        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());

        Svc.ContextMenu.OnMenuOpened += OnContextMenuOpened;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        // Run a disposal of all sundesmos.
        DisposeSundesmos();
    }

    /// <summary>
    ///     Adds a Sundesmo to the manager. Called by GetPairedUsers upon connection.
    ///     Also called when a pair goes online, or after accepting a pair request.
    /// </summary>
    /// <remarks>
    ///     Because Sundesmos are used in other classes and combos, and need to retain
    ///     a reference to the original Sundesmo, they should not be disposed of when 
    ///     disconnecting and recreated on reconnection. Instead, we check for existence 
    ///     upon adding.
    /// </remarks>
    public void AddSundesmo(UserPair dto)
    {
        var exists = _allSundesmos.ContainsKey(dto.User);
        Logger.LogDebug($"User ({dto.User.UID}) {(exists ? "found, applying latest!" : "not found. Creating!")}.", LoggerType.PairManagement);

        // Determine if we perform a reapplication, or a creation. (Maybe change later)
        if (exists) _allSundesmos[dto.User].ReapplyAlterations();
        else _allSundesmos[dto.User] = _pairFactory.Create(dto);
        
        RecreateLazy();
    }

    /// <summary>
    ///     Adds multiple Sundesmos to the manager. 
    ///     Called upon connection, when someone goes online, or after accepting a request.
    /// </summary>
    /// <remarks>
    ///     Because Sundesmos are used in other classes and combos, and need to retain
    ///     a reference to the original Sundesmo, they should not be disposed of when 
    ///     disconnecting and recreated on reconnection. Instead, we check for existence 
    ///     upon adding.
    /// </remarks>
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
                _allSundesmos[dto.User].ReapplyAlterations();
                refreshed.Add(dto.User.UID);
            }
        }
        RecreateLazy();

        if (created.Count > 0) Logger.LogDebug($"Created: {string.Join(", ", created)}", LoggerType.PairManagement);
        if (refreshed.Count > 0) Logger.LogDebug($"Refreshed: {string.Join(", ", refreshed)}", LoggerType.PairManagement);
    }

    /// <summary>
    ///     Performs a hard-removal of a sundesmo from the manager.
    ///     Usually called when unpairing from a sundesmo, or when a 
    ///     sundesmo unpaired you.
    /// </summary>
    public void RemoveSundesmo(UserDto dto)
    {
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            return;
        // send the sundesmo offline, revert them, and clear their data.
        // (This is safe to call because we plan on removing it after)
        sundesmo.DisposeData();

        // Remove it from the manager and recreate the lazy list.
        _allSundesmos.TryRemove(dto.User, out _);
        RecreateLazy();
    }

    private void DisposeSundesmos()
    {
        Logger.LogInformation("Disposing all Sundesmos", LoggerType.PairManagement);
        var pairCount = _allSundesmos.Count;
        // Replace with Parallel.ForEach after testing.
        foreach (var sundesmo in _allSundesmos.Values)
            sundesmo.DisposeData();
        _allSundesmos.Clear();
        Logger.LogInformation($"Disposed {pairCount} Sundesmos.", LoggerType.PairManagement);
        RecreateLazy();
    }

    public void UpdateToPermanent(UserDto dto)
    {
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            return;
        // Update their UserPair state to permanent from temporary, if applicable.
        sundesmo.MarkAsPermanent();
    }

    /// <summary>
    ///     Occurs whenever our client disconnects from the SundouleiaServer. <para />
    ///     What actions are taken depend on the disconnection intent.
    /// </summary>
    public void OnClientDisconnected(DisconnectIntent intent)
    {
        Logger.LogInformation($"Client disconnected with intent: {intent}", LoggerType.PairManagement);
        switch (intent)
        {
            // For normal or unexpected disconnects, simply mark all as offline. (but do not dispose)
            case DisconnectIntent.Normal:
            case DisconnectIntent.Unexpected:
                Parallel.ForEach(_allSundesmos, s => s.Value.MarkOffline());
                RecreateLazy();
                break;

            // Reloads or logouts should revert and clear all sundesmos.
            case DisconnectIntent.Reload:
                // Perform the same as the above, except with an immidiate revert.
                Parallel.ForEach(_allSundesmos, s => s.Value.MarkOffline(true));
                RecreateLazy();
                break;

            case DisconnectIntent.Logout:
                // Dispose of all sundesmos properly upon logout.
                Logger.LogInformation("Client in Logout, disposing all Sundesmos.");
                DisposeSundesmos();
                break;

            case DisconnectIntent.Shutdown:
                // If we logged out, there is no reason to have any pairs anymore.
                // However, it also should trigger the managers disposal. If it doesn't, something's wrong.
                Logger.LogInformation("Client in Logout/Shutdown, disposal will handle cleanup.");
                break;
        }
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

        // Init the proper first-time online message. (also prevent reload spamming logs)
        if (notify && _config.Current.OnlineNotifications && ! sundesmo.IsReloading)
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
        if (!_allSundesmos.TryGetValue(user, out var sundesmo))
            throw new InvalidOperationException($"No user found [{user.AliasOrUID}]");
        
        Logger.LogTrace($"Marked {sundesmo.PlayerName}({sundesmo.GetNickAliasOrUid()}) as reloading", LoggerType.PairManagement);
        sundesmo.MarkForUnload();
    }

    /// <summary>
    ///     Marks a user pair as offline. This will clear their OnlineUserDTO. <para />
    ///     A Sundesmo's Chara* can still be valid while offline, and they can still download & apply changes.
    /// </summary>
    public void MarkSundesmoOffline(UserData user)
    {
        if (!_allSundesmos.TryGetValue(user, out var sundesmo))
            throw new InvalidOperationException($"No user found [{user.AliasOrUID}]");
        
        Logger.LogTrace($"Marked {sundesmo.PlayerName}({sundesmo.GetNickAliasOrUid()}) as offline", LoggerType.PairManagement);
        Mediator.Publish(new ClearProfileDataMessage(sundesmo.UserData));

        // Performs an immidiate revert if they were marked for a paused state.
        // How does this not cause race conditions with UserChangeUniqueSingle?
        //  - When called by the client. (The Client paused this Sundesmo), we set the new value before making the call.
        //  - When called by the sundesmo, the permission update is returned before the Offline call, so it will be set.
        sundesmo.MarkOffline(sundesmo.OwnPerms.PauseVisuals);
        RecreateLazy();
    }

    /// <summary>
    ///     Call to reapply all alterations to all sundesmos. (Used for post-cutscene)
    /// </summary>
    private void ReapplyAllRendered()
    {
        foreach (var pair in _allSundesmos.Values)
            pair.ReapplyAlterations();
    }

    private void TargetSundesmo(Sundesmo s)
    {
        if (PlayerData.InPvP || !s.IsRendered) return;
        unsafe
        {
            if (_folderConfig.Current.TargetWithFocus)
                TargetSystem.Instance()->FocusTarget = (GameObject*)s.PlayerAddress;
            else
                TargetSystem.Instance()->SetHardTarget((GameObject*)s.PlayerAddress);
        }
    }

    private void RecreateLazy()
    {
        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Mediator.Publish(new FolderUpdateSundesmos());
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
    ///     Gets the MoodlesTrusted sundesmos to share off moodle data to.
    /// </summary>
    public List<UserData> GetMoodleTrusted(IEnumerable<UserData> users) => users.Where(u => _allSundesmos.TryGetValue(u, out var s) && s.OwnPerms.ShareOwnMoodles).ToList();

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
        nickAliasUid = _nicks.GetNicknameForUid(uid) ?? _allSundesmos.Keys.FirstOrDefault(p => p.UID == uid)?.AliasOrUID;
        return !string.IsNullOrWhiteSpace(nickAliasUid);
    }

    public bool TryGetNickAliasOrUid(UserData user, [NotNullWhen(true)] out string? nickAliasUid)
    {
        nickAliasUid = _allSundesmos.TryGetValue(user, out var s) ? s.GetNickAliasOrUid() : null;
        return !string.IsNullOrWhiteSpace(nickAliasUid);
    }

    /// <summary>
    ///     Attempt to retrieve a sundesmo by <see cref="UserData"/>. If failed, null is returned.
    /// </summary>
    public Sundesmo? GetUserOrDefault(UserData user) => _allSundesmos.TryGetValue(user, out var sundesmo) ? sundesmo : null;

    #endregion Manager Helpers

    #region Updates    
    public void ReceiveMoodleData(UserData target, MoodleData newMoodleData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"Received moodle update for {sundesmo.GetNickAliasOrUid()}!", LoggerType.Callbacks);
        sundesmo.SetMoodleData(newMoodleData);
    }

    public void ReceiveMoodleStatuses(UserData target, List<MoodlesStatusInfo> newStatuses)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");
        Logger.LogTrace($"Received moodle status update for {sundesmo.GetNickAliasOrUid()}!", LoggerType.Callbacks);
        sundesmo.SharedData.SetStatuses(newStatuses);
    }

    public void ReceiveMoodlePresets(UserData target, List<MoodlePresetInfo> newPresets)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");
        Logger.LogTrace($"Received moodle preset update for {sundesmo.GetNickAliasOrUid()}!", LoggerType.Callbacks);
        sundesmo.SharedData.SetPresets(newPresets);
    }

    public void ReceiveMoodleStatusUpdate(UserData target, MoodlesStatusInfo status, bool deleted)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");
        Logger.LogTrace($"Received moodle status single update for {sundesmo.GetNickAliasOrUid()}!", LoggerType.Callbacks);
        
        if (deleted) sundesmo.SharedData.Statuses.Remove(status.GUID);
        else sundesmo.SharedData.TryUpdateStatus(status);
    }

    public void ReceiveMoodlePresetUpdate(UserData target, MoodlePresetInfo preset, bool deleted)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");
        Logger.LogTrace($"Received moodle preset single update for {sundesmo.GetNickAliasOrUid()}!", LoggerType.Callbacks);

        if (deleted) sundesmo.SharedData.Presets.Remove(preset.GUID);
        else sundesmo.SharedData.TryUpdatePreset(preset);
    }

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
    }

    public void PermChangeUnique(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        if (!PropertyChanger.TrySetProperty(sundesmo.PairPerms, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        // Inform of a permission change here for moodles!
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s PairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        // Post permission change logic.
        switch (permName)
        {
            // Could do pause state here to actually pause but instead just refresh profile.
            case nameof(PairPerms.PauseVisuals):
                Mediator.Publish(new ClearProfileDataMessage(target));
                break;

            case nameof(PairPerms.ShareOwnMoodles):
                // Clear Shared Info if cleared.
                if (!sundesmo.PairPerms.ShareOwnMoodles)
                {
                    sundesmo.SharedData.Statuses.Clear();
                    sundesmo.SharedData.Presets.Clear();
                }
                break;

            case nameof(PairPerms.MoodleAccess):
            case nameof(PairPerms.MaxMoodleTime):
                Mediator.Publish(new MoodlePermsChanged(sundesmo));
                break;
        }
        // Clear Moodles if share perms was turned off.
        if (permName == nameof(PairPerms.ShareOwnMoodles) && !sundesmo.PairPerms.ShareOwnMoodles)
        {
            sundesmo.SharedData.Statuses.Clear();
            sundesmo.SharedData.Presets.Clear();
        }
    }

    public void PermChangeUnique(UserData target, Dictionary<string, object> changes)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        foreach (var (permName, newValue) in changes)
        {
            if (!PropertyChanger.TrySetProperty(sundesmo.PairPerms, permName, newValue, out var finalVal) || finalVal is null)
                throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");
            // Inform of a permission change here for moodles!
            Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s PairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        }

        // Post permission change logic.
        if (changes.ContainsKey(nameof(PairPerms.PauseVisuals)))
        {
            Mediator.Publish(new ClearProfileDataMessage(target));
        }
        else if (changes.ContainsKey(nameof(PairPerms.ShareOwnMoodles)))
        {
            if (!sundesmo.PairPerms.ShareOwnMoodles)
            {
                sundesmo.SharedData.Statuses.Clear();
                sundesmo.SharedData.Presets.Clear();
            }
        }
        else if (changes.ContainsKey(nameof(PairPerms.MoodleAccess)) || changes.ContainsKey(nameof(PairPerms.MaxMoodleTime)))
        {
            Mediator.Publish(new MoodlePermsChanged(sundesmo));
        }
    }

    public void PermChangeUnique(UserData target, PairPerms newPerms)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // cache prev state and update them.
        var prevPerms = sundesmo.PairPerms with { };

        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s PairPerms updated in bulk.]", LoggerType.PairDataTransfer);

        // Post permission change logic.
        if (prevPerms.PauseVisuals != sundesmo.PairPerms.PauseVisuals)
        {
            Mediator.Publish(new ClearProfileDataMessage(target));
        }
        else if (prevPerms.ShareOwnMoodles != sundesmo.PairPerms.ShareOwnMoodles)
        {
            if (!sundesmo.PairPerms.ShareOwnMoodles)
            {
                sundesmo.SharedData.Statuses.Clear();
                sundesmo.SharedData.Presets.Clear();
            }
        }
        else if (prevPerms.MoodleAccess != sundesmo.PairPerms.MoodleAccess
            || prevPerms.MaxMoodleTime != sundesmo.PairPerms.MaxMoodleTime)
        {
            Mediator.Publish(new MoodlePermsChanged(sundesmo));
        }
    }

    #endregion Updates


    /// <summary>
    ///     Logic for ensuring that correct pairs display a context menu when right-clicked.
    /// </summary>
    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        Logger.LogInformation("Opening Pair Context Menu of type " + args.MenuType, LoggerType.PairManagement);
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
        subMenu.OnClicked += (args) => OpenSubMenu(match, args);
        args.AddMenuItem(subMenu);
    }

    /// <summary>
    ///     Required to show the nested menu in the opened context menus.
    /// </summary>
    private void OpenSubMenu(Sundesmo sundesmo, IMenuItemClickedArgs args)
    {
        args.OpenSubmenu("Sundouleia Options", [ new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Profile").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { Mediator.Publish(new ProfileOpenMessage(sundesmo.UserData)); },
        }, new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Permissions").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { Mediator.Publish(new OpenSundesmoSidePanel(sundesmo, true)); },
        }]);
    }
}
