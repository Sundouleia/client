using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Hosting;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Services;

/// <summary>
///     Only value is for delayed framework updates and other small things now. 
///     Has no other purpose.
/// </summary>
public class OnTickService : DisposableMediatorSubscriberBase, IHostedService
{
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    // Tracks the start and endpoints of these transitions / activities.
    private uint _lastZone = 0;
    private bool _sentBetweenAreas = false;
    private bool _isInGpose = false;
    private bool _isInCutscene = false;

    public OnTickService(ILogger<OnTickService> logger, SundouleiaMediator mediator, MainConfig config)
        : base(logger, mediator)
    {
        // Move to the sundesmoManager
        mediator.Subscribe<TargetSundesmoMessage>(this, (msg) =>
        {
            // Fail in pvp or when not rendered.
            if (PlayerData.IsInPvP || !msg.Sundesmo.PlayerRendered)
                return;
            unsafe
            {
                if (config.Current.FocusTargetOverTarget)
                    TargetSystem.Instance()->FocusTarget = (GameObject*)msg.Sundesmo.GetAddress(OwnedObject.Player);
                else
                    TargetSystem.Instance()->SetHardTarget((GameObject*)msg.Sundesmo.GetAddress(OwnedObject.Player));
            }
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting OnFrameworkService");
        Svc.Framework.Update += OnTick;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogTrace("Stopping OnFrameworkService");
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
            Logger.LogDebug("Cutscene start");
            _isInCutscene = true;
            Mediator.Publish(new CutsceneBeginMessage());
        }
        else if (!PlayerData.InCutscene && _isInCutscene)
        {
            Logger.LogDebug("Cutscene end");
            _isInCutscene = false;
            Mediator.Publish(new CutsceneEndMessage());
        }

        // Check for gpose changes (this also is likely worthless.
        if (PlayerData.IsInGPose && !_isInGpose)
        {
            Logger.LogDebug("Gpose start");
            _isInGpose = true;
            Mediator.Publish(new GPoseStartMessage());
        }
        else if (!PlayerData.IsInGPose && _isInGpose)
        {
            Logger.LogDebug("Gpose end");
            _isInGpose = false;
            Mediator.Publish(new GPoseEndMessage());
        }

        // Check for zoning changes (we should move this to a service as the radar needs to watch this)
        if (PlayerData.IsZoning)
        {
            var zone = PlayerContent.TerritoryID;
            if (_lastZone != zone)
            {
                _lastZone = zone;
                if (!_sentBetweenAreas)
                {
                    Logger.LogDebug("Zone switch start");
                    _sentBetweenAreas = true;
                    Mediator.Publish(new ZoneSwitchStartMessage(_lastZone));
                }
            }
            return;
        }

        // this is called while are zoning between areas has ended
        if (_sentBetweenAreas)
        {
            Logger.LogDebug("Zone switch end");
            _sentBetweenAreas = false;
            Mediator.Publish(new ZoneSwitchEndMessage());
        }

        if (isNormal)
            return;
        Mediator.Publish(new DelayedFrameworkUpdateMessage());
        _delayedFrameworkUpdateCheck = DateTime.Now;
    }
}