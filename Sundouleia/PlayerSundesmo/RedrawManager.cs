using Sundouleia;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Enums;

namespace Sundouleia.Pairs;
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
public class RedrawManager(Sundesmo sundesmo, ILogger<RedrawManager> logger, IpcManager ipc) : IDisposable
{
    /// <summary>
    ///     Ensures only one PlayerHandler operation is occuring at a time.
    /// </summary>
    private readonly SemaphoreSlim _playerUpdateSlim = new(1, 1);

    /// <summary>
    ///    Tracks the number of ongoing update processes. Redraws are deferred until this count reaches zero.
    /// </summary>
    private int _updatesProcessing = 0;

    /// <summary>
    ///     RedrawType assigned as a result of the ApplyWithRedraw call using the PlayerHandler. <para />
    ///     This is kept so we can culminate redraws at the end of the update process.
    /// </summary>
    private RedrawKind _playerPendingType = RedrawKind.None;

    /// <summary>
    ///     Redraws enqueued by the Sundesmo's OwnedObjects while _updatesProcessing > 0 (Which incs from UpdateSlims).
    /// </summary>
    private readonly ConcurrentDictionary<PlayerOwnedHandler, RedrawKind> _pendingRedraws = new();

    public void Dispose()
    {
        _playerUpdateSlim.Wait();
        _playerUpdateSlim.Dispose();
        _pendingRedraws.Clear();
    }

    /// <summary>
    ///     Trigger a manual begin, that must complete with an end. Perform this if an extra outer scope is needed.
    /// </summary>
    public void BeginUpdate()
    {
        Interlocked.Increment(ref _updatesProcessing);
        logger.LogDebug($"[ManualBeginUpdate] {sundesmo.GetNickAliasOrUid()} (Processing {_updatesProcessing} Updates)", LoggerType.PairManagement);
    }

    /// <summary>
    ///    Applies updates and records any required redraws to be processed at the end of the update cycle.
    /// </summary>
    public async Task RunOnPendingRedrawSlim(PlayerHandler player, Func<Task<RedrawKind>> funcWithPendingRedraw)
    {
        // Begin by incrementing the interlocked processing count, to refer excessive redraws.
        Interlocked.Increment(ref _updatesProcessing);
        logger.LogDebug($"[BeginUpdate] {player.NameString}({sundesmo.GetNickAliasOrUid()}) (Now Processing {_updatesProcessing} Updates)", LoggerType.PairManagement);
        try
        {
            // Now we need to await for the slim to become free, then await for the func callback.
            await _playerUpdateSlim.WaitAsync();
            logger.LogDebug($"[RunOnSlim] {player.NameString}({sundesmo.GetNickAliasOrUid()})", LoggerType.PairManagement);

            var ret = await funcWithPendingRedraw();
            // Ensure the greater value is prioritized.
            _playerPendingType = ret > _playerPendingType ? ret : _playerPendingType;
        }
        finally
        {
            // Ends an update process. If no other updates are being processed, triggers processing of any pending redraws.
            if (Interlocked.Decrement(ref _updatesProcessing) == 0)
            {
                logger.LogDebug($"[EndUpdate] {player.NameString}({sundesmo.GetNickAliasOrUid()}) Hit 0, processing pending redraws.", LoggerType.PairManagement);
                ProcessPendingRedraws();
            }
            else
            {
                logger.LogDebug($"[EndUpdate] {player.NameString}({sundesmo.GetNickAliasOrUid()}) (Still has {_updatesProcessing} updates, deferring redraws)", LoggerType.PairManagement);
            }
            // Release the slim now that we are finished.
            SafeReleaseSlim();
        }
    }

    /// <summary>
    ///    Decrements the interlocked update process count. Should only be used after a begin, and in a try-finally <para />
    ///    If no other updates are being processed, triggers processing of any pending redraws.
    /// </summary>
    public void EndUpdate()
    {
        if (Interlocked.Decrement(ref _updatesProcessing) == 0)
        {
            logger.LogDebug($"[ManualEndUpdate] {sundesmo.GetNickAliasOrUid()}, Nothing more to process, performing pending redraws.", LoggerType.PairManagement);
            ProcessPendingRedraws();
        }
        else
        {
            logger.LogDebug($"[ManualEndUpdate] {sundesmo.GetNickAliasOrUid()} (Still has {_updatesProcessing} updates, deferring redraws)", LoggerType.PairManagement);
        }
    }

    public void RedrawOwnedObject(PlayerOwnedHandler ownedObj, RedrawKind type)
    {
        // Ignore if no redraw kind is needed or the object is not rendered.
        if (type is RedrawKind.None || !ownedObj.IsRendered)
            return;

        // If updates are running, defer and escalate
        if (Volatile.Read(ref _updatesProcessing) > 0)
        {
            _pendingRedraws.AddOrUpdate(ownedObj, type, (_, current) => type > current ? type : current);
            return;
        }

        // Otherwise redraw immediately
        RedrawOwnedInternal(ownedObj, type);
    }

    private void RedrawOwnedInternal(PlayerOwnedHandler ownedObj, RedrawKind type)
    {
        if (!ownedObj.IsRendered) return;
        RedrawInternal(ownedObj.ObjIndex, type);
    }

    // Could be task idk.
    private void RedrawInternal(ushort objIdx, RedrawKind type)
    {
        if (type.HasAny(RedrawKind.Full))
        {
            ipc.Penumbra.RedrawGameObject(objIdx);
            return;
        }
        if (type.HasAny(RedrawKind.Reapply))
        {
            ipc.Glamourer.ReapplyActor(objIdx);
            return;
        }
    }

    /// <summary>
    ///    Redraws any owned objects that have pending redraw requests, passing the appropriate redraw flags to each object.
    /// </summary>
    private void ProcessPendingRedraws()
    {
        // If there is a redraw pending for the player, handle it.
        if (sundesmo.IsRendered && _playerPendingType is not RedrawKind.None)
        {
            var redrawType = _playerPendingType;
            _playerPendingType = RedrawKind.None;
            RedrawInternal(sundesmo.ObjIndex, redrawType);
        }

        // Then handle all pending for the owned objects.
        foreach (var (ownedObj, redrawType) in _pendingRedraws)
        {
            if (redrawType is RedrawKind.None)
                continue;

            _pendingRedraws.TryRemove(ownedObj, out _);

            if (ownedObj.IsRendered)
                RedrawOwnedInternal(ownedObj, redrawType);
        }
    }

    private void SafeReleaseSlim()
    {
        try
        {
            _playerUpdateSlim.Release();
        }
        catch (ObjectDisposedException)
        {
            // Ignore, this instance of the class has been disposed.
        }
    }
}