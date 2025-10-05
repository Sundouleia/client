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
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Pairs;

/// <summary>
///     Manager for paired connections. <para />
///     This also applies for temporary pairs.
/// </summary>
public sealed partial class SundesmoManager : DisposableMediatorSubscriberBase
{
    // concurrent dictionary of all paired paired to the client.
    private readonly ConcurrentDictionary<UserData, Sundesmo> _allSundesmos;
    private readonly MainConfig _mainConfig;
    private readonly ServerConfigManager _serverConfigs;
    private readonly SundesmoFactory _pairFactory;

    private CancellationTokenSource _disconnectTimeoutCTS = new();
    private Task? _disconnectTimeoutTask = null;


    private Lazy<List<Sundesmo>> _directPairsInternal;  // the internal direct pairs lazy list for optimization
    public List<Sundesmo> DirectPairs => _directPairsInternal.Value; // the direct pairs the client has with other users.

    public SundesmoManager(ILogger<SundesmoManager> logger, SundouleiaMediator mediator,
        SundesmoFactory factory, MainConfig config, ServerConfigManager serverConfigs) : base(logger, mediator)
    {
        _allSundesmos = new(UserDataComparer.Instance);
        _pairFactory = factory;
        _mainConfig = config;
        _serverConfigs = serverConfigs;

        Mediator.Subscribe<ConnectedMessage>(this, _ => _disconnectTimeoutCTS.SafeCancel());
        Mediator.Subscribe<ReconnectedMessage>(this, _ => _disconnectTimeoutCTS.SafeCancel());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => OnClientDisconnected());
        Mediator.Subscribe<CutsceneEndMessage>(this, _ => ReapplyAlterations());

        Mediator.Subscribe<TargetSundesmoMessage>(this, (msg) =>
        {
            // Fail in pvp or when not rendered.
            if (PlayerData.IsInPvP || !msg.Sundesmo.PlayerRendered)
                return;
            unsafe
            {
                if (config.Current.FocusTargetOverTarget)
                    TargetSystem.Instance()->FocusTarget = (GameObject*)msg.Sundesmo.GetAddress(OwnedObject.Player);
                else
                    TargetSystem.Instance()->SetHardTarget((GameObject*)msg.Sundesmo.GetAddress(OwnedObject.Player));
            }
        });


        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Svc.ContextMenu.OnMenuOpened += OnOpenContextMenu;
    }

    private void OnOpenContextMenu(IMenuOpenedArgs args)
    {
        Logger.LogInformation("Opening Pair Context Menu of type "+args.MenuType, LoggerType.PairManagement);
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_mainConfig.Current.ShowContextMenus) return;
        if (args.Target is not MenuTargetDefault target || target.TargetObjectId == 0) return;
        // Find the sundesmo that matches this and display the results.
        if (DirectPairs.FirstOrDefault(p => p.PlayerRendered && p.PlayerObjectId == target.TargetObjectId && !p.IsPaused) is not { } match)
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
        _disconnectTimeoutCTS.SafeCancelDispose();
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

    /// <summary>
    ///     Whenever the client disconnects from the server we should run this function. <para />
    ///     This will assign a timeout task to wait for 15s before disposing of all handled data. <para />
    ///     Should the client reconnect before timeout, cancel the await and keep all data intext. <para />
    ///     Once the delay expires, run a disposal of all data.
    /// </summary>
    public void OnClientDisconnected()
    {
        Logger.LogInformation("Client disconnected, starting disconnect timeout.", LoggerType.PairManagement);
        _disconnectTimeoutCTS = _disconnectTimeoutCTS.SafeCancelRecreate();
        // Mark all sundesmos offline immidiately.
        Parallel.ForEach(_allSundesmos, s => s.Value.MarkOffline());
        RecreateLazy();
        // assign the delayed task.
        _disconnectTimeoutTask =  Task.Run(async () =>
        {
            try
            {
                // Await 15s for timeout, ensuring that any data that would be reapplied is reapplied.
                await Task.Delay(TimeSpan.FromSeconds(15), _disconnectTimeoutCTS.Token).ConfigureAwait(false);
                Logger.LogInformation("ClientDC timeout elapsed, disposing all sundesmos.", LoggerType.PairManagement);
                Parallel.ForEach(_allSundesmos, s => s.Value.DisposeData());
                _allSundesmos.Clear();
                RecreateLazy();
            }
            catch (OperationCanceledException)
            {
                Logger.LogInformation("ClientDC timeout cancelled upon reconnection.", LoggerType.PairManagement);
            }
        }, _disconnectTimeoutCTS.Token);
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
    ///     Mark a stored sundesmo as online, applying their <see cref="OnlineUser"/> DTO.
    /// </summary>
    public void MarkSundesmoOnline(OnlineUser dto, bool notify = true)
    {
        if (!_allSundesmos.TryGetValue(dto.User, out var sundesmo))
            throw new InvalidOperationException($"No user found [{dto}]");
        // Refresh their profile data.
        Mediator.Publish(new ClearProfileDataMessage(dto.User));
        if (sundesmo.IsOnline)
        {
            Logger.LogWarning($"Pair [{dto.User.AliasOrUID}] is already marked online. Recreating direct pairs.", LoggerType.PairManagement);
            RecreateLazy();
            return;
        }

        // Init the proper first-time online message.
        if (notify && _mainConfig.Current.NotifyForOnlinePairs && (_mainConfig.Current.NotifyLimitToNickedPairs && !string.IsNullOrEmpty(sundesmo.GetNickname())))
        {
            var nick = sundesmo.GetNickname();
            var msg = !string.IsNullOrEmpty(sundesmo.GetNickname())
                ? $"{nick} ({dto.User.AliasOrUID}) is now online" : $"{dto.User.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User Online", msg, NotificationType.Info, TimeSpan.FromSeconds(2)));
        }

        Logger.LogTrace($"Marked {sundesmo.PlayerName}({sundesmo.GetNickAliasOrUid()}) as online", LoggerType.PairManagement);
        sundesmo.MarkOnline(dto);
        RecreateLazy();
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
        }
        RecreateLazy();
    }

    private void ReapplyAlterations()
    {
        foreach (var pair in _allSundesmos.Values)
            pair.ReapplyAlterations();
    }

    private void RecreateLazy()
    {
        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Mediator.Publish(new RefreshWhitelistMessage());
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
    public int GetVisibleCount() => _allSundesmos.Count(p => p.Value.PlayerRendered);

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos. <para />
    ///     <b>NOTE: It is possible for a visible sundesmos to be offline!</b>
    /// </summary>
    public List<UserData> GetVisible() => _allSundesmos.Where(p => p.Value.PlayerRendered).Select(p => p.Key).ToList();

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos that are connected.
    /// </summary>
    public List<UserData> GetVisibleConnected() => _allSundesmos.Where(p => p.Value.PlayerRendered && p.Value.IsOnline).Select(p => p.Key).ToList();

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
}
