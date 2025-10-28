using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;

namespace Sundouleia.PlayerClient;
/// <summary> 
///     Manages all sundesmo requests for the client. <para />
///     This includes both incoming and outgoing requests.
/// </summary>
public sealed class RequestsManager
{
    private readonly ILogger<RequestsManager> _logger;
    private readonly SundouleiaMediator _mediator;

    private List<SundesmoRequest> _incomingRequests = [];
    private List<SundesmoRequest> _outgoingRequests = [];

    public RequestsManager(ILogger<RequestsManager> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    public int TotalIncoming => _incomingRequests.Count;
    public int TotalOutgoing => _outgoingRequests.Count;
    public IReadOnlyList<SundesmoRequest> Incoming => _incomingRequests;
    public IReadOnlyList<SundesmoRequest> Outgoing => _outgoingRequests;

    public void LoadInitial(List<SundesmoRequest> allRequests)
    {
        // Filter the requests based on the user and target.
        _incomingRequests = allRequests.Where(r => r.Target.UID == MainHub.UID).ToList();
        _outgoingRequests = allRequests.Where(r => r.User.UID == MainHub.UID).ToList();
        _logger.LogDebug($"Loaded {_incomingRequests.Count} incoming and {_outgoingRequests.Count} outgoing requests", LoggerType.PairManagement);
        _mediator.Publish(new RefreshFolders(false, false, true));
    }

    public void AddRequest(SundesmoRequest request)
    {
        if (request.Target.UID == MainHub.UID)
        {
            _incomingRequests.Add(request);
            _logger.LogDebug($"Added incoming request", LoggerType.PairManagement);
        }
        else if (request.User.UID == MainHub.UID)
        {
            _outgoingRequests.Add(request);
            _logger.LogDebug($"Added outgoing request", LoggerType.PairManagement);
        }
        _mediator.Publish(new RefreshFolders(false, false, true));
    }

    public void RemoveRequest(SundesmoRequest request)
    {
        if (request.Target.UID == MainHub.UID)
        {
            _incomingRequests.Remove(request);
            _logger.LogDebug($"Removed incoming request", LoggerType.PairManagement);
        }
        else if (request.User.UID == MainHub.UID)
        {
            _outgoingRequests.Remove(request);
            _logger.LogDebug($"Removed outgoing request", LoggerType.PairManagement);
        }
        _mediator.Publish(new RefreshFolders(false, false, true));
    }
}
