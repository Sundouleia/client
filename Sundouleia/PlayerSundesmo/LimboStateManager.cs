using CkCommons;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;

namespace Sundouleia.Pairs;

internal record SundesmoInLimbo(Sundesmo Sundesmo, Task OnTimeout, CancellationTokenSource TimeoutCTS);

/// <summary>
///     Manages the limbo states of the client's Sundesmos. <para />
///     
///     When a Sundesmo that was previously rendered becomes underendered, 
///     or goes offline while rendered, they are placed into a timeout state. <br/>
///     <b> When this timeout expires, their appearence is reverted. </b><para />
///     
///     When marked for Unloading, sundesmos are added to the SundesmosReloading hashset.
///     Changing territories will cleanup the cached data in these Sundesmos to optimize 
///     how much data is being cached. <br/>
///     <b> This may likely not be nessisary at all and could probably be removed. </b>
/// </summary>
public sealed class LimboStateManager : DisposableMediatorSubscriberBase
{
    public const int TIMEOUT_SECONDS = 7;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(TIMEOUT_SECONDS);

    private ConcurrentDictionary<UserData, SundesmoInLimbo> _timeoutTasks = new(UserDataComparer.Instance);

    public LimboStateManager(ILogger<LimboStateManager> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
        // Dont need to subscribe to entering and leaving limbo states,
        // as their states are not innate, and reflected via InLimbo.
    }

    /// <summary>
    ///     The Sundesmos currently in limbo timeout.
    /// </summary>
    public IEnumerable<UserData> InLimbo => _timeoutTasks.Keys;


    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Cancel all limbo tasks.
        foreach (var tuple in _timeoutTasks.Values)
            tuple.TimeoutCTS.SafeCancelDispose();
        // Clear all remaining tasks.
        _timeoutTasks.Clear();
    }


    public bool IsInLimbo(UserData user)
        => _timeoutTasks.ContainsKey(user);

    public bool EnterLimbo(Sundesmo s, Func<Task> onTimeout)
        => EnterLimbo(s, DefaultTimeout, onTimeout);

    public bool EnterLimbo(Sundesmo s, TimeSpan timeout, Func<Task> onTimeout)
    {
        if (_timeoutTasks.ContainsKey(s.UserData))
            return false;

        // init the cts for the task.
        var cts = new CancellationTokenSource();
        // Assign the internal limbo task.
        var task = Task.Run(async () =>
        {
            try
            {
                // If visible, send into limbo.
                if (s.IsRendered)
                    Mediator.Publish(new SundesmoEnteredLimbo(s));

                // Await for the defined timeout time. If canceled at any point, the
                // mediator will ask them to leave limbo regardless.
                await Task.Delay(timeout, cts.Token);

                // Inform the mediator that they left limbo, and revert their visual status.
                Logger.LogDebug($"Timeout elapsed for [{s.PlayerName}] ({s.GetNickAliasOrUid()}).", LoggerType.PairManagement);
                Mediator.Publish(new SundesmoLeftLimbo(s));

                // Run whatever we wanted to run after the timeout expired.
                await onTimeout();
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug($"Timeout cancelled for [{s.PlayerName}] ({s.GetNickAliasOrUid()}).", LoggerType.PairManagement);
                Mediator.Publish(new SundesmoLeftLimbo(s));
            }
            finally
            {
                // Clean up the dictionary entry.
                _timeoutTasks.TryRemove(s.UserData, out _);
            }
        }, cts.Token);

        // Update the dictionary for this sundesmo.
        return _timeoutTasks.TryAdd(s.UserData, new SundesmoInLimbo(s, task, cts));
    }

    public bool CancelLimbo(UserData user)
    {
        if (!_timeoutTasks.TryRemove(user, out var tuple))
            return false;
        // Run a safe cancel and dispose on the CTS, halting the task.
        tuple.TimeoutCTS.SafeCancelDispose();
        return true;
    }

    // Could maybe include custom logic spesifically for reverting appearnace in here idk.

}