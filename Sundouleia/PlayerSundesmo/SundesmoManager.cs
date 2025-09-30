using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Utils;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.Windows;

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
    
    private Lazy<List<Sundesmo>> _directPairsInternal;                          // the internal direct pairs lazy list for optimization
    public List<Sundesmo> DirectPairs => _directPairsInternal.Value;            // the direct pairs the client has with other users.

    public SundesmoManager(ILogger<SundesmoManager> logger, SundouleiaMediator mediator,
        SundesmoFactory factory, MainConfig config, ServerConfigManager serverConfigs) 
        : base(logger, mediator)
    {
        _allSundesmos = new(UserDataComparer.Instance);
        _pairFactory = factory;
        _mainConfig = config;
        _serverConfigs = serverConfigs;

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearSundesmos());
        // See why we need to worry about this later.
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) =>
        {
            foreach (var pair in _allSundesmos.Select(k => k.Value))
                pair.ReapplyLatestData();
        });

        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Svc.ContextMenu.OnMenuOpened += OnOpenContextMenu;
    }

    private void OnOpenContextMenu(IMenuOpenedArgs args)
    {
        Logger.LogInformation("Opening Pair Context Menu of type "+args.MenuType, LoggerType.DtrBar);
        if (args.MenuType is ContextMenuType.Inventory) return;
        if (!_mainConfig.Current.ShowContextMenus) return;
        // otherwise, locate the pair and add the context menu args to the visible pairs.
        foreach (var pair in _allSundesmos.Where((p => p.Value.IsVisible)))
            pair.Value.AddContextMenu(args);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ContextMenu.OnMenuOpened -= OnOpenContextMenu;
        DisposePairs();
    }

    public void AddSundesmo(UserPair dto)
    {
        var exists = _allSundesmos.ContainsKey(dto.User);
        var msg = $"User ({dto.User.UID}) {(exists ? "found, applying latest!" : "not found. Creating!")}.";
        Logger.LogDebug(msg, LoggerType.PairManagement);
        if (exists) 
            _allSundesmos[dto.User].ReapplyLatestData();
        else
            _allSundesmos[dto.User] = _pairFactory.Create(dto);
        RecreateLazy();
    }

    public void AddSundesmos(IEnumerable<UserPair> list)
    {
        var created = new List<string>();
        var refreshed = new List<string>();
        foreach (var dto in list)
            if (!_allSundesmos.ContainsKey(dto.User))
            {
                _allSundesmos[dto.User] = _pairFactory.Create(dto);
                created.Add(dto.User.UID);
            }
            else
            {
                _allSundesmos[dto.User].ReapplyLatestData();
                refreshed.Add(dto.User.UID);
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

    public void ClearSundesmos()
    {
        Logger.LogDebug("Clearing all Pairs", LoggerType.PairManagement);
        DisposePairs();
        _allSundesmos.Clear();
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
        Parallel.ForEach(_allSundesmos, item =>
        {
            item.Value.MarkOffline();
            item.Value.DisposeData();
        });
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
            Logger.LogDebug($"Pair [{dto.User.AliasOrUID}] is already marked online. Recreating direct pairs.", LoggerType.PairManagement);
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

        sundesmo.MarkOnline(dto);
        // If the sundesmo is not yet rendered, run a check against the watched objects.
        if (!sundesmo.IsRendered)
            sundesmo.CheckForCharacter();
        Mediator.Publish(new PairWentOnlineMessage(dto.User));
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
            Logger.LogTrace($"Marked {pair.GetNickAliasOrUid()} as offline", LoggerType.PairManagement);
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            pair.MarkOffline();
        }
        RecreateLazy();
    }

    /// <summary>
    ///     Called by the CharaObjectWatcher, and is what indicates when we become visible or not. <para />
    ///     Triggers reapplication of existing sent data, and marks them as visible for us to send data to.
    /// </summary>
    public unsafe void CharaEnteredRenderRange(Character* chara)
    {
        // Grab the hashed ident from the Character to indicate the potential OnlineUserIdent.
        var hash = SundouleiaSecurity.GetIdentHashByCharacterPtr((nint)chara);
        // Now we should iterate through all of our sundesmos. If one of them is online with this ident, pass it in.
        if (_allSundesmos.Values.FirstOrDefault(s => s.Ident == hash) is { } match)
            match.PlayerEnteredRender(chara);

    }

    /// <summary>
    ///     Called by the CharaObjectWatcher, and sends the player into a timeout state. <para />
    ///     In Timeout state, we know the sundesmo is still online, but just not visible. <para />
    ///     As such we can continue to send any data in transit, and track when they last left render. <para />
    ///     Upon re-entering render range, only send the data if the timeout awaiter elapsed.
    /// </summary>
    public unsafe void CharaLeftRenderRange(Character* chara)
    {

    }


    private void RecreateLazy()
    {
        _directPairsInternal = new Lazy<List<Sundesmo>>(() => _allSundesmos.Select(k => k.Value).ToList());
        Mediator.Publish(new RefreshUiMessage());
    }

    #region Manager Helpers
    /// <summary>
    ///     Sundesmos that we have an OnlineUser DTO of, implying they are connected.
    /// </summary>
    public List<Sundesmo> GetOnlineSundesmos() => _allSundesmos.Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();

    /// <summary>
    ///     Sundesmos that we have an OnlineUser DTO of, implying they are connected.
    /// </summary>
    public List<UserData> GetOnlineUserDatas() => _allSundesmos.Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Key).ToList();

    /// <summary>
    ///     The number of sundesmos that are in our render range. <para />
    ///     NOTE: This does not mean that they have applied data!
    /// </summary>
    public int GetVisibleCount() => _allSundesmos.Count(p => p.Value.IsVisible);

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos. <para />
    ///     <b>NOTE: It is possible for a visible sundesmos to be offline!</b>
    /// </summary>
    public List<UserData> GetVisible() => _allSundesmos.Where(p => p.Value.IsVisible).Select(p => p.Key).ToList();

    /// <summary>
    ///     Get the <see cref="UserData"/> for all rendered sundesmos that are connected.
    /// </summary>
    public List<UserData> GetVisibleConnected() => _allSundesmos.Where(p => p.Value.IsVisible && p.Value.IsOnline).Select(p => p.Key).ToList();

    /// <summary>
    ///     (Maybe restructure this later) Fetch all visible pair game objects. (Try and remove if possible, i hate this)
    /// </summary>
    public List<IGameObject> GetVisibleGameObjects() => _allSundesmos.Select(p => p.Value.VisiblePairGameObject).Where(gameObject => gameObject != null).ToList()!;

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
