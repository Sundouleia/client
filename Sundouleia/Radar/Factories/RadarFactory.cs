using Microsoft.Extensions.Logging;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar.Factories;

// Factory to create Radar user instances.
public class RadarFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FolderConfig _folders;
    private readonly FavoritesConfig _favorites;
    private readonly NicksConfig _nicks;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;
    private readonly CharaWatcher _watcher;

    public RadarFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folders, FavoritesConfig favorites, NicksConfig nicks,
        SundesmoManager sundesmos, RequestsManager requests, CharaWatcher watcher)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _config = config;
        _folders = folders;
        _favorites = favorites;
        _nicks = nicks;
        _sundesmos = sundesmos;
        _requests = requests;
        _watcher = watcher;
    }

    public RadarPublicUser Create(RadarMember radarUserInfo)
        => new RadarPublicUser(radarUserInfo, _loggerFactory.CreateLogger<RadarPublicUser>(), _mediator, _sundesmos, _requests, _watcher);
}
