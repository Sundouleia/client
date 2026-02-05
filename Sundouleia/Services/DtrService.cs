using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Microsoft.Extensions.Hosting;
using OtterGui.Classes;
using Sundouleia.Gui.Components;
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

        Mediator.Subscribe<ConnectedMessage>(this, _ => RefreshEntries());

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

    public void RefreshEntries()
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

        if (!MainHub.IsConnectionDataSynced)
            return;

        if (_requests.Incoming.Count is 0)
            return;

        requestsEntry.Shown = true;
        requestsEntry.OnClick = _ => Mediator.Publish(new OpenMainUiTab(MainMenuTabs.SelectedTab.Requests));
        var tooltip = new SeStringBuilder();
        var entryTxt = new SeStringBuilder();
        // Create the entry display.
        entryTxt.AddIcon(BitmapFontIcon.VentureDeliveryMoogle);
        entryTxt.AddText($"{_requests.Incoming.Count}");
        tooltip.AddYellow($"{_requests.Incoming.Count} Incoming Requests\n");
        foreach (var req in _requests.Incoming)
        {
            tooltip.AddIcon(req.IsTemporaryRequest ? BitmapFontIcon.GoldStar : BitmapFontIcon.BlueStar);
            tooltip.AddText($" {req.SenderAnonName}\n");
        }
        requestsEntry.Text = entryTxt.BuiltString;
        requestsEntry.Tooltip = tooltip.BuiltString;
    }

    private void OnRadarUpdated()
    {
        radarEntry.Shown = false;
        if (!_config.Current.RadarNearbyDtr)
            return;

        if (!MainHub.IsConnectionDataSynced)
            return;

        if (_radar.RadarUsers.Count is 0)
            return;

        // Otherwise create the entry for it and show it.
        radarEntry.Shown = true;
        radarEntry.OnClick = _ => Mediator.Publish(new OpenMainUiTab(MainMenuTabs.SelectedTab.Radar));
        // Compile all possible icons into a single string.
        var entryTxt = new SeStringBuilder();
        var tooltip = new SeStringBuilder();
        entryTxt.AddIcon(BitmapFontIcon.Recording);
        entryTxt.AddText($"{_radar.RadarUsers.Count}");
        // Devise the tooltip.
        var total = _radar.RadarUsers.Count;
        var totalPaired = _radar.RadarUsers.Count(u => u.IsPaired);
        var totalLurkers = _radar.RadarUsers.Count(u => !u.IsValid);

        tooltip.AddIcon(BitmapFontIcon.AnyClass);
        tooltip.AddText($"{total} Radar Users\n");
        tooltip.AddIcon(BitmapFontIcon.LevelSync);
        tooltip.AddText($"{totalPaired} Paired Radar Users\n");
        tooltip.AddIcon(BitmapFontIcon.DoNotDisturb);
        tooltip.AddText($"{totalLurkers} Lurkers");
        radarEntry.Text = entryTxt.BuiltString;
        radarEntry.Tooltip = tooltip.BuiltString;
    }

    private void OnSundesmosUpdated()
    {
        sundesmosEntry.Shown = false;
        if (!MainHub.IsConnectionDataSynced)
            return; 
        
        var onlinePairs = _sundesmos.GetOnlineSundesmos();
        if (!_config.Current.EnablePairDtr || onlinePairs.Count is 0)
            return;

        sundesmosEntry.Shown = true;
        sundesmosEntry.OnClick = _ => Mediator.Publish(new OpenMainUiTab(MainMenuTabs.SelectedTab.BasicWhitelist));
        var tooltip = new SeStringBuilder();
        // Otherwise create the entry for it and show it.
        var pairCount = onlinePairs.Count;
        sundesmosEntry.Text = new SeString(new TextPayload($"\uE044 {pairCount}"));
        
        var visible = onlinePairs.Count(p => p.IsRendered);
        tooltip.AddText($"{visible} Visible\n");
        tooltip.AddText($"{pairCount} Online");
        sundesmosEntry.Tooltip = tooltip.BuiltString;
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

