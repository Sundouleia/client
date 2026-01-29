using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

/// <summary>
///     Operates the task execution and CTS's of operations performed by the
///     ClientUpdateHandler and the ClientDistrutor.
/// </summary>
public sealed class ClientUpdateService : DisposableMediatorSubscriberBase
{
    private readonly LimboStateManager _limbo;
    private readonly SundesmoManager _sundesmos;

    // Internal variables.
    private IpcKind _allPendingUpdates = IpcKind.None;
    private Dictionary<OwnedObject, IpcKind> _pendingUpdates = new();

    // Internal datacache storage of the Client's modded state.
    private readonly SemaphoreSlim _dataUpdateLock = new(1, 1);
    private ClientDataCache _latestData = new();

    // Debouncer to handle multiple updates occuring at once.
    private Task? _debounceTask;
    private CancellationTokenSource _debounceCTS = new();

    // For distributing data to other sundesmos.
    private Task? _distributionTask;
    private CancellationTokenSource _distributionCTS = new();

    public ClientUpdateService(ILogger<ClientUpdateService> logger, SundouleiaMediator mediator,
        LimboStateManager limbo, SundesmoManager sundesmos) : base(logger, mediator)
    {
        _limbo = limbo;
        _sundesmos = sundesmos;

        Mediator.Subscribe<SundesmoPlayerRendered>(this, msg => NewVisibleUsers.Add(msg.Handler.Sundesmo.UserData));

        // We shouldnt technically need to add this, but we can if we need an extra failsafe
        // Svc.ClientState.Logout += OnLogout;
    }

    // Update management, Internal use only.
    internal HashSet<UserData> NewVisibleUsers { get; private set; } = new();
    internal List<UserData> UsersForUpdatePush => _sundesmos.GetVisibleConnected().Except([.. _limbo.InLimbo, .. NewVisibleUsers]).ToList();
    internal ClientDataCache LatestData => _latestData;

    // Pending Updates.
    public IReadOnlyDictionary<OwnedObject, IpcKind> PendingUpdates => _pendingUpdates;
    public IpcKind AllPendingUpdates => _allPendingUpdates;

    // Accessible helpers to prevent race conditions.
    public bool Debouncing => _debounceTask is not null && !_debounceTask.IsCompleted;
    public bool Distributing => _distributionTask is not null && !_distributionTask.IsCompleted;
    public bool UpdatingData => _dataUpdateLock.CurrentCount is 0;
    public bool InUpdateTask => Debouncing || UpdatingData;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _debounceCTS.SafeCancelDispose();
            _distributionCTS.SafeCancelDispose();
        }

        // Svc.ClientState.Logout -= OnLogout;
    }

    /// <summary>
    ///     An additional failsafe in place to ensure that no data is carried over between players.
    /// </summary>
    private async void OnLogout(int type, int code)
    {
        Logger.LogInformation($"Player Logout detected, cleaning up any stored IPC Data.");
        _latestData = new();
        _pendingUpdates.Clear();
        _allPendingUpdates = IpcKind.None;
        NewVisibleUsers.Clear();
    }

    // The debounce time increases based on what updates are pending currently.
    // This has some flaws due to how any additional updates that are not Mods
    // will still restart the timer by 1000ms, but we can change this up later,
    // it's not too important right now.
    public int GetDebounceTime()
    {
        if (_allPendingUpdates.HasAny(IpcKind.Mods))        return 1000;
        if (_allPendingUpdates.HasAny(IpcKind.Glamourer))   return 750;
        if (_allPendingUpdates.HasAny(IpcKind.Heels))       return 750;
        if (_allPendingUpdates.HasAny(IpcKind.CPlus))       return 750;
        if (_allPendingUpdates.HasAny(IpcKind.Honorific))   return 500;
        if (_allPendingUpdates.HasAny(IpcKind.Moodles))     return 250;
        if (_allPendingUpdates.HasAny(IpcKind.ModManips))   return 250;
        if (_allPendingUpdates.HasAny(IpcKind.PetNames))    return 150;
        return 1500;
    }

    /// <summary>
    ///     Adds a update to be processed after the debounce period, and restart the debouncer.
    /// </summary>
    public void AddPendingUpdate(OwnedObject type, IpcKind kind)
    {
        _debounceCTS = _debounceCTS.SafeCancelRecreate();
        Logger.LogTrace($"Detected update for {type} ({kind})", LoggerType.ClientUpdates);
        if (_pendingUpdates.ContainsKey(type))
            _pendingUpdates[type] |= kind;
        else
            _pendingUpdates[type] = kind;
        _allPendingUpdates |= kind;
    }

    public void ClearPendingUpdates()
    {
        _pendingUpdates.Clear();
        _allPendingUpdates = IpcKind.None;
    }

    public void SetDebounceTask(Func<Task> task)
    {
        _debounceTask = Task.Run(async () =>
        {
            // await for the processed debounce time, or until cancelled.
            Logger.LogTrace($"Waiting for debounce time of {GetDebounceTime()}ms", LoggerType.ClientUpdates);
            await Task.Delay(GetDebounceTime(), _debounceCTS.Token).ConfigureAwait(false);
            // Run the task.
            await task();
        }, _debounceCTS.Token);
    }

    public void RefreshDistributionCTS() => _distributionCTS = _distributionCTS.SafeCancelRecreate();
    public void SetDistributionTask(Func<Task> task)
        => _distributionTask = Task.Run(async () => await task(), _distributionCTS.Token);

    /// <summary>
    ///     Execute an operation that will be performed inside of a DataUpdateLock,
    ///     utilizing the semaphore slim to ensure no race conditions.
    /// </summary>
    public async Task RunOnDataUpdateSlim(Func<Task> task)
    {
        await _dataUpdateLock.WaitAsync();
        try
        {
            await task();
        }
        catch (Bagagwa ex)
        {
            Logger.LogError($"Error in RunOnDataUpdateSlim: {ex}");
        }
        finally
        {
            _dataUpdateLock.Release();
        }
    }
}
