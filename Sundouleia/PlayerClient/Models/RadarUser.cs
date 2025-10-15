using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using SundouleiaAPI.Network;

namespace Sundouleia.PlayerClient;

/// <summary>
///     Model representing a valid visible radar user. <para />
///     Holds data about their Character* ontop of the OnlineUser.
/// </summary>
public unsafe class RadarUser
{
    private OnlineUser _user;
    private Character* _player;

    public RadarUser(RadarUserInfo user, IntPtr address)
    {
        _user = user.OnlineUser;
        IsChatter = user.State.UseChat;
        HashedIdent = user.State.HashedCID;
        _player = address != IntPtr.Zero ? (Character*)address : null;
    }

    public string AnonymousName => _user.User.AnonName;
    public string AliasOrUID => _user.User.AliasOrUID;
    public string UID => _user.User.UID;
    public bool IsChatter { get; private set; } = false;
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
    ///     Properly update the state of a radar user, and their preferences. <para />
    ///     If the Ident becomes string.Empty, clear visibility.
    /// </summary>
    public void UpdateState(RadarState newState)
    {
        IsChatter = newState.UseChat;
        HashedIdent = newState.HashedCID;
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