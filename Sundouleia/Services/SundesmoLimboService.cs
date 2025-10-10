using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

// maybe use this service to govern how sundesmo limbo timeouts are governed, we'll see.
// It would be ideal if the sundesmo, or the service, managed timeouts and such, to have more control.
/// <summary> 
///     Service for updating which Sundesmos are newly visible, and which are in limbo. <para />
///     Helper methods are included to interact with and send updates to others. <para />
///     Tracks when updates are new / dirty, and cleans up sundesmos in limbo automatically.
/// </summary>
public sealed class SundesmoLimboService : DisposableMediatorSubscriberBase
{
    // likely file sending somewhere in here
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    public SundesmoLimboService(ILogger<SundesmoLimboService> logger, SundouleiaMediator mediator,
        IpcManager ipc, SundesmoManager pairs, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _ipc = ipc;
        _sundesmos = pairs;
        _watcher = watcher;

        // Important, informs us of their state, if they are reloading or not.
        Mediator.Subscribe<SundesmoOffline>(this, msg => OnSundesmoDisconnected(msg.Sundesmo));
        // Adds to _needsFullUpdate if not in limbo.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, msg => OnSundesmoRendered(msg.Handler));
        // Whenever the Sundesmo is no longer visible, They should be added to the limbo list, regardless of online state.
        Mediator.Subscribe<SundesmoPlayerUnrendered>(this, msg => OnSundesmoUnrendered(msg.Handler));

        // Unsure if i even want this right now yet.
        Mediator.Subscribe<SundesmoTimedOut>(this, msg => OnSundesmoTimedOut(msg.Handler));

        // Should probably make this be called via the manager,
        // but we should definitely place all sundesmos in limbo.
        //
        // It might even be better if we had the sundesmo timeout process inside of here.
        Mediator.Subscribe<DisconnectedMessage>(this, _ => _needsFullUpdate.Clear());
    }

    // Private these so that we ensure access is controlled.
    private HashSet<UserData> _needsFullUpdate = new();
    private HashSet<UserData> _inLimbo = new();

    // Hopefully unrendering and going offline doesnt conflict too much?
    private void OnSundesmoDisconnected(Sundesmo s)
    {
        // if they are marked for unloading, remove them from limbo and exit.
        if (s.IsReloading)
        {
            Logger.LogDebug($"{s.GetNickAliasOrUid()} disconnected and was marked for reloading, removing from limbo if present.", LoggerType.PairVisibility);
            _inLimbo.Remove(s.UserData);
            _needsFullUpdate.Remove(s.UserData);
            return;
        }
        // Should also check if they are still rendered after being offline.
        // NOTE: This allows for a disconnect with the sundesmo's timeout state. Attempt to reconnect them!
        if ((s.State & SundesmoState.VisibleWithData) != 0)
        {
            Logger.LogDebug($"{s.GetNickAliasOrUid()} ({s.PlayerName}) disconnected while visible with data. Placing in limbo.", LoggerType.PairVisibility);
            _inLimbo.Add(s.UserData);
            return;
        }
    }

    private void OnSundesmoRendered(PlayerHandler handler)
    {
        // If in limbo, only remove them from limbo, and do not add to _needsFullUpdate.
        if (_inLimbo.Remove(handler.Sundesmo.UserData))
            return;
        Logger.LogDebug($"Ensuring a FullDataIpcUpdate is sent to {handler.Sundesmo.GetNickAliasOrUid()} ({handler.Sundesmo.PlayerName})", LoggerType.PairVisibility);
        _needsFullUpdate.Add(handler.Sundesmo.UserData);
    }


    // I dont really like this concept because someone could be 'unrendered' or 'offline' and they both imply timeout.
    // This will likely cause issues we will need to fix soon.
    private void OnSundesmoUnrendered(PlayerHandler handler)
    {
        // Ignore if offline, as they already 
        if (!handler.Sundesmo.IsOnline)
            return;
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} unrendered but is still online. Adding to limbo.", LoggerType.PairVisibility);
        _inLimbo.Add(handler.Sundesmo.UserData);
    }

    // Remove the sundesmo from the limbo hashset, so we send them a full update next time.
    private void OnSundesmoTimedOut(PlayerHandler handler)
    {
        Logger.LogDebug($"Sundesmo {handler.Sundesmo.PlayerName} timed out, removing from limbo.", LoggerType.PairVisibility);
        _inLimbo.Remove(handler.Sundesmo.UserData);
    }

    //public List<UserData> NewVisibleNoLimbo => _sundesmos.GetVisibleConnected().Except(NewVisible).ToList();
    //public List<UserData> SundesmosForUpdatePush => _sundesmos.GetVisibleConnected().Except([.. InLimbo, .. NewVisible]).ToList();
}