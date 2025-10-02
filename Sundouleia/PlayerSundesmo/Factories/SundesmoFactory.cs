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
    private readonly SundesmoHandlerFactory _innerFactory;
    private readonly ServerConfigManager _serverConfigs;

    public SundesmoFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        SundesmoHandlerFactory factory, ServerConfigManager configs)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _innerFactory = factory;
        _serverConfigs = configs;
    }

    public Sundesmo Create(UserPair sundesmoInfo)
        => new(sundesmoInfo, _loggerFactory.CreateLogger<Sundesmo>(), _mediator, _innerFactory, _serverConfigs);
}
