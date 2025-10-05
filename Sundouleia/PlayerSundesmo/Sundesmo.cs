using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Sundouleia.Pairs.Factories;
using Sundouleia.Services;
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
    private readonly SundesmoHandlerFactory _factory;
    private readonly CharaObjectWatcher _watcher;

    // Associated Player Data (Created once online).
    private OnlineUser? OnlineUser;
    private PlayerHandler _player;
    private PlayerOwnedHandler _mountMinion;
    private PlayerOwnedHandler _pet;
    private PlayerOwnedHandler _companion;

    public Sundesmo(UserPair userPairInfo, ILogger<Sundesmo> logger, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, ServerConfigManager nicks, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _mediator = mediator;
        _nickConfig = nicks;
        _watcher = watcher;

        UserPair = userPairInfo;
        // Create handlers for each of the objects.
        _player = factory.Create(this);
        _mountMinion = factory.Create(OwnedObject.MinionOrMount, this);
        _pet = factory.Create(OwnedObject.Pet, this);
        _companion = factory.Create(OwnedObject.Companion, this);
    }

    public bool PlayerNull => _player == null;
    public bool MountMinionNull => _mountMinion == null;
    public bool PetNull => _pet == null;
    public bool CompanionNull => _companion == null;

    // Associated ServerData.
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
            OwnedObject.Player => _player?.Address ?? IntPtr.Zero,
            OwnedObject.MinionOrMount => _mountMinion?.Address ?? IntPtr.Zero,
            OwnedObject.Pet => _pet?.Address ?? IntPtr.Zero,
            OwnedObject.Companion => _companion?.Address ?? IntPtr.Zero,
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
        _player.ReapplyAlterations();
        _mountMinion.ReapplyAlterations();
        _pet.ReapplyAlterations();
        _companion.ReapplyAlterations();
    }

    // Tinker with async / no async later.
    public async void ApplyFullData(RecievedModUpdate newModData, VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null)
            _player.UpdateAndApplyFullData(newModData, newIpc.PlayerChanges);
        if (newIpc.MinionMountChanges != null)
            await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);
        if (newIpc.PetChanges != null)
            await _pet.ApplyIpcData(newIpc.PetChanges);
        if (newIpc.CompanionChanges != null)
            await _companion.ApplyIpcData(newIpc.CompanionChanges);
    }

    public async void ApplyModData(RecievedModUpdate newModData)
        => await _player.UpdateAndApplyModData(newModData);

    public async void ApplyIpcData(VisualUpdate newIpc)
    {
        if (newIpc.PlayerChanges != null)
            await _player.ApplyIpcData(newIpc.PlayerChanges);

        if (newIpc.MinionMountChanges != null)
            await _mountMinion.ApplyIpcData(newIpc.MinionMountChanges);

        if (newIpc.PetChanges != null)
            await _pet.ApplyIpcData(newIpc.PetChanges);

        if (newIpc.CompanionChanges != null)
            await _companion.ApplyIpcData(newIpc.CompanionChanges);
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
    public void MarkOnline(OnlineUser dto)
    {
        OnlineUser = dto;
        _mediator.Publish(new SundesmoOnline(this, PlayerRendered));
        // if they are rendered we should halt any timeouts occuring.
        if (PlayerRendered) _player.StopTimeoutTask();
        if (MountMinionRendered) _mountMinion.StopTimeoutTask();
        if (PetRendered) _pet.StopTimeoutTask();
        if (CompanionRendered) _companion.StopTimeoutTask();
        // Now that we have the ident we should run a check against all of the handlers.
        _watcher.CheckForExisting(_player);
        _watcher.CheckForExisting(_mountMinion);
        _watcher.CheckForExisting(_pet);
        _watcher.CheckForExisting(_companion);
        // if anything is rendered and has alterations, reapply them.
        if (PlayerRendered) ReapplyAlterations();
    }

    /// <summary>
    ///     Marks the pair as offline.
    /// </summary>
    public void MarkOffline()
    {
        OnlineUser = null;
        _player.StartTimeoutTask();
        _mountMinion.StartTimeoutTask();
        _pet.StartTimeoutTask();
        _companion.StartTimeoutTask();
        _mediator.Publish(new SundesmoOffline(this));
    }

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
