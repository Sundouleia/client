using CkCommons;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.Services;

/// <summary>
///     Handles updating other sundesmos based on connection states.
/// </summary>
public sealed class DistributorService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;
    // maybe merge with this, not sure.
    private readonly ClientUpdateService _updateService;

    private SemaphoreSlim _updateSlim = new SemaphoreSlim(1, 1);

    // manage timeout tracking on 'newly visible users' so we know if it is just a reconnect or a actual timeout.
    // - means we likely need a helper class that holds the UserData's we updated with said latest data.
    // - can compare any new people against it and then see if they were recovering from a timeout or not.
    // - will help us know what is a 'fresh sundesmo' connecting / becoming visible or not.
    private readonly HashSet<UserData> _newVisibleUsers = [];
    private readonly HashSet<UserData> _newOnlineUsers = [];

    public DistributorService(ILogger<DistributorService> logger, SundouleiaMediator mediator,
        MainHub hub, SundesmoManager sundesmos, ClientUpdateService updateService)
        : base(logger, mediator)
    {
        _hub = hub;
        _sundesmos = sundesmos;
        _updateService = updateService;

        // Updates.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => DelayedFrameworkOnUpdate());

        // User Pair management.
        Mediator.Subscribe<PairWentOnlineMessage>(this, arg =>
        {
            if (!MainHub.IsConnectionDataSynced)
                return;
            _newOnlineUsers.Add(arg.UserData);
        });
        Mediator.Subscribe<PairHandlerVisibleMessage>(this, msg => { });

        // Upon connection we should push to all visible users, but only if to users who were not from our timeout?
        Mediator.Subscribe<ConnectedMessage>(this, _ => PushToAllIfNew());
    }

    // Idk why we need this really, anymore, but whatever i guess. If it helps it helps.
    private Dictionary<string, string> _modPlaceholder = new();
    private ModDataUpdate _lastPushedModData = new();
    private VisualDataUpdate _lastPushedIpcData = new();


    private void PushToAllIfNew()
    {
        // dummy.
    }

    private void DelayedFrameworkOnUpdate()
    {
        // Do not process if not data synced.
        if (!MainHub.IsConnectionDataSynced)
            return;

        // Handle Online Players.
        if (_newOnlineUsers.Count > 0)
        {
            var newOnlineUsers = _newOnlineUsers.ToList();
            _newOnlineUsers.Clear();
            PushCompositeData(newOnlineUsers).ConfigureAwait(false);
        }

        // Handle Visible Players.
        if (PlayerData.Available && _newVisibleUsers.Count > 0)
        {
            var newVisiblePlayers = _newVisibleUsers.ToList();
            _newVisibleUsers.Clear();
            UpdateVisibleFull(newVisiblePlayers).ConfigureAwait(false);
        }
    }

    /// <summary> Informs us if the new data being passed in is different from the previous stored. </summary>
    /// <remarks> This does not update the object, you should update it if this returns true. </remarks>
    private bool DataIsDifferent<T>(T? prevData, T newData) where T : class
    {
        if (prevData is null || !Equals(newData, prevData))
            return true;

        Logger.LogDebug("Data was no different. Not sending data", LoggerType.ApiCore);
        return false;
    }

    /// <summary>
    ///     Method used for updating all provided visible sundesmo's with our moodles and appearance data. <para />
    ///     Called whenever a new visible pair enters our render range.
    /// </summary>
    private async Task UpdateVisibleFull(List<UserData> visibleCharas)
    {
        if (!MainHub.IsConnectionDataSynced)
        {
            Logger.LogDebug("Not pushing Visible Full Data, not connected to server or data not synced.", LoggerType.ApiCore);
            _newVisibleUsers.UnionWith(visibleCharas);
            return;
        }

        Logger.LogDebug($"Pushing Appearance and Moodles data to ({string.Join(", ", visibleCharas.Select(v => v.AliasOrUID))})", LoggerType.ApiCore);
        await Task.Delay(1).ConfigureAwait(false);
    }

    private async Task PushCompositeData(List<UserData> newOnlinesundesmos)
    {
        // if not connected and data synced just add the sundesmos to the list. (Extra safety net)
        if (!MainHub.IsConnectionDataSynced)
        {
            Logger.LogDebug("Not pushing Composite Data, not connected to server or data not synced.", LoggerType.ApiCore);
            _newOnlineUsers.UnionWith(newOnlinesundesmos);
            return;
        }

        if (newOnlinesundesmos.Count <= 0)
            return;

        // do the push thing.
        await Task.Delay(1).ConfigureAwait(false);
    }

}
