using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Could maybe turn this into a service. Just handles who is blocked and such.
/// </summary>
public sealed class BlockedUserManager : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly ConfigFileProvider _fileNames;

    public BlockedUserManager(ILogger<BlockedUserManager> logger, SundouleiaMediator mediator,
        MainConfig config, ConfigFileProvider fileNames)
        : base(logger, mediator)
    {
        _config = config;
        _fileNames = fileNames;
    }
}
