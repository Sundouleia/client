using Sundouleia;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Enums;
using Sundouleia.Services.Mediator;

/// <summary>
///    Manages redraw operations for a Sundesmo player, specifically ensuring redraws only happen while
///    no updates are being processed for the player or their owned objects.
/// </summary>
public class RedrawManager : DisposableMediatorSubscriberBase
{
    private readonly Sundesmo _sundesmo;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<OwnedObject, Redraw> _pendingRedraws = new(){
        { OwnedObject.Player,         Redraw.None },
        { OwnedObject.MinionOrMount,  Redraw.None },
        { OwnedObject.Pet,            Redraw.None },
        { OwnedObject.Companion,      Redraw.None },
    };

    private int _updatesProcessing = 0;

    public RedrawManager(ILogger<RedrawManager> logger, SundouleiaMediator mediator, Sundesmo sundesmo) : base(logger, mediator)
    {
        _sundesmo = sundesmo;
    }

    public void BeginUpdate()
    {
        Logger.LogDebug($"Beginning update for {_sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
        Interlocked.Increment(ref _updatesProcessing);
    }

    public async Task ApplyAndRedraw(OwnedObject ownedObject, Func<Task<Redraw>> applyUpdates)
    {
        Logger.LogDebug($"Applying updates for {_sundesmo.GetNickAliasOrUid()}'s {ownedObject}.", LoggerType.PairManagement);
        try
        {
            await _semaphore.WaitAsync();
            _pendingRedraws[ownedObject] |= await applyUpdates();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void EndUpdate()
    {
        Logger.LogDebug($"Ending update for {_sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
        if (Interlocked.Decrement(ref _updatesProcessing) == 0)
        {
            ProcessPendingRedraws();
        }
    }

    public void ProcessPendingRedraws()
    {
        try
        {
            _semaphore.Wait();

            foreach (var (obj, pendingRedraw) in _pendingRedraws.ToList())
            {
                if (pendingRedraw != Redraw.None)
                {
                    Logger.LogDebug($"Processing pending redraw for {_sundesmo.GetNickAliasOrUid()}'s {obj}: {pendingRedraw}", LoggerType.PairManagement);
                    _sundesmo.GetOwnedHandler(obj)?.RedrawGameObject(pendingRedraw);
                    _pendingRedraws[obj] = Redraw.None;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}