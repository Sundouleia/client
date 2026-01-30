using Sundouleia;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Enums;
using Sundouleia.Services.Mediator;

/// <summary>
///    Manages redraw operations for a Sundesmo player, specifically ensuring redraws only happen while
///    no updates are being processed for the player or their owned objects.<para/>
///    The general call pattern is:
///    <list type="number">
///        <item>BeginUpdate()</item>
///        <item>ApplyWithPendingRedraw(...) - multiple times as needed</item>
///        <item>EndUpdate()</item>
///    </list>
///    When EndUpdate is called and there are no other updates being processed, any pending redraws
///    are executed for the owned objects that requested them.
/// </summary>
public class RedrawManager(ILogger<RedrawManager> logger, Sundesmo sundesmo) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    ///    Tracks pending redraw requests for each owned object, which will be processed once all updates are complete.
    /// </summary>
    private readonly Dictionary<OwnedObject, Redraw> _pendingRedraws = new(){
        { OwnedObject.Player,         Redraw.None },
        { OwnedObject.MinionOrMount,  Redraw.None },
        { OwnedObject.Pet,            Redraw.None },
        { OwnedObject.Companion,      Redraw.None },
    };

    /// <summary>
    ///    Tracks the number of ongoing update processes. Redraws are deferred until this count reaches zero.
    /// </summary>
    private int _updatesProcessing = 0;

    /// <summary>
    ///    Begins an update process, preventing redraws until all updates are complete.
    /// </summary>
    public void BeginUpdate()
    {
        logger.LogDebug($"Beginning update for {sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
        Interlocked.Increment(ref _updatesProcessing);
    }

    /// <summary>
    ///    Applies updates and records any required redraws to be processed at the end of the update cycle.
    /// </summary>
    public async Task ApplyWithPendingRedraw(OwnedObject ownedObject, Func<Task<Redraw>> applyUpdates)
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

    /// <summary>
    ///    Ends an update process. If no other updates are being processed, triggers processing of any pending redraws.
    /// </summary>
    public void EndUpdate()
    {
        logger.LogDebug($"Ending update for {sundesmo.GetNickAliasOrUid()}.", LoggerType.PairManagement);
        if (Interlocked.Decrement(ref _updatesProcessing) == 0)
        {
            ProcessPendingRedraws();
        }
    }

    /// <summary>
    ///    Redraws any owned objects that have pending redraw requests, passing the appropriate redraw flags to each object.
    /// </summary>
    private void ProcessPendingRedraws()
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