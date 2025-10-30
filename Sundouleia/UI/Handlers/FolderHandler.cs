using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Handlers;

/// <summary>
///     Handles the updates for default and group folders.
///     Also provides the results.
/// </summary>
public class FolderHandler : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly DrawEntityFactory _factory;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    public FolderHandler(ILogger<FolderHandler> logger, SundouleiaMediator mediator, MainConfig config,
        DrawEntityFactory factory, GroupsManager groups, SundesmoManager sundesmos, RequestsManager requests)
        : base(logger, mediator)
    {
        _config = config;
        _factory = factory;
        _groups = groups;
        _sundesmos = sundesmos;
        _requests = requests;
    }
}