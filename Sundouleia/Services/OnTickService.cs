using CkCommons;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Services;

/// <summary>
///     Only value is for delayed framework updates and other small things now. 
///     Has no other purpose.
/// </summary>
public unsafe class OnTickService : IHostedService
{
    private readonly ILogger<OnTickService> _logger;
    private readonly SundouleiaMediator _mediator;

    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    // Conditions we want to track.
    public static bool InGPose { get; private set; } = false;
    public static bool InCutscene { get; private set; } = false;

    public OnTickService(ILogger<OnTickService> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting OnFrameworkService");
        Svc.Framework.Update += OnTick;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogTrace("Stopping OnFrameworkService");
        Svc.Framework.Update -= OnTick;
        return Task.CompletedTask;
    }


    private void OnTick(IFramework framework)
    {
        if (!PlayerData.Available)
            return;

        // Can just process some basic stuff and then notify the mediators.
        var isNormal = DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1);

        // Check for cutscene changes, but there is probably an event for this somewhere.
        if (PlayerData.InCutscene && !InCutscene)
        {
            _logger.LogDebug("Cutscene start");
            InCutscene = true;
            _mediator.Publish(new CutsceneBeginMessage());
        }
        else if (!PlayerData.InCutscene && InCutscene)
        {
            _logger.LogDebug("Cutscene end");
            InCutscene = false;
            _mediator.Publish(new CutsceneEndMessage());
        }

        // Check for GPose changes (this also is likely worthless.
        if (PlayerData.IsInGPose && !InGPose)
        {
            _logger.LogDebug("Gpose start");
            InGPose = true;
            _mediator.Publish(new GPoseStartMessage());
        }
        else if (!PlayerData.IsInGPose && InGPose)
        {
            _logger.LogDebug("Gpose end");
            InGPose = false;
            _mediator.Publish(new GPoseEndMessage());
        }

        if (isNormal)
            return;
        _mediator.Publish(new DelayedFrameworkUpdateMessage());
        _delayedFrameworkUpdateCheck = DateTime.Now;
    }
}