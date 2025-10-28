using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
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
    private readonly InteractionsHandler _interactions;
    private readonly IdDisplayHandler _nameDisplay;
    private readonly GroupsManager _groupManager;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    public DrawEntityFactory(SundouleiaMediator mediator, MainHub hub, MainConfig config, 
        FavoritesConfig favorites, InteractionsHandler interactions, IdDisplayHandler nameDisplay,
        GroupsManager groups, SundesmoManager sundesmos, RequestsManager requests)
    {
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _favorites = favorites;
        _interactions = interactions;
        _nameDisplay = nameDisplay;
        _groupManager = groups;
        _sundesmos = sundesmos;
    }

    // Advance this for groups later.
    public DrawFolderDefault CreateDefaultFolder(string tag, List<Sundesmo> filtered, IImmutableList<Sundesmo> all)
        => new DrawFolderDefault(tag, filtered.Select(u => CreateDrawEntity(tag, u)).ToImmutableList(), all, _config, _groupManager);

    public DrawFolderGroup CreateGroupFolder(SundesmoGroup group, List<Sundesmo> filtered, IImmutableList<Sundesmo> all)
        => new DrawFolderGroup(group, filtered.Select(u => CreateDrawEntity(group.Label, u)).ToImmutableList(), all, _config, _groupManager);

    public DrawFolderRadar CreateRadarFolder(string label, List<RadarUser> all, Func<List<RadarUser>, IImmutableList<DrawEntityRadarUser>> lazyGen)
        => new DrawFolderRadar(label, all, lazyGen, _config, _groupManager);

    public DrawEntitySundesmo CreateDrawEntity(string id, Sundesmo sundesmo)
        => new DrawEntitySundesmo(id + sundesmo.UserData.UID, sundesmo, _mediator, _favorites, _interactions, _nameDisplay);

    public DrawEntityRadarUser CreateRadarEntity(RadarUser user)
        => new DrawEntityRadarUser(user, _mediator, _hub, _sundesmos, _requests);
}
