using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Sundouleia.Pairs.Factories;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs;

/// <summary>
///     Stores information about a pairing (Sundesmo) between 2 users. <para />
///     Created via the SundesmoFactory
/// </summary>
public class Sundesmo : IComparable<Sundesmo>
{
    private readonly ILogger<Sundesmo> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly ServerConfigManager _nickConfig;

    // Associated Player Data
    private PlayerHandler _player;
    private PlayerOwnedHandler _mountMinion;
    private PlayerOwnedHandler _pet;
    private PlayerOwnedHandler _companion;

    public Sundesmo(UserPair userPairInfo, ILogger<Sundesmo> logger, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, ServerConfigManager nicks)
    {
        _logger = logger;
        _mediator = mediator;
        _nickConfig = nicks;

        UserPair = userPairInfo;
        _player = factory.Create(this);
        _mountMinion = factory.Create(OwnedObject.MinionOrMount, this);
        _pet = factory.Create(OwnedObject.Pet, this);
        _companion = factory.Create(OwnedObject.Companion, this);
    }

    // Associated ServerData.
    private OnlineUser? OnlineUser; // Dictates if the sundesmo is online (connected).
    public UserPair UserPair { get; init; }
    public UserData UserData => UserPair.User;
    public PairPerms OwnPerms => UserPair.OwnPerms;
    public GlobalPerms PairGlobals => UserPair.Globals;
    public PairPerms PairPerms => UserPair.Perms;

    // Internal Helpers
    public bool IsTemporary => UserPair.IsTemp;
    public bool IsOnline => OnlineUser != null;
    public bool IsPaused => OwnPerms.PauseVisuals;
    public string Ident => OnlineUser?.Ident ?? string.Empty;
    public string PlayerName => _player.PlayerName;
    public ulong PlayerEntityId => _player.EntityId;
    public ulong PlayerObjectId => _player.GameObjectId;
    public bool PlayerRendered => _player.IsRendered;
    public bool MountMinionRendered => _mountMinion.IsRendered;
    public bool PetRendered => _pet.IsRendered;
    public bool CompanionRendered => _companion.IsRendered;

    // Comparable helper, allows us to do faster lookup.
    public int CompareTo(Sundesmo? other)
    {
        if (other is null) return 1;
        return string.Compare(UserData.UID, other.UserData.UID, StringComparison.Ordinal);
    }

    public IntPtr GetAddress(OwnedObject obj)
        => obj switch
        {
            OwnedObject.Player => _player.Address,
            OwnedObject.MinionOrMount => _mountMinion.Address,
            OwnedObject.Pet => _pet.Address,
            OwnedObject.Companion => _companion.Address,
            _ => IntPtr.Zero,
        };

    public void OpenSundouleiaSubMenu(IMenuItemClickedArgs args)
    {
        args.OpenSubmenu("Sundouleia Options", [ new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Profile").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { _mediator.Publish(new ProfileOpenMessage(UserData)); },
        }, new MenuItem()
        {
            Name = new SeStringBuilder().AddText("Open Permissions").Build(),
            PrefixChar = 'S',
            PrefixColor = 708,
            OnClicked = (a) => { _mediator.Publish(new TogglePermissionWindow(this)); },
        }]);
    }

    public string? GetNickname() => _nickConfig.GetNicknameForUid(UserData.UID);
    public string GetNickAliasOrUid() => GetNickname() ?? UserData.AliasOrUID;

    public void ReapplyAlterations()
    {
        if (PlayerRendered)
            _player.ReapplyAlterations();
        if (MountMinionRendered)
            _mountMinion.ReapplyAlterations();
        if (PetRendered)
            _pet.ReapplyAlterations();
        if (CompanionRendered)
            _companion.ReapplyAlterations();
    }

    // Tinker with async / no async later.
    public async void ApplyFullData(RecievedModUpdate newModData, VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null) _player.UpdateAndApplyFullData(newModData, newIpc.PlayerChanges);
        if (newIpc.MinionMountChanges != null) await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);
        if (newIpc.PetChanges != null) await _pet.ApplyIpcData(newIpc.PetChanges);
        if (newIpc.CompanionChanges != null) await _companion.ApplyIpcData(newIpc.CompanionChanges);
    }

    public async void ApplyModData(RecievedModUpdate newModData)
        => await _player.UpdateAndApplyModData(newModData);

    public async void ApplyIpcData(VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null) await _player.ApplyIpcData(newIpc.PlayerChanges);
        if (newIpc.MinionMountChanges != null) await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);
        if (newIpc.PetChanges != null) await _pet.ApplyIpcData(newIpc.PetChanges);
        if (newIpc.CompanionChanges != null) await _companion.ApplyIpcData(newIpc.CompanionChanges);
    }

    public async void ApplyIpcSingle(OwnedObject obj, IpcKind kind, string newData)
    {
        await (obj switch
        {
            OwnedObject.Player => _player.ApplyIpcSingle(kind, newData),
            OwnedObject.MinionOrMount => _mountMinion.ApplyIpcSingle(kind, newData),
            OwnedObject.Pet => _pet.ApplyIpcSingle(kind, newData),
            OwnedObject.Companion => _companion.ApplyIpcSingle(kind, newData),
            _ => Task.CompletedTask,
        }).ConfigureAwait(false);
    }


    /// <summary>
    ///     Marks the sundesmo as online, providing us with their UID and CharaIdent.
    /// </summary>
    public void MarkOnline(OnlineUser dto) => OnlineUser = dto;

    /// <summary>
    ///     Marks the pair as offline.
    /// </summary>
    public void MarkOffline() => OnlineUser = null;

    /// <summary>
    ///     Removes all applied appearance data for the sundesmo if rendered, 
    ///     and disposes all internal data.
    /// </summary>
    public void DisposeData()
    {
        _logger.LogDebug($"Disposing data for [{PlayerName}] ({GetNickAliasOrUid()})", UserData.AliasOrUID);
        _player.Dispose();
        _mountMinion.Dispose();
        _pet.Dispose();
        _companion.Dispose();
    }
}
