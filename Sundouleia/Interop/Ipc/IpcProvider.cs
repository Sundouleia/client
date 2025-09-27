using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Mediator;
using Microsoft.Extensions.Hosting;

namespace Sundouleia.Interop;

/// <summary>
/// The IPC Provider for Sundouleia to interact with other plugins by sharing information about visible players.
/// </summary>
public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private const int SundouleiaApiVersion = 1;

    private readonly ILogger<IpcProvider> _logger;
    private readonly SundesmoManager _pairManager;

    public SundouleiaMediator Mediator { get; init; }


    // IPC Event Actions intended to be sent over to Moodles.
    private static ICallGateProvider<MoodlesStatusInfo, object?>?               ApplyStatusInfo;      // applies a moodle tuple to the client.
    private static ICallGateProvider<List<MoodlesStatusInfo>, object?>?         ApplyStatusInfoList;  // applies a list of moodle tuples to the client.
    private static ICallGateProvider<string, List<MoodlesStatusInfo>, object?>? StatusInfoAppliedByPair;// Tells Moodles to apply the moodles to the client.

    // Sundouleia's Personal IPC Events.
    private static ICallGateProvider<int>?    ApiVersion; // FUNC 
    private static ICallGateProvider<object>? ListUpdated; // ACTION
    private static ICallGateProvider<object>? Ready; // FUNC
    private static ICallGateProvider<object>? Disposing; // FUNC

    public IpcProvider(ILogger<IpcProvider> logger, SundouleiaMediator mediator, SundesmoManager pairManager)
    {
        _logger = logger;
        Mediator = mediator;
        _pairManager = pairManager;

        Mediator.Subscribe<MoodlesReady>(this, (_) => NotifyListChanged());

        Mediator.Subscribe<MoodlesPermissionsUpdated>(this, (msg) =>
        {
            // get the idx to update.
            var idx = VisiblePairObjects.FindIndex(vpo => vpo.Item1.NameWithWorld == msg.User.PlayerNameWithWorld);
            if (idx >= 0)
            {
                _logger.LogInformation($"MoodlesPermissionsUpdated for [{msg.User.PlayerNameWithWorld}]", LoggerType.IpcSundouleia);
                var newPerms = _pairManager.GetMoodlePermsForPairByName(msg.User.PlayerNameWithWorld);
                VisiblePairObjects[idx] = (VisiblePairObjects[idx].Item1, newPerms.Item1, newPerms.Item2);
                // inform list change.
                NotifyListChanged();
            }
        });

        Mediator.Subscribe<VisibleUsersChanged>(this, (_) => NotifyListChanged());

        Mediator.Subscribe<UserGameObjCreatedMessage>(this, (msg) =>
        {
            _logger.LogInformation($"MoodlesPermissionsUpdated for [{msg.UserGameObj.NameWithWorld}]", LoggerType.IpcSundouleia);
            var moodlePerms = _pairManager.GetMoodlePermsForPairByName(msg.UserGameObj.NameWithWorld);
            VisiblePairObjects.Add((msg.UserGameObj, moodlePerms.Item1, moodlePerms.Item2));
            NotifyListChanged();
        });
        Mediator.Subscribe<UserGameObjDestroyedMessage>(this, (msg) =>
        {
            _logger.LogInformation($"MoodlesPermissionsUpdated for [{msg.UserGameObj.NameWithWorld}]", LoggerType.IpcSundouleia);
            VisiblePairObjects.RemoveAll(pair => pair.Item1.NameWithWorld == msg.UserGameObj.NameWithWorld);
            NotifyListChanged();
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IpcProviderService");
        // init API
        ApiVersion = Svc.PluginInterface.GetIpcProvider<int>("Sundouleia.GetApiVersion");
        // init Events
        Ready = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Ready");
        Disposing = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Disposing");
        // init Getters
        HandledVisiblePairs = Svc.PluginInterface.GetIpcProvider<List<(string, MoodlesGSpeakPairPerms, MoodlesGSpeakPairPerms)>>("Sundouleia.GetHandledVisiblePairs");
        // init appliers
        ApplyStatusesToPairRequest = Svc.PluginInterface.GetIpcProvider<string, string, List<MoodlesStatusInfo>, bool, object?>("Sundouleia.ApplyStatusesToPairRequest");
        ListUpdated = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.VisiblePairsUpdated");
        ApplyStatusInfo = Svc.PluginInterface.GetIpcProvider<MoodlesStatusInfo, object?>("Sundouleia.ApplyStatusInfo");
        ApplyStatusInfoList = Svc.PluginInterface.GetIpcProvider<List<MoodlesStatusInfo>, object?>("Sundouleia.ApplyStatusInfoList");
        StatusInfoAppliedByPair = Svc.PluginInterface.GetIpcProvider<string, List<MoodlesStatusInfo>, object?>("Sundouleia.StatusInfoAppliedByPair");

        // register api
        ApiVersion.RegisterFunc(() => SundouleiaApiVersion);
        // register getters
        HandledVisiblePairs.RegisterFunc(GetVisiblePairs);
        // register appliers
        ApplyStatusesToPairRequest.RegisterAction(HandleApplyStatusesToPairRequest);

        _logger.LogInformation("Started IpcProviderService");
        NotifyReady();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        NotifyDisposing();

        ApiVersion?.UnregisterFunc();
        Ready?.UnregisterFunc();
        Disposing?.UnregisterFunc();

        HandledVisiblePairs?.UnregisterFunc();
        ApplyStatusesToPairRequest?.UnregisterAction();
        ListUpdated?.UnregisterAction();
        ApplyStatusInfo?.UnregisterAction();
        ApplyStatusInfoList?.UnregisterAction();

        Mediator.UnsubscribeAll(this);

        return Task.CompletedTask;
    }

    private static void NotifyReady() => Ready?.SendMessage();
    private static void NotifyDisposing() => Disposing?.SendMessage();
    private static void NotifyListChanged() => ListUpdated?.SendMessage();

    private List<(string, MoodlesGSpeakPairPerms, MoodlesGSpeakPairPerms)> GetVisiblePairs()
    {
        var ret = new List<(string, MoodlesGSpeakPairPerms, MoodlesGSpeakPairPerms)>();

        return VisiblePairObjects.Where(g => g.Item1.NameWithWorld != string.Empty && g.Item1.Address != nint.Zero)
            .Select(g => ((g.Item1.NameWithWorld), (g.Item2), (g.Item3)))
            .Distinct()
            .ToList();
    }

    /// <summary> Handles the request from our clients moodles plugin to update another one of our pairs status. </summary>
    /// <param name="requester">The name of the player requesting the apply (SHOULD ALWAYS BE OUR CLIENT PLAYER) </param>
    /// <param name="recipient">The name of the player to apply the status to. (SHOULD ALWAYS BE A PAIR) </param>
    /// <param name="statuses">The list of statuses to apply to the recipient. </param>
    private void HandleApplyStatusesToPairRequest(string requester, string recipient, List<MoodlesStatusInfo> statuses, bool isPreset)
    {
        // we should throw a warning and return if the requester is not a visible pair.
        var recipientObject = VisiblePairObjects.FirstOrDefault(g => g.Item1.NameWithWorld == recipient);
        if (recipientObject.Item1 == null)
        {
            _logger.LogWarning($"ApplyStatusesToPairRequest for {recipient} recieved, but they were not visible.");
            return;
        }

        // the moodle and permissions are valid.
        var pairUser = _pairManager.DirectPairs.FirstOrDefault(p => p.PlayerNameWithWorld == recipient)!.UserData;
        if (pairUser == null)
        {
            _logger.LogWarning($"ApplyStatusesToPairRequest for {recipient} received, but couldn't find their UID.");
            return;
        }

        // fetch the UID for the pair to apply for.
        _logger.LogInformation($"ApplyStatusesToPairRequest for {recipient} from {requester}, applying statuses");
        var dto = new MoodlesApplierByStatus(pairUser, statuses, (isPreset ? MoodleType.Preset : MoodleType.Status));
        Mediator.Publish(new MoodlesApplyStatusToPair(dto));
    }

    /// <summary>
    ///     Applies a <see cref="MoodlesStatusInfo"/> tuple to the CLIENT ONLY via Moodles. <para />
    ///     This helps account for trying on Moodle Presets, or applying the preset's StatusTuples. <para />
    ///     Method is invoked via Sundouleia's IpcProvider to prevent miss-use of bypassing permissions.
    /// </summary>
    public void ApplyStatusTuple(MoodlesStatusInfo status) => ApplyStatusInfo?.SendMessage(status);

    /// <summary>
    ///     Applies a group of <see cref="MoodlesStatusInfo"/> tuples to the CLIENT ONLY via Moodles. <para />
    ///     This helps account for trying on Moodle Presets, or applying the preset's StatusTuples. <para />
    ///     Method is invoked via Sundouleia's IpcProvider to prevent miss-use of bypassing permissions.
    /// </summary>
    public void ApplyStatusTuples(IEnumerable<MoodlesStatusInfo> statuses) => ApplyStatusInfoList?.SendMessage(statuses.ToList());

    /// <summary>
    ///     Applies the list of <see cref="MoodlesStatusInfo"/> tuples to the client, that was sent by another User.
    /// </summary>
    public void ApplyMoodlesSentByUser(string kinksterNameWorld, List<MoodlesStatusInfo> statuses) => StatusInfoAppliedByPair?.SendMessage(kinksterNameWorld, statuses);
}

