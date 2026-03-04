using CkCommons;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.PlayerClient;

public class LociMonitor : DisposableMediatorSubscriberBase
{
    private readonly SundesmoManager _sundesmos;
    private readonly ClientDistributor _distributor;
    public LociMonitor(ILogger<LociMonitor> logger, SundouleiaMediator mediator,
        SundesmoManager sundesmos, ClientDistributor distributor)
        : base(logger, mediator)
    {
        _sundesmos = sundesmos;
        _distributor = distributor;

        IpcProviderLoci.OnStatusModifiedCalled += OnStatusModified;
        IpcProviderLoci.OnPresetModifiedCalled += OnPresetModified;
        IpcProviderLoci.OnApplyToTargetCalled += OnApplyToTarget;
        IpcProviderLoci.OnApplyToTargetBulkCalled += OnApplyToTargetBulk;
        // Since we init the IPC, Loci will always be ready by the time this is called.
        OnLociReady();
        Mediator.Subscribe<LociSharePermChanged>(this, _ => LociDataSharePermsUpdate(_.Sundesmo));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IpcProviderLoci.OnStatusModifiedCalled -= OnStatusModified;
        IpcProviderLoci.OnPresetModifiedCalled -= OnPresetModified;
        IpcProviderLoci.OnApplyToTargetCalled -= OnApplyToTarget;
        IpcProviderLoci.OnApplyToTargetBulkCalled -= OnApplyToTargetBulk;
    }

    private async void LociDataSharePermsUpdate(Sundesmo sundesmo)
    {
        if (!sundesmo.OwnPerms.ShareOwnLociData)
            return;
        // Push all data to them.
        await _distributor.PushLociData([sundesmo.UserData]).ConfigureAwait(false);
    }

    public async void OnLociReady()
    {
        Logger.LogDebug("Loci ready, pushing to all trusted pairs", LoggerType.IpcLoci);
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociData(trusted);
    }

    public async void OnStatusModified(LociStatus status, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociStatusUpdate(trusted, status.ToTuple(), wasDeleted);
    }

    public async void OnPresetModified(LociPreset preset, bool wasDeleted)
    {
        if (PlayerData.IsZoning || !PlayerData.Available)
            return;
        // push the update.
        var trusted = _sundesmos.DirectPairs.Where(x => x.IsRendered && x.OwnPerms.ShareOwnLociData).Select(p => p.UserData).ToList();
        await _distributor.PushLociPresetUpdate(trusted, preset.ToTuple(), wasDeleted);
    }

    private async void OnApplyToTarget(nint targetAddr, string targetHost, LociStatusInfo data)
        => OnApplyToTargetBulk(targetAddr, targetHost, [data]);

    private async void OnApplyToTargetBulk(nint targetAddr, string targetHost, List<LociStatusInfo> data)
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
        if (!LociEx.CanApply(match.PairPerms, data))
            return;
        // It is valid, so push the update out
        await _distributor.PushLociApplyToTarget(match.UserData, data).ConfigureAwait(false);
    }
}
