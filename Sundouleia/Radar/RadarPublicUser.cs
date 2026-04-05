using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Base model representing a visible radar user. <para />
///     Holds data about their Character* and common visibility logic.
///     Concrete subclasses provide display/UID and filter behavior.
/// </summary>
public unsafe class RadarPublicUser : DisposableMediatorSubscriberBase
{
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;
    private readonly CharaWatcher _watcher;

    private Character* _player;
    private UserData _user;
    private Sundesmo? _sundesmo;

    // Find a way to phase out the manager if possible, perhaps with a function
    public RadarPublicUser(RadarMember radarUser, ILogger<RadarPublicUser> logger, SundouleiaMediator mediator,
        SundesmoManager sundesmos, RequestsManager requests, CharaWatcher watcher)
        : base(logger, mediator)
    {
        _sundesmos = sundesmos;
        _requests = requests;
        _watcher = watcher;

        _user = radarUser.User;
        HashedIdent = radarUser.HashedIdent;
        Flags = radarUser.Flags;
        _player = null;
        RefreshSundesmo();

        Mediator.Subscribe<WatchedObjectCreated>(this, _ => CheckMatchForAddr(_.Address));
        // Mediator.Subscribe<WatchedObjectDestroyed>(this, _ => MarkUnrendered(_.Address));
    }

    public string     HashedIdent { get; private set; }
    public RadarFlags Flags       { get; private set; }
    public bool       InRequests  { get; private set; } = false;

    public string UID         => _sundesmo?.UserData.UID        ?? _user.UID;
    public string AnonTag     => _sundesmo?.UserData.AnonTag    ?? _user.AnonTag;
    public string DisplayName => _sundesmo?.GetNickAliasOrUid() ?? _user.AnonName;

    public bool IsPaired        => _sundesmo is not null;
    public bool CanSendRequests => !IsPaired && !InRequests && HashedIdent.Length != 0;

    // Visibility.
    public bool   IsValid        => _player is not null;     
    public IntPtr Address        => (nint)_player;
    public ushort ObjIndex       => IsValid ? _player->ObjectIndex : ushort.MaxValue;
    public ulong  EntityId       => IsValid ? _player->EntityId : ulong.MaxValue;
    public ulong  PlayerObjectId => IsValid ? _player->GetGameObjectId().Id : ulong.MaxValue;

    private void CheckMatchForAddr(IntPtr address)
    {
        if (Address != IntPtr.Zero || string.IsNullOrEmpty(HashedIdent))
            return;
        // Must have valid CharaIdent.
        if (HashedIdent != SundouleiaSecurity.GetIdentHashByCharacterPtr(address))
            return;

        Logger.LogDebug($"Matched radar user to a created object @ [{address:X}]", LoggerType.PairHandler);
        _player = (Character*)address;
    }

    /// <summary>
    ///     Should be called after updates to the SundesmoManager 
    ///     and RequestsManager are made for accurate results.
    /// </summary>
    public void RefreshSundesmo()
    {
        _sundesmo = _sundesmos.GetUserOrDefault(_user);
        InRequests = _requests.ExistsFor(UID);
    }

    /// <summary>
    ///   Update the hashedId for this radar user. Determines visibility status.
    /// </summary>
    public void Update(RadarMember newData)
    {
        // Update flags regardless
        Flags = newData.Flags;
        // If the hashedIdent is string.Empty, clear the player pointer.
        if (string.IsNullOrEmpty(HashedIdent))
            _player = null;
    }

    /// <summary>
    ///   The User is now visible, bind the address to their visible character.
    /// </summary>
    public void UpdateVisibility(IntPtr address)
    {
        _player = (Character*)address;
    }

    // For simplifying a filter check against a radar user.
    public bool MatchesFilter(string filter)
    {
        if (filter.Length is 0)
            return true;

        if (_sundesmo is not null)
            return _sundesmo.UserData.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (_sundesmo.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || _sundesmo.UserData.UID.Contains(filter, StringComparison.OrdinalIgnoreCase);

        return _user.AnonName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}