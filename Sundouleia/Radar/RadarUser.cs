using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Model representing a valid visible radar user. <para />
///     Holds data about their Character* on top of the OnlineUser.
/// </summary>
public unsafe class RadarUser
{
    // Used for retrieveing the display name.
    private readonly SundesmoManager _sundesmos;

    private UserData _user;
    private Character* _player;

    public RadarUser(SundesmoManager sundesmos, OnlineUser user, IntPtr address)
    {
        _sundesmos = sundesmos;
        _user = user.User;
        HashedIdent = user.Ident;
        _player = address != IntPtr.Zero ? (Character*)address : null;
    }

    public string UID => _user.UID;
    public string AnonymousName => _user.AnonName;
    public string PlayerName => IsValid ? _player->NameString : string.Empty; // Maybe remove.
    public string HashedIdent { get; private set; } = string.Empty;

    public bool IsValid => _player is not null;
    public bool CanSendRequest => !string.IsNullOrEmpty(HashedIdent);
    public bool IsPair => _sundesmos.GetUserOrDefault(_user) is not null;

    // All of the below only works if valid.
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public unsafe ulong EntityId => _player->EntityId;
    public unsafe ulong PlayerObjectId => _player->GetGameObjectId().Id;

    /// <summary>
    ///     Update the hashedId for this radar user. It will 
    ///     determine resulting visibility status.
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

    public bool MatchesFilter(string filter)
    {
        if (filter.Length is 0)
            return true;

        Sundesmo? pair = _sundesmos.GetUserOrDefault(_user);
        if (pair is not null)
        {
            return pair.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (pair.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (pair.PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

        }
        else
        {
            return AnonymousName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }
    }
}