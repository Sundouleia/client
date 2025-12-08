using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Model representing a valid visible radar user. <para />
///     Holds data about their Character* on top of the OnlineUser.
/// </summary>
//public unsafe class RadarUser
//{
//    // Used for retrieving the display name.
//    private readonly SundesmoManager _sundesmos;

//    private UserData _user;
//    private Character* _player;

//    public RadarUser(SundesmoManager sundesmos, OnlineUser user, IntPtr address)
//    {
//        _sundesmos = sundesmos;
//        _user = user.User;
//        HashedIdent = user.Ident;
//        _player = address != IntPtr.Zero ? (Character*)address : null;
//    }

//    public string UID => _user.UID;
//    public string AnonymousName => _user.AnonName;
//    public string DisplayName => _sundesmos.TryGetNickAliasOrUid(_user, out var name) ? name : AnonymousName;
//    public string PlayerName => IsValid ? _player->NameString : string.Empty; // Maybe remove.
//    public string HashedIdent { get; private set; } = string.Empty;

//    public bool IsValid => _player is not null;
//    public bool CanSendRequest => !string.IsNullOrEmpty(HashedIdent);
//    public bool IsPair => _sundesmos.GetUserOrDefault(_user) is not null;

//    // All of the below only works if valid.
//    public unsafe IntPtr Address => (nint)_player;
//    public unsafe ushort ObjIndex => _player->ObjectIndex;
//    public unsafe ulong EntityId => _player->EntityId;
//    public unsafe ulong PlayerObjectId => _player->GetGameObjectId().Id;

//    /// <summary>
//    ///     Update the hashedId for this radar user. It will 
//    ///     determine resulting visibility status.
//    /// </summary>
//    public void UpdateOnlineUser(OnlineUser newState)
//    {
//        HashedIdent = newState.Ident;
//        // If the hashedIdent is string.Empty, clear the player pointer.
//        if (string.IsNullOrEmpty(HashedIdent))
//            _player = null;
//    }

//    /// <summary>
//    ///     The User is now visible, bind the address to their visible character.
//    /// </summary>
//    public void UpdateVisibility(IntPtr address)
//    {
//        if (address == IntPtr.Zero)
//            return;
//        _player = (Character*)address;
//    }

//    public bool MatchesFilter(string filter)
//    {
//        if (filter.Length is 0)
//            return true;

//        Sundesmo? pair = _sundesmos.GetUserOrDefault(_user);
//        if (pair is not null)
//        {
//            return pair.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
//                || (pair.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
//                || (pair.PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

//        }
//        else
//        {
//            return AnonymousName.Contains(filter, StringComparison.OrdinalIgnoreCase);
//        }
//    }
//}

/// <summary>
///     Base model representing a visible radar user. <para />
///     Holds data about their Character* and common visibility logic.
///     Concrete subclasses provide display/UID and filter behavior.
/// </summary>
public unsafe abstract class RadarUser
{
    protected Character* _player;

    // Remember that the hashed Ident is dependent on radar user setting, not pair setting.
    // Someone can be paired and have their ident hidden, even if in the sundesmo.
    protected RadarUser(string hashedIdent, IntPtr address)
    {
        HashedIdent = hashedIdent;
        _player = address != IntPtr.Zero ? (Character*)address : null;
    }

    public string HashedIdent { get; protected set; }

    // Used for comparisons and the like. 
    public abstract string UID { get; }
    public abstract string AnonTag { get; }
    public abstract string DisplayName { get; }

    public bool IsValid => _player is not null;
    public virtual bool CanSendRequests => HashedIdent.Length is not 0;
     

    // All of the below only works if valid.
    public IntPtr Address        => (nint)_player;
    public ushort ObjIndex       => _player->ObjectIndex;
    public ulong  EntityId       => _player->EntityId;
    public ulong  PlayerObjectId => _player->GetGameObjectId().Id;

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

    // For simplifying a filter check against a radar user.
    public abstract bool MatchesFilter(string filter);
}

/// <summary>
///     Model representing a valid visible radar user. <para />
///     Holds data about their Character* on top of the OnlineUser.
/// </summary>
public sealed class PairedRadarUser : RadarUser
{
    private Sundesmo _sundesmo;
    public unsafe PairedRadarUser(Sundesmo sundesmo, OnlineUser identState, IntPtr address)
        : base(identState.Ident, address)
    {
        _sundesmo = sundesmo;
    }

    // Primarily for debugging.
    public override string UID => _sundesmo.UserData.UID;
    public override string AnonTag => _sundesmo.UserData.AnonTag;
    public override string DisplayName => _sundesmo.GetNickAliasOrUid();
    public override bool CanSendRequests => false;

    public override bool MatchesFilter(string filter)
        => filter.Length is 0 ? true
        : _sundesmo.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase)
          || (_sundesmo.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>
///     Concrete radar user for unpaired / anonymous users. Keeps base/default behaviour,
///     but explicitly marks IsPair = false and provides a focused MatchesFilter.
/// </summary>
public sealed class UnpairedRadarUser : RadarUser
{
    private readonly RequestsManager _requests;
    private UserData _user;
    public UnpairedRadarUser(RequestsManager requests, OnlineUser radarUser, IntPtr address)
        : base(radarUser.Ident, address)
    {
        _requests = requests;
        _user = radarUser.User;
    }

    public override string UID => _user.UID;
    public override bool CanSendRequests => true;
    public override string AnonTag => _user.AnonTag;
    public override string DisplayName => _user.AnonName;
    public override bool MatchesFilter(string filter)
        => filter.Length is 0 ? true : _user.AnonName.Contains(filter, StringComparison.OrdinalIgnoreCase);
}