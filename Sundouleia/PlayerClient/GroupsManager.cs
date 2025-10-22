using CkCommons;
using Dalamud.Interface;
using Sundouleia.Gui.Components;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class GroupsManager
{
    private readonly ILogger<GroupsManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly GroupsConfig _config;

    public GroupsManager(ILogger<GroupsManager> logger, SundouleiaMediator mediator, GroupsConfig config)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
    }

    public GroupsStorage Config => _config.Current;

    public void SaveConfig() => _config.Save();
    
}
