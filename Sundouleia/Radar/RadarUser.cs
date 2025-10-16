using FFXIVClientStructs.FFXIV.Client.Game.Character;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Model representing a valid visible radar user. <para />
///     Holds data about their Character* on top of the OnlineUser.
/// </summary>
public unsafe class RadarUser
{
    private UserData _user;
    private Character* _player;

    public RadarUser(OnlineUser user, IntPtr address)
    {
        _user = user.User;
        HashedIdent = user.Ident;
        _player = address != IntPtr.Zero ? (Character*)address : null;
    }

    public string AnonymousName => _user.AnonName;
    public string AliasOrUID => _user.AliasOrUID;
    public string UID => _user.UID;
    public string HashedIdent { get; private set; } = string.Empty;

    public bool IsValid => _player is not null;
    public string PlayerName => IsValid ? _player->NameString : string.Empty; // Maybe remove.
   
    // All of the below only works if valid.
    internal Character PlayerState { get { unsafe { return *_player; } } } // Maybe remove.
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public unsafe ulong EntityId => _player->EntityId;
    public unsafe ulong GameObjectId => _player->GetGameObjectId().ObjectId;

    /// <summary>
    ///     Update the hashedId for this radar user. It will 
    ///     determine resulting visibility status.
    /// </summary>
    public void UpdateState(OnlineUser newState)
    {
        HashedIdent = newState.Ident;
        // If the hashedIdent is string.Empty, clear the player pointer.
        if (string.IsNullOrEmpty(HashedIdent))
            _player = null;
    }

    /// <summary>
    ///     The User is now visible, bind the address to their visible character.
    /// </summary>
    public void BindToAddress(IntPtr address)
    {
        if (address == IntPtr.Zero)
            return;
        _player = (Character*)address;
    }
}