using Sundouleia.DrawSystem;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;
using static OtterGui.Services.ImGuiCacheService;

namespace Sundouleia.PlayerClient;

/// <summary> Directs which configs get loaded until which circumstances. </summary>
/// <remarks> This is a placeholder until we get other things sorted. For now it's for Main/Dev server split. </remarks>
public class ConfigDirector
{
    private readonly ILogger<ConfigDirector> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly ConfigFileProvider _fileProvider;
    private readonly ServerHubConfig _serverConfig;
    private readonly MainConfig _mainConfig;
    private readonly AccountConfig _accounts;
    private readonly FavoritesConfig _favorites;
    private readonly FolderConfig _folders;
    private readonly NicksConfig _nicks;
    private readonly GroupsDrawSystem _ddsGroups;

    public ConfigDirector(ILogger<ConfigDirector> logger, SundouleiaMediator mediator,
        ConfigFileProvider fileProvider, ServerHubConfig serverConfig, MainConfig config,
        AccountConfig accounts, FavoritesConfig favorites, FolderConfig folders,
        NicksConfig nicks, GroupsDrawSystem dsGroups)
    {
        _logger = logger;
        _mediator = mediator;
        _fileProvider = fileProvider;
        _serverConfig = serverConfig;
        _mainConfig = config;
        _accounts = accounts;
        _favorites = favorites;
        _folders = folders;
        _nicks = nicks;
        _ddsGroups = dsGroups;
    }

    public void UpdateForNewUri()
    {
        var cachedUri = _fileProvider.CurrentHubURI;
        if (string.Equals(cachedUri, ServerHubConfig.CurrentHubUri, StringComparison.Ordinal))
            return;

        _logger.LogInformation($"ServerHub URI changed from {cachedUri} to {ServerHubConfig.CurrentHubUri}, updating configs.");
        // Could run a save and flushAsync here, but should be fine without it.
        if (_fileProvider.TryUpdateForServerUri(_serverConfig, ServerHubConfig.CurrentHubUri))
        {
            _logger.LogInformation("ServerHub URI update detected, reloading all configs.");
            _mainConfig.Load();
            _accounts.Load();
            _favorites.Load();
            _nicks.Load();

            // Send them to the intro screen if the main config is no longer valid.
            if (!_mainConfig.HasValidSetup() || !_mainConfig.HasValidCacheFolderSetup() || !_accounts.IsConfigValid())
                _mediator.Publish(new SwitchToIntroUiMessage());
        }
        else
        {
            _logger.LogWarning("ServerHub URI update detected, but failed to update file provider. " +
                "\n This likely means the new URI is the same as the old one, or there was an error accessing the file system. ");
        }
    }

    public void UpdateFromHubResponse(ConnectionResponse response)
    {
        // Ensure we have the configs loaded for the correct ServerHub and ProfileUID.
        _logger.LogInformation($"Connected to ServerHub {ServerHubConfig.CurrentHubName} (URI: {ServerHubConfig.CurrentHubUri})");
        _logger.LogInformation($"Logged into ServerHub with UID: {response.User.UID}");
        // Update the plugin context.
        if (_fileProvider.TrySetProfileConfigs(_serverConfig, response.User.UID))
        {
            _logger.LogInformation($"Profile UID changed to {response.User.UID}, updating configs.");
            _folders.Load();
            _ddsGroups.LoadData();
        }

        // Send them to the intro screen if the main config is no longer valid.
        if (!_mainConfig.HasValidSetup() || !_mainConfig.HasValidCacheFolderSetup() || !_accounts.IsConfigValid())
            _mediator.Publish(new SwitchToIntroUiMessage());
    }

    // From the config helpers.
    public void SetHubIndex(int newIdx)
    {
        _serverConfig.SetHubIndex(newIdx);
        UpdateForNewUri();
    }

    public bool AddServerHub(ServerHubInfo hubInfo)
        => _serverConfig.AddServerHub(hubInfo);

    public bool RemoveServerHub(ServerHubInfo hubInfo)
    {
        var wasCurService = hubInfo.HubUri == ServerHubConfig.CurrentHubUri;
        if (!_serverConfig.RemoveHub(hubInfo))
            return false;
        
        // Removed, if removed was the current hubUri, update URI.
        if (wasCurService)
            UpdateForNewUri();

        return true;
    }
}
