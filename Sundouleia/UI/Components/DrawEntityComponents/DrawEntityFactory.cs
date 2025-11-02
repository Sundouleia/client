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
    private readonly FolderConfig _folderConfig;
    private readonly FavoritesConfig _favorites;
    private readonly SharedFolderMemory _folderMemory;
    private readonly IdDisplayHandler _nameDisplay;
    private readonly GroupsManager _groupManager;
    private readonly RadarManager _radarManager;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    public DrawEntityFactory(ILoggerFactory logFactory,
        SundouleiaMediator mediator, 
        MainHub hub,
        MainConfig config,
        FolderConfig folderConfig, 
        FavoritesConfig favorites, 
        SharedFolderMemory memory,
        IdDisplayHandler nameDisplay,
        GroupsManager groups,
        RadarManager radar,
        SundesmoManager sundesmos,
        RequestsManager requests)
    {
        _logFactory = logFactory;
        _mediator = mediator;
        _hub = hub;
        _config = config;
        _folderConfig = folderConfig;
        _favorites = favorites;
        _folderMemory = memory;
        _nameDisplay = nameDisplay;
        _requests = requests;
        _radarManager = radar;
        _groupManager = groups;
        _sundesmos = sundesmos;
    }

    // Advance this for groups later.
    public DrawFolderDefault CreateDefaultFolder(string tag, FolderOptions options)
        => new DrawFolderDefault(tag, options, _logFactory.CreateLogger<DrawFolderDefault>(), _mediator,
            _folderConfig, _folderMemory, this, _groupManager, _sundesmos);

    public DrawFolderGroup CreateGroupFolder(SundesmoGroup group, FolderOptions options)
        => new DrawFolderGroup(group, options, _logFactory.CreateLogger<DrawFolderGroup>(), _mediator, 
            _folderConfig, _folderMemory, this, _groupManager, _sundesmos);

    public DynamicRadarFolder CreateRadarFolder(string label, FolderOptions options)
        => new DynamicRadarFolder(label, options, _logFactory.CreateLogger<DynamicRadarFolder>(), _mediator,
            _folderConfig, this, _groupManager, _folderMemory, _radarManager, _sundesmos);

    public DrawFolderRequestsIn CreateIncomingRequestsFolder()
        => new DrawFolderRequestsIn(_logFactory.CreateLogger<DrawFolderRequestsIn>(), _mediator, _folderConfig, this, _groupManager, _folderMemory, _requests);

    public DrawFolderRequestsOut CreateOutgoingRequestsFolder()
        => new DrawFolderRequestsOut(_logFactory.CreateLogger<DrawFolderRequestsOut>(), _mediator, _folderConfig, this, _groupManager, _folderMemory, _requests);

    // For DynamicPairFolder 
    public DrawEntitySundesmo CreateDrawEntity(DynamicPairFolder parent, Sundesmo sundesmo)
        => new DrawEntitySundesmo(parent, sundesmo, _mediator, _config, _favorites, _nameDisplay);

    public DrawEntityRadarUser CreateRadarEntity(RadarUser user)
        => new DrawEntityRadarUser(user, _hub, _sundesmos, _requests);

    public DrawEntityRequest CreateRequestEntity(DynamicRequestFolder parent, RequestEntry entry)
        => new DrawEntityRequest(parent, entry, _hub, _sundesmos, _requests);
}
