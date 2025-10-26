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
    private readonly MainConfig _config;
    private readonly FavoritesConfig _favorites;
    private readonly IdDisplayHandler _nameDisplay;
    private readonly GroupsManager _groupManager;

    public DrawEntityFactory(SundouleiaMediator mediator, MainHub hub, MainConfig config,
        FavoritesConfig favorites, IdDisplayHandler nameDisplay, GroupsManager groupManager)
    {
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _favorites = favorites;
        _nameDisplay = nameDisplay;
        _groupManager = groupManager;
    }

    // Advance this for groups later.
    public DrawFolderDefault CreateDefaultFolder(string tag, List<Sundesmo> filtered, IImmutableList<Sundesmo> all)
        => new DrawFolderDefault(tag, filtered.Select(u => CreateDrawEntity(tag, u)).ToImmutableList(), all, _config, _groupManager);

    public DrawFolderGroup CreateGroupFolder(SundesmoGroup group, List<Sundesmo> filtered, IImmutableList<Sundesmo> all)
        => new DrawFolderGroup(group, filtered.Select(u => CreateDrawEntity(group.Label, u)).ToImmutableList(), all, _config, _groupManager);

    public DrawEntitySundesmo CreateDrawEntity(string id, Sundesmo sundesmo)
        => new DrawEntitySundesmo(id + sundesmo.UserData.UID, sundesmo, _mediator, _hub, _favorites, _nameDisplay);
}
