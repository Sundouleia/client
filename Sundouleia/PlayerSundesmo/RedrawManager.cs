using Sundouleia;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Enums;
using Sundouleia.Services.Mediator;

/// <summary>
///    Manages redraw operations for a Sundesmo player, specifically ensuring redraws only happen while
///    no updates are being processed for the player or their owned objects.
/// </summary>
public class RedrawManager(ILogger<RedrawManager> logger, Sundesmo sundesmo) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Dictionary<OwnedObject, Redraw> _pendingRedraws = new(){
        { OwnedObject.Player,         Redraw.None },
        { OwnedObject.MinionOrMount,  Redraw.None },
        { OwnedObject.Pet,            Redraw.None },
        { OwnedObject.Companion,      Redraw.None },
    };

    private int _updatesProcessing = 0;

	public void BeginUpdate()
    {
        logger.LogDebug($"Beginning update for {sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
        Interlocked.Increment(ref _updatesProcessing);
    }

    public async Task ApplyAndRedraw(OwnedObject ownedObject, Func<Task<Redraw>> applyUpdates)
    {
        logger.LogDebug($"Applying updates for {sundesmo.GetNickAliasOrUid()}'s {ownedObject}.", LoggerType.PairManagement);
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
        logger.LogDebug($"Ending update for {sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
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
                    logger.LogDebug($"Processing pending redraw for {sundesmo.GetNickAliasOrUid()}'s {obj}: {pendingRedraw}", LoggerType.PairManagement);
                    sundesmo.GetOwnedHandler(obj)?.RedrawGameObject(pendingRedraw);
                    _pendingRedraws[obj] = Redraw.None;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

	public void Dispose()
	{
		_semaphore.Dispose();
	}
}