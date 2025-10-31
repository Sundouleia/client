using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Manages all sundesmo requests for the client. <para />
///     This includes both incoming and outgoing requests.
/// </summary>
public sealed class RequestsManager : DisposableMediatorSubscriberBase
{
    private HashSet<RequestEntry> _allRequests = new();

    // Lazily created lists from the full request list.
    private Lazy<List<RequestEntry>> _incomingInternal;
    private Lazy<List<RequestEntry>> _outgoingInternal;

    public RequestsManager(ILogger<RequestsManager> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
#if DEBUG
        // Generate some dummy entries.
        Mediator.Subscribe<ConnectedMessage>(this, _ =>
        {
            for (int i = 0; i < 5; i++)
            {
                var incomingRequest = new SundesmoRequest(new($"Dummy Sender {i}"), MainHub.OwnUserData, new(false, "Blah Blah", (ushort)i, (ushort)(i * 10)), DateTime.Now);
                var outgoingRequest = new SundesmoRequest(MainHub.OwnUserData, new($"Dummy Recipient {i}"), new(false, "Blah Blah", (ushort)(i * 5), (ushort)(i * 15)), DateTime.Now);
                _allRequests.Add(new RequestEntry(incomingRequest));
                _allRequests.Add(new RequestEntry(outgoingRequest));
            }
            RecreateLazy();
        });
#endif
        RecreateLazy();

        Mediator.Subscribe<DisconnectedMessage>(this, _ =>
        {
            // Clear all requests on disconnect.
            Logger.LogDebug("Clearing all requests on disconnect.", LoggerType.PairManagement);
            _allRequests.Clear();
            RecreateLazy();
        });
    }

    public int TotalRequests => _allRequests.Count;

    // Expose the Request Entries.
    public List<RequestEntry> Incoming => _incomingInternal.Value;
    public List<RequestEntry> Outgoing => _outgoingInternal.Value;

    public void AddNewRequest(SundesmoRequest newRequest)
    {
        var entry = new RequestEntry(newRequest);
        if (_allRequests.Contains(entry))
            return;
        // Add it to the requests.
        Logger.LogDebug($"Adding new request entry to manager.", LoggerType.PairManagement);
        _allRequests.Add(entry);
        RecreateLazy();
    }

    public void AddNewRequest(IEnumerable<SundesmoRequest> newRequests)
    {
        // Assume we can add all requests.
        var toAdd = newRequests.Select(r => new RequestEntry(r));
        // Trim out any that already exist.
        var validToAdd = toAdd.Except(_allRequests).ToList();
        if (validToAdd.Count is 0)
            return;
        // Add them to the requests.
        Logger.LogDebug($"Adding {validToAdd.Count} new request entries to manager.", LoggerType.PairManagement);
        _allRequests.UnionWith(validToAdd);
        RecreateLazy();
    }

    // From UI Callback.
    public void RemoveRequest(RequestEntry requestEntry)
    {
        if (!_allRequests.Remove(requestEntry))
            return;
        // Removed successfully.
        Logger.LogDebug($"Removed request entry from manager.", LoggerType.PairManagement);
        RecreateLazy();
    }

    // From server callback.
    public void RemoveRequest(SundesmoRequest requestEntry)
    {
        var entry = new RequestEntry(requestEntry);
        if (!_allRequests.Remove(entry))
            return;
        // Removed successfully.
        Logger.LogDebug($"Removed request entry from manager.", LoggerType.PairManagement);
        RecreateLazy();
    }

    private void RecreateLazy()
    {
        // Update internals.
        _incomingInternal = new(() => _allRequests.Where(r => !r.FromClient).OrderByDescending(r => r.TimeToRespond).ToList());
        _outgoingInternal = new(() => _allRequests.Where(r => !r.FromClient).OrderByDescending(r => r.TimeToRespond).ToList());
        Mediator.Publish(new RegenerateEntries(RefreshTarget.Requests));
    }
}
