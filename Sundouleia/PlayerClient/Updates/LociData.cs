using CkCommons;
using LociApi.Ipc;
using LociApi.Helpers;
using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.PlayerClient;

public class LociData : DisposableMediatorSubscriberBase
{
    private readonly IpcCallerLoci _loci;
    private readonly SundesmoManager _sundesmos;
    private readonly ClientDistributor _distributor;

    // Events to listen to and set in the initializer
    private readonly EventSubscriber<nint, string, List<LociStatusInfo>> ApplyToTargetSent;
    private readonly EventSubscriber<Guid, bool> StatusUpdated;
    private readonly EventSubscriber<Guid, bool> PresetUpdated;
    public LociData(ILogger<LociData> logger, SundouleiaMediator mediator,
        IpcCallerLoci loci, SundesmoManager sundesmos, ClientDistributor distributor)
        : base(logger, mediator)
    {
        _loci = loci;
        _sundesmos = sundesmos;
        _distributor = distributor;

        StatusUpdated = LociApi.Ipc.StatusUpdated.Subscriber(Svc.PluginInterface, OnStatusUpdated);
        PresetUpdated = LociApi.Ipc.PresetUpdated.Subscriber(Svc.PluginInterface, OnPresetUpdated);
        ApplyToTargetSent = LociApi.Ipc.ApplyToTargetSent.Subscriber(Svc.PluginInterface, OnApplyToTarget);
        // Can listen to other events if needed down the line.
        if (IpcCallerLoci.APIAvailable)
            OnLociReady();

        Mediator.Subscribe<LociSharePermChanged>(this, _ => LociDataSharePermsUpdate(_.Sundesmo));
    }

    // Static accessible data cached from Loci.
    public static readonly LociContainer Cache = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        StatusUpdated.Dispose();
        PresetUpdated.Dispose();
        ApplyToTargetSent.Dispose();
    }

    private async void LociDataSharePermsUpdate(Sundesmo sundesmo)
    {
        if (!sundesmo.OwnPerms.ShareOwnLociData) return;
        await _distributor.PushLociData([sundesmo.UserData]).ConfigureAwait(false);
    }

    public async void OnLociReady()
    {
        Logger.LogDebug("Loci ready, pushing to all trusted pairs", LoggerType.IpcLoci);
        var dataInfo = await _loci.GetOwnManagerInfo().ConfigureAwait(false);
        var statuses = await _loci.GetStatusInfos().ConfigureAwait(false);
        var presets = await _loci.GetPresetInfos().ConfigureAwait(false);
        Cache.SetDataInfo(dataInfo.Select(di => di.ToStruct()));
        Cache.SetStatuses(statuses.Select(s => s.ToStruct()));
        Cache.SetPresets(presets.Select(p => p.ToStruct()));
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociData(trusted);
    }

    public async void OnStatusUpdated(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (wasDeleted)
            Cache.Statuses.Remove(id);
        else
            Cache.Statuses[id] = (await _loci.GetStatusInfo(id).ConfigureAwait(false)).ToStruct();

        // push the update.
        var toPush = wasDeleted ? new() : Cache.Statuses[id];
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociStatusUpdate(trusted, toPush, wasDeleted);
    }

    public async void OnPresetUpdated(Guid id, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;

        if (wasDeleted)
            Cache.Presets.Remove(id);
        else
            Cache.Presets[id] = (await _loci.GetPresetInfo(id).ConfigureAwait(false)).ToStruct();

        // push the update.
        var toPush = wasDeleted ? new() : Cache.Presets[id];
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociPresetUpdate(trusted, toPush, wasDeleted);
    }

    public async void OnPresetModified(LociPresetInfo preset, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociPresetUpdate(trusted, preset.ToStruct(), wasDeleted);
    }

    private async void OnApplyToTarget(nint targetAddr, string targetHost, List<LociStatusInfo> data)
    {
        // Ignore if not for us
        if (!string.Equals(targetHost, IpcManager.LOCI_REGISTER_TAG, StringComparison.Ordinal))
            return;
        // Ignore if zoning or unavailable
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // Try to locate the Sundesmo
        if (_sundesmos.DirectPairs.FirstOrDefault(x => x.IsRendered && x.PlayerAddress == targetAddr) is not { } match)
            return;
        // Ensure we have the correct permissions to apply to them.
        if (!SundouleiaEx.CanApply(match.PairPerms, data))
            return;
        // It is valid, so push the update out
        await _distributor.PushLociApplyToTarget(match.UserData, data).ConfigureAwait(false);
    }
}
