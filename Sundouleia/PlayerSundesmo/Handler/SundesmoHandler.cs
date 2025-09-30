using CkCommons;
using Dalamud.Game.ClientState.Objects.Types;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.Pairs.Factories;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs.Handlers;
/// <summary>
/// 
/// </summary>
public sealed class SundesmoHandlerZ : DisposableMediatorSubscriberBase
{
     private readonly UserGameObjFactory _factory;
     private readonly IpcManager _ipc;
     private readonly OnFrameworkService _frameworkUtil;
     private readonly IHostApplicationLifetime _lifetime;

     private CancellationTokenSource? _appCTS = new();

     private Guid _penumbraCollectionId = Guid.Empty;
     private Guid? _activeCustomize = null;

     // Cached, nullable data.
     private CharaIpcDataFull? _appearance = null;
     private string? _statusManagerStr = null;
     private UserGameObj? _gameObject;
    
     // if this sundesmo is currently visible.
     private bool _isVisible;

     public SundesmoHandler(OnlineUser onlineUser, ILogger<SundesmoHandler> logger, SundouleiaMediator mediator,
     UserGameObjFactory gen, IpcManager ipc, OnFrameworkService frameworkUtil, IHostApplicationLifetime app)
     : base(logger, mediator)
{
     OnlineUser = onlineUser;
     _factory = gen;
     _ipc = ipc;
     _frameworkUtil = frameworkUtil;
     _lifetime = app;

     // Can easily create a temporary collection here to manage pcp's for if need be,
     // or even manage them with helper functions.
     //_penumbraCollectionId = _ipc.Penumbra.CreateUserCollection(OnlineUser.User.UID).ConfigureAwait(false).GetAwaiter().GetResult();
     //Mediator.Subscribe<PenumbraInitialized>(this, _ =>
     //{
     //    _penumbraCollectionId = _ipc.Penumbra.CreateUserCollection(OnlineUser.User.UID).ConfigureAwait(false).GetAwaiter().GetResult();
     //    if (!IsVisible && _gameObject != null)
     //    {
     //        PlayerName = string.Empty;
     //        _gameObject.Dispose();
     //        _gameObject = null;
     //    }
     //});
     // subscribe to the framework update Message 
     Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());
     // Invalidate our sundesmo pairs whenever we begin changing zones.
     Mediator.Subscribe<ZoneSwitchStartMessage>(this, (_) =>
     {
          _gameObject?.Invalidate();
          IsVisible = false;
     });
}

     // determines if a paired user is visible. (if they are in render range)
     public bool IsVisible
     {
          get => _isVisible;
          private set
          {
               if (_isVisible != value)
               {
                    _isVisible = value;
                    Logger.LogTrace("User Visibility Changed, now: " + (_isVisible ? "Is Visible" : "Is not Visible"), LoggerType.PairHandlers);
                    Mediator.Publish(new RefreshUiMessage());
                    Mediator.Publish(new VisibleUsersChanged());
               }
          }
     }

     public OnlineUser OnlineUser { get; private set; }  // the online user Dto. Set when pairhandler is made for the cached player in the pair object.
     public nint PairAddress => _gameObject?.Address ?? nint.Zero; // the player character object address
     public IGameObject? PairObject => _gameObject?.PlayerCharacterObjRef; // the player character object
     public string? PlayerName { get; private set; }
     public string PlayerNameWithWorld => _gameObject?.NameWithWorld ?? string.Empty;
     public string PlayerNameHash => OnlineUser.Ident;

     public override string ToString()
          => OnlineUser is null ? base.ToString() ?? string.Empty
               : $"AliasOrUID: ({OnlineUser.User.AliasOrUID}, {(_gameObject != null ? _gameObject.ToString() : "NoHandler")}";

     protected override void Dispose(bool disposing)
     {
          base.Dispose(disposing);

          // store name and address to reference removal properly.
          var name = PlayerNameWithWorld;
          var address = _gameObject?.Address ?? nint.Zero;
          Logger.LogDebug($"Disposing User: {name} ({OnlineUser})", LoggerType.PairHandlers);
          // Safely dispose.
          Generic.Safe(() =>
          {
               // safely cancel any running vsual application tasks.
               _appCTS.SafeCancelDispose();
               _appCTS = null;
               // if the pair was a visible sundesmo, publish their disposal.
               if (!string.IsNullOrEmpty(name))
                    Mediator.Publish(new EventMessage(new(name, OnlineUser.User.UID, DataEventType.VisibilityChange, "Disposing User Handler")));

               // if the hosted service lifetime is ending, return
               if (_lifetime.ApplicationStopping.IsCancellationRequested)
               {
                    // try and remove regardless and prevent a deadlock if on shutdown?
                    // (Dunno why this is nessisary but whatever i guess)
                    // _ipc.Penumbra.RemoveUserCollection(_penumbraCollectionId).ConfigureAwait(false);
                    _ipc.Glamourer.ReleaseUser(this).ConfigureAwait(false);
                    _ipc.CustomizePlus.RevertUserProfile(_activeCustomize).ConfigureAwait(false);
                    _ipc.Heels.RestoreUserOffset(this).ConfigureAwait(false);
                    _ipc.Honorific.ClearTitleAsync(this).ConfigureAwait(false);
                    _ipc.PetNames.ClearUserPetNames(this).ConfigureAwait(false);
                    _ipc.Moodles.ClearStatus(name).ConfigureAwait(false);
                    return;
               }

               // If not zoning, and the player is being disposed, this sundesmo has left the zone, and we need to invalidate them.
               if (!PlayerData.IsZoning && !string.IsNullOrEmpty(name))
               {
                    Logger.LogTrace($"Restoring Vanilla state for User: {name} ({OnlineUser})", LoggerType.PairHandlers);
                    // if they are not not visible (have no valid pointer) we need to revert them by name.
                    if (!IsVisible)
                    {
                    // Glamourer is special as it will not revert the data if they are not present.
                    Logger.LogDebug($"Reverting Glamour to Vanilla state for User: {name}", LoggerType.PairHandlers);
                    _ipc.Glamourer.ReleaseUserByName(name).ConfigureAwait(false);
                    }
                    // otherwise, revert ALL data if they are visible!
                    else
                    {
                    Logger.LogInformation($"Is User IPCData null? {_appearance is null}", LoggerType.PairHandlers);
                    // catch inside inner exception just incase.
                    RevertAppearanceData(name).GetAwaiter().GetResult();
                    }
               }
          });

          // safely dispose of the sundesmo game object.
          _gameObject?.Dispose();
          _gameObject = null;
          PlayerName = null;
          _appearance = null;
          Logger.LogDebug($"Disposal complete for User: {name} ({OnlineUser})", LoggerType.PairHandlers);
     }

     public async Task ApplyAppearanceData(CharaIpcDataFull newData)
     {
          if (PairAddress == nint.Zero) return;
          if (_appearance is null) _appearance = new CharaIpcDataFull();

          // may need to process a cancellation token here if overlap occurs, but it shouldnt due to updates being 1s apart.

          // Process the application of all non-null data.
          Logger.LogDebug($"Updating appearance for User: {PlayerName} ({OnlineUser.User.AliasOrUID})", LoggerType.GameObjects);
          // maybe wait for redraw finish? idk..
          await ApplyUpdatedAppearance(newData).ConfigureAwait(false);

          // maybe some redraw logic here, maybe not, we'll see.

          // Mark as updated.
          _appearance.UpdateNonNull(newData);
          Logger.LogInformation($"Updated appearance for User: {PlayerName} ({OnlineUser.User.AliasOrUID})", LoggerType.GameObjects);
     }

     public async Task ApplyAppearanceSingle(IpcKind type, string newDataString)
     {
          if (PairAddress == nint.Zero) return;
          if (_appearance is null) return;
          // apply the new data.
          switch (type)
          {
               //case IpcKind.ModManips when !newDataString.Equals(_appearance.ModManips) && _penumbraCollectionId != Guid.Empty:
               //    await _ipc.Penumbra.SetUserManipulations(_penumbraCollectionId, newDataString).ConfigureAwait(false);
               //    break;
               case IpcKind.Glamourer when !newDataString.Equals(_appearance.GlamourerBase64):
                    await _ipc.Glamourer.ApplyUserGlamour(this, newDataString).ConfigureAwait(false);
                    break;
               case IpcKind.CPlus when !newDataString.Equals(_appearance.CustomizeProfile):
                    _activeCustomize = await _ipc.CustomizePlus.SetUserProfile(this, newDataString).ConfigureAwait(false);
                    break;
               case IpcKind.Heels when !newDataString.Equals(_appearance.HeelsOffset):
                    await _ipc.Heels.SetUserOffset(this, newDataString).ConfigureAwait(false);
                    break;
               case IpcKind.Honorific when !newDataString.Equals(_appearance.HonorificTitle):
                    await _ipc.Honorific.SetTitleAsync(this, newDataString).ConfigureAwait(false);
                    break;
               case IpcKind.PetNames when !newDataString.Equals(_appearance.PetNicknames):
                    await _ipc.PetNames.SetUserPetNames(this, newDataString).ConfigureAwait(false);
                    break;
               default:
                    return;
          }
          // update the appearance data.
          Logger.LogDebug($"Updated {type} for User: {PlayerName} ({OnlineUser.User.AliasOrUID})", LoggerType.PairHandlers);
          _appearance.UpdateNewData(type, newDataString);
     }

     public void UpdateMoodles(string newDataString)
     {
          if (PairAddress == nint.Zero || PlayerNameWithWorld.Length == 0)
               return;
          Logger.LogDebug($"Updating moodles for User: {PlayerName} ({OnlineUser.User.AliasOrUID})", LoggerType.PairHandlers);
          _ipc.Moodles.SetStatus(PlayerNameWithWorld, newDataString).ConfigureAwait(false);
          // update the string.
          _statusManagerStr = newDataString;
     }

     private async Task ApplyUpdatedAppearance(CharaIpcDataFull newData, bool force = false)
     {
          // Apply ModManips if different.
          if (_penumbraCollectionId != Guid.Empty)
          {
               if (newData.ModManips != null && (force || !newData.ModManips.Equals(_appearance!.ModManips)))
                    await _ipc.Penumbra.SetUserManipulations(_penumbraCollectionId, newData.ModManips).ConfigureAwait(false);
          }

          // Apply Glamour if different.
          if (newData.GlamourerBase64 != null && (force || !newData.GlamourerBase64.Equals(_appearance!.GlamourerBase64)))
               await _ipc.Glamourer.ApplyUserGlamour(this, newData.GlamourerBase64).ConfigureAwait(false);
        
          // Apply Customize+ if different.
          if (newData.CustomizeProfile != null && (force || !newData.CustomizeProfile.Equals(_appearance!.CustomizeProfile)))
          {
               // update the active profile to what we set, we should do this if there is a difference in what profile to enforce.
               // this should also revert if the new string is empty.
               _activeCustomize = await _ipc.CustomizePlus.SetUserProfile(this, newData.CustomizeProfile).ConfigureAwait(false);
          }
          // might need to do an else if or something here as C+ works wierd with how it does reverts.

          // Apply Heels if different.
          if (newData.HeelsOffset != null && (force || !newData.HeelsOffset.Equals(_appearance!.HeelsOffset)))
               await _ipc.Heels.SetUserOffset(this, newData.HeelsOffset).ConfigureAwait(false);

          // Apply Honorific if different. (will clear title if string.empty)
          if (newData.HonorificTitle != null && (force || !newData.HonorificTitle.Equals(_appearance!.HonorificTitle)))
               await _ipc.Honorific.SetTitleAsync(this, newData.HonorificTitle).ConfigureAwait(false);

          // Apply Pet Nicknames if different. (will clear nicks if string.empty)
          if (newData.PetNicknames != null && (force || !newData.PetNicknames.Equals(_appearance!.PetNicknames)))
               await _ipc.PetNames.SetUserPetNames(this, newData.PetNicknames).ConfigureAwait(false);
     }



     private void FrameworkUpdate()
     {
          // Perform first time initializations on Users if not initialized.
          if (string.IsNullOrEmpty(PlayerName))
          {
               // get name/address from cache in framework utils by Ident. Return if it's not found / default.
               var nameAndAddr = _frameworkUtil.FindPlayerByNameHash(OnlineUser.Ident);
               if (nameAndAddr == default((string, nint)))
                    return;
               // Perform Initialization for the User. (This sets the PlayerName, making this only happen once).
               Logger.LogDebug($"One-Time Initializing [{this}]", LoggerType.PairHandlers);
               Initialize(nameAndAddr.Name);
               Logger.LogDebug($"One-Time Initialized [{this}] ({nameAndAddr.Name})", LoggerType.PairHandlers);
          }

          // If the monitored objects address is valid, but IsVisible is false, apply their data.
          if (_gameObject?.Address != nint.Zero && !IsVisible)
          {
               IsVisible = true;
               // publish that this User is not visible.
               Mediator.Publish(new PairHandlerVisibleMessage(this));
               // if they have non-null cached data, reapply it to them.
               Logger.LogTrace($"Visibility changed for User: {PlayerName} ({OnlineUser.User.AliasOrUID}). Now: {IsVisible}", LoggerType.PairHandlers);
               if (_appearance is not null)
                    _ = Task.Run(() => ApplyUpdatedAppearance(_appearance, true));
               if (_statusManagerStr is not null)
                    _ = Task.Run(() => UpdateMoodles(_statusManagerStr));
          }
          // otherwise, if they went from visible to not visible, we should invalidate them.
          else if (_gameObject?.Address == nint.Zero && IsVisible)
          {
               IsVisible = false;
               _gameObject.Invalidate();
               Logger.LogTrace($"Invalidating as Visibility changed for User: {PlayerName} ({OnlineUser.User.AliasOrUID}). Now: {IsVisible}", LoggerType.PairHandlers);
          }
     }
}
