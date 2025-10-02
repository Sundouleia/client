using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary> 
///     Helps ensure the correct data is loaded for the current Account Profile. <para />
///     Is also necessary to keep the ConfigFileProvider up to date (kind of?) <para />
///     This is called after connection is established and before MainHubConnectedMessage is sent.
/// </summary>
public sealed class AccountService : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly ServerConfigManager _serverConfigs;
    private readonly ConfigFileProvider _fileNames;

    public AccountService(ILogger<AccountService> logger, SundouleiaMediator mediator,
        MainConfig config, ServerConfigManager serverConfigs, ConfigFileProvider fileNames)
        : base(logger, mediator)
    {
        _config = config;
        _serverConfigs = serverConfigs;
        _fileNames = fileNames;

        Svc.ClientState.Logout += (_,_) => OnLogout();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.Logout -= (_, _) => OnLogout();
    }

    private void OnLogout()
    {
        Logger.LogInformation("Clearing Client Data for Profile on Logout!");
        _fileNames.ClearUidConfigs();
    }

    /// <summary>
    ///     By awaiting this, we know it will be distribute data once complete.
    /// </summary>
    public void SetDataForAccountProfile()
    {
        // if the ConnectionResponse for whatever reason was null, dont process any of this.
        // (this theoretically should never happen, but just in case)
        if (MainHub.ConnectionResponse is not { } connectionInfo)
            return;

        // 1. Load in the updated config storages for the profile.
        Logger.LogInformation($"[SYNC PROGRESS]: Updating FileProvider for Profile ({MainHub.UID})");
        _fileNames.UpdateConfigs(MainHub.UID); // Update for the new key instead.

        // 2. Load in Profile-specific Configs.
        Logger.LogInformation($"[SYNC PROGRESS]: Loading Configs for Profile!");

        // 3. Sync Visual Cache with active state.
        Logger.LogInformation("[SYNC PROGRESS]: Syncing Visual Cache With Display");

        // 6. Update the achievement manager with the latest UID and the latest data.
        Logger.LogInformation($"[SYNC PROGRESS]: Syncing Achievement Data ({MainHub.UID})");
        // Do if adding.

        Logger.LogInformation("[SYNC PROGRESS]: Done!");
    }
}
