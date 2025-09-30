using CkCommons;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Sundouleia.Pairs.Factories;
using Sundouleia.Pairs.Handlers;
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
    private readonly SemaphoreSlim _creationSemaphore = new(1);
    private readonly ServerConfigManager _nickConfig;

    private CancellationTokenSource _appearanceCTS = new CancellationTokenSource();
    private CancellationTokenSource _moodlesCTS = new CancellationTokenSource();

    public Sundesmo(UserPair userPairInfo, ILogger<Sundesmo> logger, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, ServerConfigManager nicks)
    {
        _logger = logger;
        _mediator = mediator;
        _nickConfig = nicks;

        UserPair = userPairInfo;
        Handler = factory.Create(this);
    }

    // Associated ServerData.
    private OnlineUser? OnlineUserIdent; // Dictates if the sundesmo is online (connected).
    private SundesmoHandler Handler { get; init; } // Dictates how changes to our sundesmo are handled.

    public UserPair UserPair { get; init; }
    public UserData UserData => UserPair.User;
    public PairPerms OwnPerms => UserPair.OwnPerms;
    public GlobalPerms PairGlobals => UserPair.Globals;
    public PairPerms PairPerms => UserPair.Perms;

    // Internal Helpers (Revamped)
    public bool IsTemporary => UserPair.IsTemp;
    public bool IsOnline => OnlineUserIdent != null;
    public bool IsPaused => OwnPerms.PauseVisuals;
    public bool IsRendered => false;
    public string Ident => OnlineUserIdent?.Ident ?? string.Empty;
    public string SundesmoName => Handler.PlayerName ?? UserData.AliasOrUID ?? string.Empty;



    // Internal Helpers. (Should renovate later)
    public bool HasCachedPlayer => !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _OnlineUser != null;
    public bool IsVisible => Handler.IsVisible ?? false;
    public IGameObject? VisiblePairGameObject => IsVisible ? (Handler.PairObject ?? null) : null;

    // Comparable helper, allows us to do faster lookup.
    public int CompareTo(Sundesmo? other)
    {
        if (other is null) return 1;
        return string.Compare(UserData.UID, other.UserData.UID, StringComparison.Ordinal);
    }

    // Internal context menu to display for active pairs.
    public void AddContextMenu(IMenuOpenedArgs args)
    {
        // if the visible player is not cached, not our target, or not a valid object, or paused, don't display.
        if (CachedPlayer == null || (args.Target is not MenuTargetDefault target) || target.TargetObjectId != VisiblePairGameObject?.GameObjectId || IsPaused) return;

        _logger.LogDebug("Adding Context Menu for " + UserData.UID, LoggerType.DtrBar);
        // This only works when you create it prior to adding it to the args,
        // otherwise the += has trouble calling. (it would fall out of scope)
        var subMenu = new MenuItem();
        subMenu.IsSubmenu = true;
        subMenu.Name = "Sundouleia";
        subMenu.PrefixChar = 'S';
        subMenu.PrefixColor = 708;
        subMenu.OnClicked += args => OpenSundouleiaSubMenu(args, _logger);
        args.AddMenuItem(subMenu);
    }

    private void OpenSundouleiaSubMenu(IMenuItemClickedArgs args, ILogger logger)
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
            OnClicked = (a) => { _mediator.Publish(new TogglePermissionWindow(this, 0)); },
        }]);
    }


    /// <summary> 
    ///     Method that creates the cached player (PairHandler) object for the client pair. <para />
    ///     This method is ONLY EVER CALLED BY THE PAIR MANAGER under the <c>MarkUserOnline</c> method! 
    /// </summary>
    /// <remarks> Until the CachedPlayer object is made, the client will not apply any data sent from this paired user. </remarks>
    public void CreateCachedPlayer(OnlineUser? dto = null)
    {
        try
        {
            _creationSemaphore.Wait();
            // If the cachedPlayer is already stored for this pair, we do not need to create it again, so return.
            if (CachedPlayer != null)
            {
                _logger.LogDebug("CachedPlayer already exists for " + UserData.UID, LoggerType.PairInfo);
                return;
            }

            // if the Dto sent to us by the server is null, and the pairs OnlineUser is null, dispose of the cached player and return.
            if (dto is null && _OnlineUser is null)
            {
                // dispose of the cached player and set it to null before returning
                _logger.LogDebug("No DTO provided for {uid}, and OnlineUser object in Pair class is null. Disposing of CachedPlayer", UserData.UID);
                CachedPlayer?.Dispose();
                CachedPlayer = null;
                return;
            }

            // if the OnlineUser contains information, we should update our pairs _OnlineUser to the dto
            if (dto != null)
            {
                _logger.LogDebug("Updating OnlineUser for " + UserData.UID, LoggerType.PairInfo);
                _OnlineUser = dto;
            }

            _logger.LogTrace("Disposing of existing CachedPlayer to create a new one for " + UserData.UID, LoggerType.PairInfo);
            CachedPlayer?.Dispose();
            CachedPlayer = _cachedPlayerFactory.Create(new(UserData, _OnlineUser!.Ident));
        }
        finally
        {
            _creationSemaphore.Release();
        }
    }

    public string? GetNickname() => _nickConfig.GetNicknameForUid(UserData.UID);
    public string GetNickAliasOrUid() => GetNickname() ?? UserData.AliasOrUID;
    public string GetPlayerNameHash() => CachedPlayer?.PlayerNameHash ?? string.Empty;

    /// <summary>
    ///     Used to inform our sundesmos that their respective player is now in render range. <para />
    ///     This should be set to the sundesmo's handler and processed for application.
    /// </summary>
    public unsafe void PlayerEnteredRender(Character* character)
    {
        if (character is null) return;
        _logger.LogDebug($"PlayerEnteredRender called for {GetNickAliasOrUid()}", LoggerType.PairHandler);
        Handler
    }

    /// <summary>
    ///     Marks the sundesmo as online, providing us with their UID and CharaIdent.
    /// </summary>
    public void MarkOnline(OnlineUser dto)
    {
        OnlineUserIdent = dto;
        // If we are not rendered, try and locate the character with the matching Identity.
        if (!IsRendered)
        {
            var currentCharacters = CharaObjectWatcher.RenderedCharas;
            foreach (var chara in currentCharacters)

                var hash = SundouleiaAPI.Utilities.Hashing.HashCharacterName(chara.Name.ToString());
            }
        }

    public void CheckForCharacter()

    /// <summary>
    ///     Marks the pair as offline.
    /// </summary>
    public void MarkOffline() => OnlineUserIdent = null;

    /// <summary>
    ///     Removes all applied appearance data for the sundesmo if rendered, 
    ///     and disposes all internal data.
    /// </summary>
    public void DisposeData()
    {
        // Clear out any data in the handler by disposing of it.
        // Keep in mind this will dispose all processed data.
        
    }
}
