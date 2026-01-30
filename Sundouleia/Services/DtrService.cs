using CkCommons;
using Dalamud.Game.Gui.Dtr;
using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary>
/// The service responsible for handling framework updates and other Dalamud related services.
/// </summary>
public sealed class DtrService : DisposableMediatorSubscriberBase, IHostedService
{
    private const string REQUESTS_NAME = "SundouleiaRequests";
    private const string RADAR_NAME = "SundouleiaRadar";
    private const string SUNDESMOS_NAME = "SundouleiaSundesmos";

    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;
    private readonly RadarManager _radar;

    // Potential DTRBar Entries.
    private IDtrBarEntry requestsEntry;
    private IDtrBarEntry radarEntry;
    private IDtrBarEntry sundesmosEntry;
    // private readonly IDtrBarEntry _unreadChat;

    // These could be processed in a task but they also do not need to be.
    // They only really need to be updated when something changes the state.

    public DtrService(ILogger<DtrService> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, SundesmoManager sundesmos,
        RequestsManager requests, RadarManager radar)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _sundesmos = sundesmos;
        _requests = requests;
        _radar = radar;

        // Set defaults.
        requestsEntry = Svc.DtrBar.Get(REQUESTS_NAME);
        requestsEntry.Shown = false;
        radarEntry = Svc.DtrBar.Get(RADAR_NAME);
        radarEntry.Shown = false;
        sundesmosEntry = Svc.DtrBar.Get(SUNDESMOS_NAME);
        sundesmosEntry.Shown = false;

        Mediator.Subscribe<FolderUpdateRequests>(this, _ => OnRequestsUpdated());
        Mediator.Subscribe<FolderUpdateRadar>(this, _ => OnRadarUpdated());
        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => OnSundesmosUpdated());

        // Poll a general refresh.
        RefreshEntries();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        requestsEntry.Remove();
        requestsEntry = null!;
        radarEntry.Remove();
        radarEntry = null!;
        sundesmosEntry.Remove();
        sundesmosEntry = null!;
    }

    private void RefreshEntries()
    {
        OnRequestsUpdated();
        OnRadarUpdated();
        OnSundesmosUpdated();

    }

    private void OnRequestsUpdated()
    {
        requestsEntry.Shown = false;
        if (!_config.Current.RequestNotifiers.HasAny(RequestAlertKind.DtrBar))
            return;

        // Otherwise create the entry for it and show it.
    }

    private void OnRadarUpdated()
    {
        requestsEntry.Shown = false;
        if (!_config.Current.RadarNearbyDtr)
            return;
        // Otherwise create the entry for it and show it.
    }

    private void OnSundesmosUpdated()
    {

    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("DTR Bar Service Starting");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("DTR Bar Service Stopping");
    }
}

