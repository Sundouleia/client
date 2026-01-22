using CkCommons;
using GagspeakAPI.Data;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Stores the client's Moodle StatusManager and info of their Statuses and Presets. <para/>
///     When these are modified, updates are sent to their respective callers.
/// </summary>
public class ClientMoodles : DisposableMediatorSubscriberBase
{
    private readonly IpcProvider _ipcProvider;
    private readonly IpcCallerMoodles _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly ClientDistributor _distributor;

    public static readonly MoodleData Data = new();

    public ClientMoodles(ILogger<ClientMoodles> logger, SundouleiaMediator mediator,
        IpcProvider ipcProvider, IpcCallerMoodles moodles, SundesmoManager sundesmos,
        ClientDistributor distributor)
        : base(logger, mediator)
    {
        _ipcProvider = ipcProvider;
        _ipc = moodles;
        _sundesmos = sundesmos;
        _distributor = distributor;

        _ipc.OnStatusManagerModified.Subscribe(OnStatusManagerModified);
        _ipc.OnStatusUpdated.Subscribe((id, deleted) => _ = OnStatusModified(id, deleted));
        _ipc.OnPresetUpdated.Subscribe((id, deleted) => _ = OnPresetModified(id, deleted));

        // if the moodles API is already available by the time this loads, run OnMoodlesReady.
        // This lets us account for the case where we load before Moodles does.
        if (IpcCallerMoodles.APIAvailable)
            OnMoodlesReady();

        Mediator.Subscribe<MoodleSharePermChanged>(this, _ => MoodleSharePermUpdate(_.Sundesmo));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _ipc.OnStatusManagerModified.Unsubscribe(OnStatusManagerModified);
        _ipc.OnStatusUpdated.Unsubscribe((id, deleted) => _ = OnStatusModified(id, deleted));
        _ipc.OnPresetUpdated.Unsubscribe((id, deleted) => _ = OnPresetModified(id, deleted));
    }

    private async void MoodleSharePermUpdate(Sundesmo sundesmo)
    {
        // Only send when true.
        if (!sundesmo.OwnPerms.ShareOwnMoodles)
            return;
        // Push all data to them.
        await _distributor.PushMoodlesData([sundesmo.UserData]).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Get all info from moodles to store in the cache and distribute to others.
    /// </summary>
    public async void OnMoodlesReady()
    {
        var statuses = await _ipc.GetStatusListDetails().ConfigureAwait(false);
        var presets = await _ipc.GetPresetListDetails().ConfigureAwait(false);
        Data.SetStatuses(statuses);
        Data.SetPresets(presets);
        Logger.LogDebug("Moodles ready, pushing to all trusted pairs", LoggerType.IpcMoodles);
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodlesData(trusted);
    }


    private async void OnStatusManagerModified(nint addr)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (addr != PlayerData.Address)
            return;

        // We had an update for ourselves, fetch latest data and then push to visible immediatly.
        Data.UpdateDataInfo(await _ipc.GetOwnDataInfo().ConfigureAwait(false));
        Mediator.Publish(new MoodlesSMChanged(addr));
        Svc.Logger.Debug($"Client Status manager modified", LoggerType.IpcMoodles);
    }

    public async Task OnStatusModified(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        Svc.Logger.Debug($"Status modified: {id} (deleted: {wasDeleted})", LoggerType.IpcMoodles);

        if (wasDeleted)
            Data.Statuses.Remove(id);
        else
            Data.TryUpdateStatus(await _ipc.GetStatusDetails(id));

        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodleStatusUpdate(trusted, Data.Statuses[id], wasDeleted);
    }

    /// <summary> Fired whenever we change any setting in any of our Moodles Presets via the Moodles UI </summary>
    public async Task OnPresetModified(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        Svc.Logger.Debug($"Preset modified: {id} (deleted: {wasDeleted})", LoggerType.IpcMoodles);

        if (wasDeleted)
            Data.Presets.Remove(id);
        else
            Data.TryUpdatePreset(await _ipc.GetPresetDetails(id));

        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodlePresetUpdate(trusted, Data.Presets[id], wasDeleted);
    }
}
