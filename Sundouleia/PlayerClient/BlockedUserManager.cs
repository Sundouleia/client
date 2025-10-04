using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Could maybe turn this into a service. Just handles who is blocked and such.
/// </summary>
public sealed class BlockedUserManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly ConfigFileProvider _fileNames;

    public BlockedUserManager(ILogger<BlockedUserManager> logger, SundouleiaMediator mediator,
        MainConfig config, ServerConfigManager serverConfigs, ConfigFileProvider fileNames)
        : base(logger, mediator)
    {
        _config = config;
        _serverConfigs = serverConfigs;
        _fileNames = fileNames;
    }

    private void OnLogout()
    {
        Logger.LogInformation("Clearing Client Data for Profile on Logout!");
        _fileNames.ClearUidConfigs();
    }
}
