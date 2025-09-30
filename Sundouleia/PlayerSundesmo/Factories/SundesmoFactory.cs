using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;

namespace Sundouleia.Pairs.Factories;

// Maybe look into revising how this is structured.
// Ideally we would want the handled pair to always be created so
// we do not leave the cache in limbo for updates.
public class SundesmoFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly SundesmoHandlerFactory _cachedPlayerFactory;
    private readonly ServerConfigManager _serverConfigs;

    public SundesmoFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        SundesmoHandlerFactory cachedPlayerFactory, ServerConfigManager serverConfigs)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _cachedPlayerFactory = cachedPlayerFactory;
        _serverConfigs = serverConfigs;
    }

    /// <summary> Creates a new Pair object from the UserPair</summary>
    /// <param name="UserPair"> The data transfer object of a user pair</param>
    /// <returns> A new Pair object </returns>
    public Sundesmo Create(UserPair sundesmoInfo)
        => new(sundesmoInfo, _loggerFactory.CreateLogger<Sundesmo>(), _mediator, _cachedPlayerFactory, _serverConfigs);

    public SundesmoHandler Create(Sundesmo sundesmo)
    {
        return new SundesmoHandler(sundesmo, _loggerFactory.CreateLogger<SundesmoHandler>(), _mediator,
            _gameObjectHandlerFactory, _ipc, _frameworkUtils, _hostApplicationLifetime);
    }

}
