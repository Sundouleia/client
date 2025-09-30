using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Services;

/// <summary>
///     Slowly phase out this unessisary service.
/// </summary>
public class OnFrameworkService : DisposableMediatorSubscriberBase, IHostedService
{
    private DateTime _delayedFrameworkUpdateCheck = DateTime.Now;
    // Tracks the start and endpoints of these transitions / activities.
    private uint _lastZone = 0;
    private bool _sentBetweenAreas = false;
    private bool _isInGpose = false;
    private bool _isInCutscene = false;

    public OnFrameworkService(ILogger<OnFrameworkService> logger, SundouleiaMediator mediator) 
        : base(logger, mediator)
    {
        // Move to the sundesmoManager
        mediator.Subscribe<TargetPairMessage>(this, (msg) =>
        {
            if (PlayerData.IsInPvP) return;
            var name = msg.Pair.Name;
            if (string.IsNullOrEmpty(name)) return;
            var addr = _playerCharas.FirstOrDefault(f => string.Equals(f.Value.Name, name, StringComparison.Ordinal)).Value.Address;
            if (addr == nint.Zero) return;
            _ = RunOnFrameworkThread(() => Svc.Targets.Target = Svc.Objects.CreateObjectReference(addr)).ConfigureAwait(false);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting OnFrameworkService");
        Svc.Framework.Update += FrameworkOnUpdate;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogTrace("Stopping OnFrameworkService");
        Svc.Framework.Update -= FrameworkOnUpdate;
        return Task.CompletedTask;
    }

    public void EnsureIsOnFramework()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread) throw new InvalidOperationException("Can only be run on Framework");
    }

    public async Task<IPlayerCharacter?> GetIPlayerCharacterFromObjectTableAsync(IntPtr address)
    {
        return await RunOnFrameworkThread(() => (IPlayerCharacter?)Svc.Objects.CreateObjectReference(address)).ConfigureAwait(false);
    }

    /// <summary> Run the task on the framework thread </summary>
    /// <param name="act">an action to run if any</param>
    public async Task RunOnFrameworkThread(Action act)
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
        {
            await Svc.Framework.RunOnFrameworkThread(act).ContinueWith((_) => Task.CompletedTask).ConfigureAwait(false);
            while (Svc.Framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
            {
                Logger.LogTrace("Still on framework");
                await Task.Delay(1).ConfigureAwait(false);
            }
        }
        else
        {
            act();
        }
    }
    /// <summary> Run the task on the framework thread </summary>
    /// <param name="func">a function to run if any</param>"
    public async Task<T> RunOnFrameworkThread<T>(Func<T> func)
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
        {
            var result = await Svc.Framework.RunOnFrameworkThread(func).ContinueWith((task) => task.Result).ConfigureAwait(false);
            while (Svc.Framework.IsInFrameworkUpdateThread) // yield the thread again, should technically never be triggered
            {
                Logger.LogTrace("Still on framework");
                await Task.Delay(1).ConfigureAwait(false);
            }
            return result;
        }

        return func.Invoke();
    }

    /// <summary> The method that is called when the framework updates </summary>
    private void FrameworkOnUpdate(IFramework framework) 
        => FrameworkOnUpdateInternal();

    private unsafe void FrameworkOnUpdateInternal()
    {
        if (!PlayerData.Available)
            return;

        _notUpdatedCharas.AddRange(_playerCharas.Keys);

        // check if we are in the middle of a delayed framework update
        var isNormalFrameworkUpdate = DateTime.Now < _delayedFrameworkUpdateCheck.AddSeconds(1);

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
            // do an early return so we dont hit the sentBetweenAreas conditional below
            return;
        }

        // this is called while are zoning between areas has ended
        if (_sentBetweenAreas)
        {
            Logger.LogDebug("Zone switch end");
            _sentBetweenAreas = false;
            Mediator.Publish(new ZoneSwitchEndMessage());
        }

        Mediator.Publish(new FrameworkUpdateMessage());
        if (isNormalFrameworkUpdate)
            return;

        // push the delayed framework update message to the mediator for things like the UI and the online player manager
        Mediator.Publish(new DelayedFrameworkUpdateMessage());
        _delayedFrameworkUpdateCheck = DateTime.Now;
    }
}