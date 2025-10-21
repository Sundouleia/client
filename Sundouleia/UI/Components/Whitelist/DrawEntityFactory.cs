using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using System.Collections.Immutable;

namespace Sundouleia.Gui;

public class DrawEntityFactory
{
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly GroupsConfig _config;
    private readonly IdDisplayHandler _nameDisplay;

    public DrawEntityFactory(SundouleiaMediator mediator, MainHub hub, GroupsConfig config, IdDisplayHandler nameDisplay)
    {
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _nameDisplay = nameDisplay;
    }

    // Advance this for groups later.
    public DrawFolderTag CreateDrawTagFolder(string tag, List<Sundesmo> filteredPairs, IImmutableList<Sundesmo> allPairs)
        => new DrawFolderTag(tag, filteredPairs.Select(u => CreateDrawPair(tag, u)).ToImmutableList(), allPairs, _config);

    public DrawUserPair CreateDrawPair(string id, Sundesmo sundesmo)
        => new DrawUserPair(id + sundesmo.UserData.UID, sundesmo, _mediator, _hub, _nameDisplay);
}
