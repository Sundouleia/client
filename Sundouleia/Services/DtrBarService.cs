using CkCommons;
using Dalamud.Game.Gui.Dtr;
using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary>
/// The service responsible for handling framework updates and other Dalamud related services.
/// </summary>
public sealed class DtrBarService : IDisposable, IHostedService
{
    private readonly ILogger<DtrBarService> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmos;

    // Lazy list of our current entries.
    private readonly Lazy<IDtrBarEntry> _radarDtr;
    private readonly Lazy<IDtrBarEntry> _radarChatDtr;

    private readonly CancellationTokenSource _dtrCTS = new();
    private Task _dtrTask;

    // Refactor this later, but not now.
    public DtrBarService(ILogger<DtrBarService> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, SundesmoManager sundesmos)
    {
        _logger = logger;
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _sundesmos = sundesmos;

        _radarDtr = new(CreateRadarEntry);
        _radarChatDtr = new(CreateChatEntry);
    }

    public void Dispose()
    {
        if (_radarDtr.IsValueCreated)
        {
            _logger.LogDebug("Removing DTR Radar Entry");
            // clear any data here.
            _radarDtr.Value.Remove();
        }
        if (_radarChatDtr.IsValueCreated)
        {
            _logger.LogDebug("Removing DTR Chat Radar Entry");
            // clear any data here.
            _radarChatDtr.Value.Remove();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DTR Bar Service Starting");
        _dtrTask = Task.Run(DtrBarLoop, _dtrCTS.Token);
        _logger.LogInformation("DTR Bar Service Started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DTR Bar Service Stopping");
        _dtrCTS.SafeCancel();
        await Generic.Safe(async () => await _dtrTask.ConfigureAwait(false));
        _dtrCTS.SafeDispose();
        _logger.LogInformation("DTR Bar Service Stopped");
    }

    // Hides the DTR entry from display.
    private void ClearRadar()
    {
        // Ignore if nothing created.
        if (!_radarDtr.IsValueCreated) 
            return;
        _logger.LogInformation("Removing DTR Radar Entry");
        _radarDtr.Value.Shown = false;
    }

    private void ClearRadarChat()
    {
        // Ignore if nothing created.
        if (!_radarChatDtr.IsValueCreated)
            return;
        _logger.LogInformation("Removing DTR Chat Radar Entry");
        _radarChatDtr.Value.Shown = false;
    }

    // for sundesmos in territory.
    private IDtrBarEntry CreateRadarEntry()
    {
        _logger.LogTrace("Creating new DTR Radar Entry");
        var entry = Svc.DtrBar.Get("Sundouleia Radar");
        entry.OnClick = _ => { /* Can do fun voodoo here! */ };
        return entry;
    }

    // For unread chat if enabled.
    private IDtrBarEntry CreateChatEntry()
    {
        _logger.LogTrace("Creating new DTR Chat Entry");
        var entry = Svc.DtrBar.Get("Sundouleia Chat Radar");
        entry.OnClick = _ => { /* Can do fun voodoo here! */ };
        return entry;
    }

    private async Task DtrBarLoop()
    {
        while (!_dtrCTS.IsCancellationRequested)
        {
            await Task.Delay(1000, _dtrCTS.Token).ConfigureAwait(false);
            ProcessUpdate();
        }
    }

    private void ProcessUpdate()
    {
        // process the entry stuff here, enabling or disabling and displaying appropriate info as nessisary.
        // This is a WIP.
    }


}

