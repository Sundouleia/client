using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using GagspeakAPI.Data;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using TerraFX.Interop.Windows;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Manages the cached moodle data and listens for updates to keep it in sync.
/// </summary>
public class MoodlesCacheService : DisposableMediatorSubscriberBase
{
    private readonly IpcProvider _ipcProvider;
    private readonly IpcCallerMoodles _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly DistributionService _distributor;

    public static readonly MoodleData MoodleCache = new();

    public MoodlesCacheService(ILogger<MoodlesCacheService> logger, SundouleiaMediator mediator,
        IpcProvider ipcProvider, IpcCallerMoodles moodles, SundesmoManager sundesmos,
        DistributionService distributor)
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
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _ipc.OnStatusManagerModified.Unsubscribe(OnStatusManagerModified);
        _ipc.OnStatusUpdated.Unsubscribe((id, deleted) => _ = OnStatusModified(id, deleted));
        _ipc.OnPresetUpdated.Unsubscribe((id, deleted) => _ = OnPresetModified(id, deleted));
    }

    private void OnStatusManagerModified(nint addr) => Mediator.Publish(new MoodlesChanged(addr));

    /// <summary> 
    ///     Get all info from moodles to store in the cache and distribute to others.
    /// </summary>
    public async void OnMoodlesReady()
    {
        var statuses = await _ipc.GetStatusListDetails().ConfigureAwait(false);
        var presets = await _ipc.GetPresetListDetails().ConfigureAwait(false);
        MoodleCache.SetStatuses(statuses);
        MoodleCache.SetPresets(presets);
        Logger.LogDebug("Moodles ready, pushing to all trusted pairs", LoggerType.IpcMoodles);
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodlesData(trusted);
    }

    public async Task OnStatusModified(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (wasDeleted)
            MoodleCache.Statuses.Remove(id);
        else
            MoodleCache.TryUpdateStatus(await _ipc.GetStatusDetails(id));

        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodleStatusUpdate(trusted, MoodleCache.Statuses[id], wasDeleted);
    }

    /// <summary> Fired whenever we change any setting in any of our Moodles Presets via the Moodles UI </summary>
    public async Task OnPresetModified(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (wasDeleted)
            MoodleCache.Presets.Remove(id);
        else
            MoodleCache.TryUpdatePreset(await _ipc.GetPresetDetails(id));

        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnMoodles).Select(p => p.UserData).ToList();
        await _distributor.PushMoodlePresetUpdate(trusted, MoodleCache.Presets[id], wasDeleted);
    }
}
