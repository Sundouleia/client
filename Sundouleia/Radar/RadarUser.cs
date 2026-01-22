using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Base model representing a visible radar user. <para />
///     Holds data about their Character* and common visibility logic.
///     Concrete subclasses provide display/UID and filter behavior.
/// </summary>
public unsafe class RadarUser
{
    private readonly SundesmoManager _sundesmos;

    private Character* _player;
    private UserData _user;
    private Sundesmo? _sundesmo;

    // Find a way to phase out the manager if possible, perhaps with a function
    public RadarUser(SundesmoManager manager, OnlineUser onlineUser, IntPtr address)
    {
        _sundesmos = manager;
        _user = onlineUser.User;
        HashedIdent = onlineUser.Ident;
        _player = address != IntPtr.Zero ? (Character*)address : null;
        RefreshSundesmo();
    }

    public string HashedIdent { get; private set; }

    public string UID         => _sundesmo?.UserData.UID        ?? _user.UID;
    public string AnonTag     => _sundesmo?.UserData.AnonTag    ?? _user.AnonTag;
    public string DisplayName => _sundesmo?.GetNickAliasOrUid() ?? _user.AnonName;

    public bool IsPaired        => _sundesmo is not null;
    public bool CanSendRequests => !IsPaired && HashedIdent.Length != 0;

    // Visibility.
    public bool   IsValid        => _player is not null;     
    public IntPtr Address        => (nint)_player;
    public ushort ObjIndex       => IsValid ? _player->ObjectIndex : ushort.MaxValue;
    public ulong  EntityId       => IsValid ? _player->EntityId : ulong.MaxValue;
    public ulong  PlayerObjectId => IsValid ? _player->GetGameObjectId().Id : ulong.MaxValue;

    public void RefreshSundesmo()
        => _sundesmo = _sundesmos.GetUserOrDefault(_user);

    /// <summary>
    ///     Update the hashedId for this radar user. Determines visibility status.
    /// </summary>
    public void UpdateOnlineUser(OnlineUser newState)
    {
        HashedIdent = newState.Ident;
        // If the hashedIdent is string.Empty, clear the player pointer.
        if (string.IsNullOrEmpty(HashedIdent))
            _player = null;
    }

    /// <summary>
    ///     The User is now visible, bind the address to their visible character.
    /// </summary>
    public void UpdateVisibility(IntPtr address)
    {
        if (address == IntPtr.Zero)
            return;
        _player = (Character*)address;
    }

    // For simplifying a filter check against a radar user.
    public bool MatchesFilter(string filter)
    {
        if (filter.Length is 0)
            return true;

        if (_sundesmo is not null)
            return _sundesmo.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (_sundesmo.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

        return _user.AnonName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}