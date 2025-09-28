using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using System.Collections.Immutable;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using SundouleiaAPI.Network;

namespace Sundouleia.Gui;

public class DrawEntityFactory
{
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly ServerConfigManager _configs;
    private readonly IdDisplayHandler _nameDisplay;

    public DrawEntityFactory(SundouleiaMediator mediator, MainHub hub, ServerConfigManager configs, IdDisplayHandler nameDisplay)
    {
        _mediator = mediator;
        _hub = hub;
        _configs = configs;
        _nameDisplay = nameDisplay;
    }

    // Advance this for groups later.
    public DrawFolderTag CreateDrawTagFolder(string tag, List<Sundesmo> filteredPairs, IImmutableList<Sundesmo> allPairs)
        => new DrawFolderTag(tag, filteredPairs.Select(u => CreateDrawPair(tag, u)).ToImmutableList(), allPairs, _configs);

    public DrawUserPair CreateDrawPair(string id, Sundesmo kinkster)
        => new DrawUserPair(id + kinkster.UserData.UID, kinkster, _mediator, _hub, _nameDisplay);

    public DrawUserRequest CreateDrawPairRequest(string id, PendingRequest request)
        => new DrawUserRequest(id, request, _hub);
}
