//using CkCommons;
//using Dalamud.Game.ClientState.Objects.SubKinds;
//using Dalamud.Plugin.Services;
//using FFXIVClientStructs.FFXIV.Client.Game.Control;
//using FFXIVClientStructs.FFXIV.Client.Game.Object;
//using FFXIVClientStructs.FFXIV.Client.UI;
//using Microsoft.Extensions.Hosting;
//using Sundouleia.PlayerClient;
//using Sundouleia.Services.Mediator;

//namespace Sundouleia.Services;

///// <summary>
/////     Provides assistance for zone changes, cutscenes, and gpose interactions.
///// </summary>
//public class TransitionService : DisposableMediatorSubscriberBase, IHostedService
//{
//    private readonly ILogger<TransitionService> _logger;
//    private readonly SundouleiaMediator _mediator;

//    public TransitionService(ILogger<TransitionService> logger, SundouleiaMediator mediator)
//    {
//        _logger = logger;
//        _mediator = mediator;

//        Svc.ClientState.TerritoryChanged
//    }

//    public Task StartAsync(CancellationToken cancellationToken)
//    {
//        _logger.LogInformation("Starting OnFrameworkService");
//        Svc.Framework.Update += OnTick;
//        return Task.CompletedTask;
//    }

//    public Task StopAsync(CancellationToken cancellationToken)
//    {
//        _logger.LogTrace("Stopping OnFrameworkService");
//        Svc.Framework.Update -= OnTick;
//        return Task.CompletedTask;
//    }

//    private void OnTick(IFramework framework)
//    {
//        if (!PlayerData.Available)
//            return;

//        // Can just process some basic stuff and then notify the mediators.
//        var isNormal = DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1);

//        // Check for cutscene changes, but there is probably an event for this somewhere.
//        if (PlayerData.InCutscene && !_isInCutscene)
//        {
//            _logger.LogDebug("Cutscene start");
//            _isInCutscene = true;
//            _mediator.Publish(new CutsceneBeginMessage());
//        }
//        else if (!PlayerData.InCutscene && _isInCutscene)
//        {
//            _logger.LogDebug("Cutscene end");
//            _isInCutscene = false;
//            _mediator.Publish(new CutsceneEndMessage());
//        }

//        // Check for gpose changes (this also is likely worthless.
//        if (PlayerData.IsInGPose && !_isInGpose)
//        {
//            _logger.LogDebug("Gpose start");
//            _isInGpose = true;
//            _mediator.Publish(new GPoseStartMessage());
//        }
//        else if (!PlayerData.IsInGPose && _isInGpose)
//        {
//            _logger.LogDebug("Gpose end");
//            _isInGpose = false;
//            _mediator.Publish(new GPoseEndMessage());
//        }

//        // Check for zoning changes (we should move this to a service as the radar needs to watch this)
//        if (PlayerData.IsZoning)
//        {
//            var zone = PlayerContent.TerritoryID;
//            if (_lastZone != zone)
//            {
//                _lastZone = zone;
//                if (!_sentBetweenAreas)
//                {
//                    _logger.LogDebug("Zone switch start");
//                    _sentBetweenAreas = true;
//                    _mediator.Publish(new ZoneSwitchStartMessage(_lastZone));
//                }
//            }
//            return;
//        }

//        // this is called while are zoning between areas has ended
//        if (_sentBetweenAreas)
//        {
//            _logger.LogDebug("Zone switch end");
//            _sentBetweenAreas = false;
//            _mediator.Publish(new ZoneSwitchEndMessage());
//        }

//        if (isNormal)
//            return;
//        _mediator.Publish(new DelayedFrameworkUpdateMessage());
//        _delayedFrameworkUpdateCheck = DateTime.Now;
//    }
//}