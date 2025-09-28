using Sundouleia.Interop;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary> 
///     Processes updates to Client-Owned object data efficiently without 
///     flooding the mediator via direct calls. <para />
///     Updates are put on a delay timer based on their type, and an update 
///     is fired to the mediator with the given types once it completes.
/// </summary>
public sealed class ClientUpdateService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;

    private Task? _updateTask;
    private CancellationTokenSource _updateCTS = new();

    public ClientUpdateService(ILogger<ClientUpdateService> logger, SundouleiaMediator mediator,
        IpcManager ipc) : base(logger, mediator)
    {
        _ipc = ipc;

        // Listen to the various changes

    }

}
