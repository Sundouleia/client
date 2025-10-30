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
    private readonly ILoggerFactory _logFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly FavoritesConfig _favorites;
    private readonly SharedFolderMemory _folderMemory;
    private readonly InteractionsHandler _interactions;
    private readonly IdDisplayHandler _nameDisplay;
    private readonly GroupsManager _groupManager;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    public DrawEntityFactory(ILoggerFactory logFactory,
        SundouleiaMediator mediator, 
        MainHub hub, 
        MainConfig config, 
        FavoritesConfig favorites, 
        SharedFolderMemory memory,
        InteractionsHandler interactions, 
        IdDisplayHandler nameDisplay,
        GroupsManager groups,
        SundesmoManager sundesmos,
        RequestsManager requests)
    {
        _logFactory = logFactory;
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _favorites = favorites;
        _folderMemory = memory;
        _interactions = interactions;
        _nameDisplay = nameDisplay;
        _groupManager = groups;
        _sundesmos = sundesmos;
    }

    // Advance this for groups later.
    public DrawFolderDefault CreateDefaultFolder(string tag, FolderOptions options)
        => new DrawFolderDefault(tag, options, _logFactory.CreateLogger<DrawFolderDefault>(), _mediator,
            _config, _folderMemory, this, _groupManager, _sundesmos);

    public DrawFolderGroup CreateGroupFolder(SundesmoGroup group, FolderOptions options)
        => new DrawFolderGroup(group, options, _logFactory.CreateLogger<DrawFolderGroup>(), _mediator, 
            _config, _folderMemory, this, _groupManager, _sundesmos);

    public DrawEntitySundesmo CreateDrawEntity(DrawFolder parent, Sundesmo sundesmo)
        => new DrawEntitySundesmo(parent, sundesmo, _mediator, _config, _favorites, _interactions, _nameDisplay);


    // In Rework.
    public DrawFolderRadar CreateRadarFolder(string label, List<RadarUser> all, Func<List<RadarUser>, IImmutableList<DrawEntityRadarUser>> lazyGen)
        => new DrawFolderRadar(label, all, lazyGen, _config, _groupManager);

    public DrawEntityRadarUser CreateRadarEntity(RadarUser user)
        => new DrawEntityRadarUser(user, _mediator, _hub, _sundesmos, _requests);
}
