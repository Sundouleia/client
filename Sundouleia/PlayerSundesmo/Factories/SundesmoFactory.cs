using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs.Factories;

// Maybe look into revising how this is structured.
// Ideally we would want the handled pair to always be created so
// we do not leave the cache in limbo for updates.
public class SundesmoFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly SundesmoHandlerFactory _innerFactory;
    private readonly MainConfig _config;
    private readonly FavoritesConfig _favorites;
    private readonly ServerConfigManager _serverConfigs;
    private readonly CharaObjectWatcher _watcher;

    public SundesmoFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, MainConfig config, FavoritesConfig favorites,
        ServerConfigManager serverConfigs, CharaObjectWatcher watcher)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _innerFactory = factory;
        _config = config;
        _favorites = favorites;
        _serverConfigs = serverConfigs;
        _watcher = watcher;
    }

    public Sundesmo Create(UserPair sundesmoInfo)
        => new(sundesmoInfo, _loggerFactory.CreateLogger<Sundesmo>(), _mediator, _config, _favorites, _serverConfigs, _innerFactory, _watcher);
}
