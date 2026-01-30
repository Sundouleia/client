using System.Reflection;
using CkCommons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private IServiceScope? _runtimeServiceScope;
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
        // set the version to the current assembly version
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        // log our version
        Logger.LogInformation("Launching {name} {major}.{minor}.{build}", "Sundouleia", version.Major, version.Minor, version.Build);

        // subscribe to the login and logout messages
        Svc.ClientState.Login += DalamudUtilOnLogIn;
        Svc.ClientState.Logout += (_, _) => DalamudUtilOnLogOut();

        // subscribe to the main UI message window for making the primary UI be the main UI interface.
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (msg) =>
        {
            if (_launchTask is null || _launchTask.IsCompleted)
                _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
        });

        // start processing the mediator queue.
        Mediator.StartQueueProcessing();

        // If already logged in, begin.
        if (PlayerData.IsLoggedIn)
            DalamudUtilOnLogIn();

        // return that the startAsync has been completed.
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

        Logger.LogDebug("Halting SundouleiaPlugin");
        return Task.CompletedTask;
    }


    private void DalamudUtilOnLogIn()
    {
        Svc.Logger.Debug("Client login");
        if (_launchTask == null || _launchTask.IsCompleted) _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    private void DalamudUtilOnLogOut()
    {
        Svc.Logger.Debug("Client logout");
        _runtimeServiceScope?.Dispose();
    }

    /// <summary> The Task executed by the launchTask var from the main plugin.cs 
    /// <para>
    /// This task will await for the player to be present (they are logged in and visible),
    /// then will dispose of the runtime service scope and create a new one to fetch
    /// the required services for our plugin to function as a base level.
    /// </para>
    /// </summary>
    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        // wait for the player to be present
        while (!PlayerData.Available)
        {
            Svc.Logger.Debug("Waiting for player to be present");
            await Task.Delay(100).ConfigureAwait(false);
        }

        // then launch the managers for the plugin to function at a base level
        try
        {
            Svc.Logger.Debug("Launching Managers");
            // before we do lets recreate the runtime service scope
            _runtimeServiceScope?.Dispose();
            _runtimeServiceScope = _serviceScopeFactory.CreateScope();

            // startup services that have no other services that call on them, yet are essential.
            _runtimeServiceScope.ServiceProvider.GetRequiredService<UiService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CommandManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CacheMonitor>();

            // display changelog if we should.
            if (_config.Current.LastRunVersion != Assembly.GetExecutingAssembly().GetName().Version!)
            {
                // update the version and toggle the UI.
                Logger?.LogInformation("Version was different, displaying UI");
                _config.Current.LastRunVersion = Assembly.GetExecutingAssembly().GetName().Version!;
                _config.Save();
                // Mediator.Publish(new UiToggleMessage(typeof(ChangelogUI)));
            }

            // if the client does not have a valid setup or config, switch to the intro ui
            if (!_config.HasValidSetup() || !_accountConfig.IsConfigValid())
            {
                Logger?.LogDebug("Has Valid Setup: {setup} Has Valid Config: {config}", _config.HasValidSetup(), _accountConfig.IsConfigValid());
                // publish the switch to intro ui message to the mediator
                _config.Current.ButtonUsed = false;

                Mediator.Publish(new SwitchToIntroUiMessage());
            }

            // Services that require an initial constructor call during bootup.
            _runtimeServiceScope.ServiceProvider.GetRequiredService<UiFontService>();
            // get the required service for the online player manager (and notification service if we add it)
            _runtimeServiceScope.ServiceProvider.GetRequiredService<ClientUpdateService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<ClientUpdateHandler>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<ClientDistributor>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<RadarDistributor>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<LocationSvc>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<ClientMoodles>();
            // stuff that should probably be a hosted service but isn't yet.
            _runtimeServiceScope.ServiceProvider.GetRequiredService<DtrService>();
        }
        catch (Bagagwa ex)
        {
            Logger?.LogCritical(ex, "Error during launch of managers");
        }
    }
}
