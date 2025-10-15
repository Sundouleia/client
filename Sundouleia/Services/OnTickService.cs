using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Microsoft.Extensions.Hosting;
using Sundouleia.PlayerClient;
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
    private bool _isInGPose = false;
    private bool _isInCutscene = false;

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
        if (PlayerData.InCutscene && !_isInCutscene)
        {
            _logger.LogDebug("Cutscene start");
            _isInCutscene = true;
            _mediator.Publish(new CutsceneBeginMessage());
        }
        else if (!PlayerData.InCutscene && _isInCutscene)
        {
            _logger.LogDebug("Cutscene end");
            _isInCutscene = false;
            _mediator.Publish(new CutsceneEndMessage());
        }

        // Check for GPose changes (this also is likely worthless.
        if (PlayerData.IsInGPose && !_isInGPose)
        {
            _logger.LogDebug("Gpose start");
            _isInGPose = true;
            _mediator.Publish(new GPoseStartMessage());
        }
        else if (!PlayerData.IsInGPose && _isInGPose)
        {
            _logger.LogDebug("Gpose end");
            _isInGPose = false;
            _mediator.Publish(new GPoseEndMessage());
        }

        if (isNormal)
            return;
        _mediator.Publish(new DelayedFrameworkUpdateMessage());
        _delayedFrameworkUpdateCheck = DateTime.Now;
    }
}