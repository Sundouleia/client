using System.Reflection;
using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sundouleia.Gui.MainWindow;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;

namespace Sundouleia;

public class SundouleiaHost : MediatorSubscriberBase, IHostedService
{
    private readonly MainConfig _config;
    private readonly AccountConfig _accountConfig;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    private IServiceScope? _lifetimeScope;
    private IServiceScope? _loginScope;
    private Task? _launchTask;
    public SundouleiaHost(ILogger<SundouleiaHost> logger, SundouleiaMediator mediator, MainConfig mainConfig,
        AccountConfig accounts, IServiceScopeFactory scopeFactory) : base(logger, mediator)
    {
        _config = mainConfig;
        _accountConfig = accounts;
        _serviceScopeFactory = scopeFactory;
    }
    /// <summary> 
    ///     The task to run after all services have been properly constructed. <para />
    ///     This will kickstart the server and begin all operations and verifications.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        Logger.LogInformation($"Starting Sundouleia v{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}");

        // Init the plugin lifetime scope.
        _lifetimeScope = _serviceScopeFactory.CreateScope();

        // Force the construction once on startup.
        _lifetimeScope.ServiceProvider.GetRequiredService<UiService>();
        _lifetimeScope.ServiceProvider.GetRequiredService<CommandManager>();

        // Get Login Scoped service events configured
        Svc.ClientState.Login += DalamudUtilOnLogIn;
        Svc.ClientState.Logout += (_, _) => DalamudUtilOnLogOut();

        // Ensure proper startup occurs on the MainUI switch message.
        Mediator.Subscribe<IntoFinishedMessage>(this, _ => DalamudUtilOnLogIn());

        // start processing the mediator queue.
        Mediator.StartQueueProcessing();

        if (PlayerData.IsLoggedIn)
            DalamudUtilOnLogIn();

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The task to run when the plugin is stopped (called from the disposal)
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeAll();
        DalamudUtilOnLogOut();

        Svc.ClientState.Login -= DalamudUtilOnLogIn;
        Svc.ClientState.Logout -= (_, _) => DalamudUtilOnLogOut();

        Logger.LogDebug("Shutting down Sundouleia");
        // This could cause some issues.
        // If it does, just ensure the respective processors stop on the framework thread.
        _lifetimeScope?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Ensures all login-scoped services are properly launched on login. <para />
    /// </summary>
    private void DalamudUtilOnLogIn()
    {
        if (_launchTask is null || _launchTask.IsCompleted)
            _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    /// <summary>
    ///     Ensures all login-scoped services are properly disposed of on logout.
    /// </summary>
    private void DalamudUtilOnLogOut()
    {
        Logger.LogDebug("Client logout");
        _loginScope?.Dispose();
    }

    /// <summary>
    ///     Awaits for the player to become available, then initializes all intended login-scoped services.
    /// </summary>
    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        // wait for the player to be present
        while (!PlayerData.Available)
        {
            Logger.LogDebug("Waiting for player to be present");
            await Task.Delay(100).ConfigureAwait(false);
        }

        // then launch the managers for the plugin to function at a base level
        try
        {
            Logger.LogDebug("Launching Managers");
            // before we do lets recreate the runtime service scope
            _loginScope?.Dispose();
            _loginScope = _serviceScopeFactory.CreateScope();

            _loginScope.ServiceProvider.GetRequiredService<CacheMonitor>();

            TryDisplayChangelog();
            TrySwitchIntroUI();

            // get the required service for the online player manager (and notification service if we add it)
            _loginScope.ServiceProvider.GetRequiredService<ClientUpdateService>();
            _loginScope.ServiceProvider.GetRequiredService<ClientUpdateHandler>();
            _loginScope.ServiceProvider.GetRequiredService<ClientDistributor>();
            _loginScope.ServiceProvider.GetRequiredService<RadarDistributor>();
            _loginScope.ServiceProvider.GetRequiredService<LocationSvc>();
            _loginScope.ServiceProvider.GetRequiredService<LociMonitor>();
        }
        catch (Bagagwa ex)
        {
            Logger?.LogCritical(ex, "Error during launch of managers");
        }
    }

    private void TryDisplayChangelog()
    {
        // display changelog if we should.
        if (_config.Current.LastRunVersion != Assembly.GetExecutingAssembly().GetName().Version!)
        {
            // update the version and toggle the UI.
            Logger?.LogInformation("Version was different, displaying UI");
            _config.Current.LastRunVersion = Assembly.GetExecutingAssembly().GetName().Version!;
            _config.Save();
            // Mediator.Publish(new UiToggleMessage(typeof(ChangelogUI)));
        }
    }

    private void TrySwitchIntroUI()
    {
        // if the client does not have a valid setup or config, switch to the intro ui
        if (!_config.HasValidSetup() || !_accountConfig.IsConfigValid())
        {
            Logger?.LogDebug("Has Valid Setup: {setup} Has Valid Config: {config}", _config.HasValidSetup(), _accountConfig.IsConfigValid());
            // publish the switch to intro ui message to the mediator
            _config.Current.ButtonUsed = false;

            Mediator.Publish(new SwitchToIntroUiMessage());
        }
        else if (_config.Current.OpenUiOnStartup)
        {
            Mediator.Publish(new UiToggleMessage(typeof(MainUI)));
        }
    }
}
