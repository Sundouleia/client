using System.Net.Http.Headers;
using System.Reflection;
using CkCommons;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sundouleia.Gui;
using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Gui.Profiles;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModFiles.Cache;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Events;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using Sundouleia.WebAPI.Files;
using TerraFX.Interop.Windows;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureMacroModule;

namespace Sundouleia;

public sealed class Sundouleia : IDalamudPlugin
{
    private readonly IHost _host;  // the host builder for the plugin instance. (What makes everything work)
    private readonly HttpClientHandler _httpHandler = new() // the http client handler for the plugin instance.
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    };
    public Sundouleia(IDalamudPluginInterface pi)
    {
        pi.Create<Svc>();
        // init GameData storages for the client language.
        GameDataSvc.Init(pi);
        // init the CkCommons.
        CkCommonsHost.Init(pi, this, CkLogFilter.None);
        // create the host builder for the plugin
        _host = ConstructHostBuilder(pi);
        // start up the host
        _ = _host.StartAsync();
    }

    // Method that creates the host builder for the Sundouleia plugin
    public IHost ConstructHostBuilder(IDalamudPluginInterface pi)
    {
        // create a new host builder for the plugin
        return new HostBuilder()
            // Get the content root for our plugin
            .UseContentRoot(pi.ConfigDirectory.FullName)
            // Configure the logging for the plugin
            .ConfigureLogging((hostContext, loggingBuilder) => GetPluginLogConfiguration(loggingBuilder))
            // Get the plugin service collection for our plugin
            .ConfigureServices((hostContext, serviceCollection) =>
            {
                var services = GetPluginServices(serviceCollection);
                //services.ValidateDependencyInjector();
            })
            .Build();
    }

    /// <summary> Gets the log configuration for the plugin. </summary>
    private void GetPluginLogConfiguration(ILoggingBuilder lb)
    {
        // clear our providers, add dalamud logging (the override that integrates ILogger into IPluginLog), and set the minimum level to trace
        lb.ClearProviders();
        lb.AddDalamudLogging();
        lb.SetMinimumLevel(LogLevel.Trace);
    }

    /// <summary> Gets the plugin services for the Sundouleia plugin. </summary>
    public IServiceCollection GetPluginServices(IServiceCollection collection)
    {
        return collection
            // add the general services to the collection
            .AddSingleton(new WindowSystem("Sundouleia"))
            .AddSingleton<FileDialogManager>()
            .AddSingleton<UiFileDialogService>()
            .AddSingleton(new Dalamud.Localization("Sundouleia.Localization.", "", useEmbedded: true))
            // add the generic services for Sundouleia
            .AddSundouleiaGeneric(_httpHandler)
            // add the services related to the IPC calls for Sundouleia
            .AddSundouleiaIPC()
            // add the services related to the configs for Sundouleia
            .AddSundouleiaConfigs()
            // add the scoped services for Sundouleia
            .AddSundouleiaScoped()
            // add the hosted services for Sundouleia (these should all contain startAsync and stopAsync methods)
            .AddSundouleiaHosted();
    }

    public void Dispose()
    {
        // Stop the host.
        _host.StopAsync().GetAwaiter().GetResult();
        // Dispose of CkCommons.
        CkCommonsHost.Dispose();
        // Dispose cleanup of GameDataSvc.
        GameDataSvc.Dispose();
        // Dispose the HttpClientHandler.
        _httpHandler.Dispose();
        // Dispose the Host.
        _host.Dispose();

    }
}

public static class SundouleiaServiceExtensions
{
    #region GenericServices
    public static IServiceCollection AddSundouleiaGeneric(this IServiceCollection services, HttpClientHandler httpHandler)
    => services
        // Necessary Services
        .AddSingleton<ILoggerProvider, Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>()
        .AddSingleton<SundouleiaHost>()
        .AddSingleton<EventAggregator>()
        .AddSingleton<SundouleiaLoc>()

        // Mod Files (revise)
        .AddSingleton<PenumbraWatcher>()
        .AddSingleton<SundouleiaWatcher>()
        .AddSingleton<FileCacheManager>()
        .AddSingleton<FileDownloader>()
        .AddSingleton<FileUploader>()
        .AddSingleton<FileTransferService>()
        .AddSingleton<FileCompactor>()

        // Player Client
        .AddSingleton<AccountManager>()
        .AddSingleton<BlockedUserManager>()
        .AddSingleton<RequestsManager>()

        // Player User
        .AddSingleton<SundesmoFactory>()
        .AddSingleton<SundesmoHandlerFactory>()
        .AddSingleton<SundesmoManager>()

        // Profiles
        .AddSingleton<ProfileFactory>()
        .AddSingleton<ProfileService>()

        // Distribution
        .AddSingleton<CharaObjectWatcher>()
        .AddSingleton<ClientUpdateService>()
        .AddSingleton<DistributionService>()
        .AddSingleton<ModdedStateManager>()
        .AddSingleton<PlzNoCrashFrens>()

        // Radar
        .AddSingleton<RadarManager>()
        .AddSingleton<RadarService>()

        // Misc. Services
        .AddSingleton<CosmeticService>()
        .AddSingleton<DtrBarService>()
        .AddSingleton<NotificationService>()
        .AddSingleton<OnTickService>()
        .AddSingleton<SundouleiaMediator>()
        .AddSingleton<TutorialService>()
        .AddSingleton<UiFontService>()

        // UI (Probably mostly in Scoped)
        .AddSingleton<IdDisplayHandler>()
        .AddSingleton<RadarChatLog>()
        .AddSingleton<PopoutRadarChatlog>()
        .AddSingleton<MainMenuTabs>()

        // WebAPI (Server stuff)
        .AddSingleton<MainHub>()
        .AddSingleton<HubFactory>()
        .AddSingleton<TokenProvider>()
        .AddSingleton((s) =>
        {
            var httpClient = new HttpClient(httpHandler);
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Sundouleia", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
            return httpClient;
        });
    #endregion GenericServices

    public static IServiceCollection AddSundouleiaIPC(this IServiceCollection services)
    => services
        .AddSingleton<IpcCallerCustomize>()
        .AddSingleton<IpcCallerGlamourer>()
        .AddSingleton<IpcCallerHeels>()
        .AddSingleton<IpcCallerHonorific>()
        .AddSingleton<IpcCallerMoodles>()
        .AddSingleton<IpcCallerPenumbra>()
        .AddSingleton<IpcCallerPetNames>()
        .AddSingleton<IpcManager>()
        .AddSingleton<IpcProvider>();

    public static IServiceCollection AddSundouleiaConfigs(this IServiceCollection services)
    => services
        .AddSingleton<AccountConfig>()
        .AddSingleton<ConfigFileProvider>()
        .AddSingleton<GroupsConfig>()
        .AddSingleton<MainConfig>()
        .AddSingleton<NickConfig>()
        .AddSingleton<NoCrashFriendsConfig>()
        .AddSingleton<TransientCacheConfig>()
        .AddSingleton<ServerConfigManager>()
        .AddSingleton<HybridSaveService>();

    #region ScopedServices
    public static IServiceCollection AddSundouleiaScoped(this IServiceCollection services)
    => services
        // Scopes monitors (important to make this thing scoped so it is only processed during access!)
        .AddScoped<CacheMonitor>()

        // Scoped Components
        .AddScoped<ProfileHelper>()

        // Scoped Factories
        .AddScoped<DrawEntityFactory>()
        .AddScoped<UiFactory>()

        // Scoped Handlers
        .AddScoped<WindowMediatorSubscriberBase, PopupHandler>()
        .AddScoped<IPopupHandler, VerificationPopupHandler>()
        .AddScoped<IPopupHandler, ReportPopupHandler>()

        // Scoped MainUI (Home)
        .AddScoped<WindowMediatorSubscriberBase, IntroUi>()
        .AddScoped<WindowMediatorSubscriberBase, MainUI>()
        .AddScoped<HomepageTab>()
        .AddScoped<WhitelistTab>()
        .AddScoped<RadarTab>()
        .AddScoped<RequestsTab>()
        .AddScoped<RadarChatTab>()
        .AddScoped<AccountTab>()


        // Scoped UI (Achievements)
        .AddScoped<AchievementTabs>()

        // Scoped Profiles
        .AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>()
        .AddScoped<WindowMediatorSubscriberBase, ProfileAvatarEditor>()
        .AddScoped<WindowMediatorSubscriberBase, ProfileEditorUI>()

        // Scoped Settings
        .AddScoped<WindowMediatorSubscriberBase, SettingsUi>()
        .AddScoped<AccountManagerTab>()
        .AddScoped<DebugTab>()
        .AddScoped<ModStorageTab>()

        // Scoped Misc
        .AddScoped<WindowMediatorSubscriberBase, TransferBarUI>()
        .AddScoped<WindowMediatorSubscriberBase, DataEventsUI>()
        .AddScoped<WindowMediatorSubscriberBase, ChangelogUI>()
        .AddScoped<WindowMediatorSubscriberBase, RadarChatPopoutUI>()
        .AddScoped<WindowMediatorSubscriberBase, DebugStorageUI>()
        .AddScoped<WindowMediatorSubscriberBase, DebugPersonalDataUI>()
        .AddScoped<WindowMediatorSubscriberBase, DebugActiveStateUI>()

        // Scoped Services
        .AddScoped<CommandManager>()
        .AddScoped<UiService>();
    #endregion ScopedServices

    /// <summary>
    ///     Services that must run logic on initialization to help with monitoring.
    ///     If it does not, it can also be an important monitor background service.
    /// </summary>
    /// <remarks> Services that simply monitor actions should be invoked in 'WaitForPlayerAndLaunchCharacterManager' </remarks>
    public static IServiceCollection AddSundouleiaHosted(this IServiceCollection services)
    => services
        .AddHostedService(p => p.GetRequiredService<HybridSaveService>())   // Begins the SaveCycle task loop
        .AddHostedService(p => p.GetRequiredService<CosmeticService>())     // Initializes our required textures so methods can work.
        .AddHostedService(p => p.GetRequiredService<SundouleiaMediator>())  // Runs the task for monitoring mediator events.
        .AddHostedService(p => p.GetRequiredService<NotificationService>()) // Important Background Monitor.
        .AddHostedService(p => p.GetRequiredService<OnTickService>())       // Starts & monitors the framework update cycle.

        // Cached Data That MUST be initialized before anything else for validity.
        .AddHostedService(p => p.GetRequiredService<FileCacheManager>())      // Handle the csv cache for all file locations.
        .AddHostedService(p => p.GetRequiredService<CosmeticService>())     // Provides all Textures necessary for the plugin.
        .AddHostedService(p => p.GetRequiredService<UiFontService>())       // Provides all fonts necessary for the plugin.

        .AddHostedService(p => p.GetRequiredService<SundouleiaLoc>())       // Initializes Localization with the current language.
        .AddHostedService(p => p.GetRequiredService<EventAggregator>())     // Forcibly calls the constructor, subscribing to the monitors.
        .AddHostedService(p => p.GetRequiredService<IpcProvider>())         // Required for IPC calls to work properly.

        .AddHostedService(p => p.GetRequiredService<MainHub>())             // Required for beyond obvious reasons.
        .AddHostedService(p => p.GetRequiredService<SundouleiaHost>());     // Make this always the final hosted service, initializing the startup.
}

public static class ValidateDependencyInjectorEx
{
    public static void ValidateDependencyInjector(this IServiceCollection services)
    {
        try
        {
            using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,  // Enforce validation on build
                ValidateScopes = false    // Ensure proper scope resolution
            });

            foreach (var service in services)
            {
                var serviceType = service.ServiceType;

                // Skip interfaces and abstract classes
/*                if (serviceType.IsInterface || serviceType.IsAbstract)
                    continue;*/

                var constructor = serviceType.GetConstructors().MaxBy(c => c.GetParameters().Length);
                if (constructor == null)
                    continue;

                var parameters = constructor.GetParameters()
                    .Select(p => serviceProvider.GetService(p.ParameterType))
                    .ToArray();

                // Skip services with unresolvable parameters instead of throwing an error
                if (parameters.Any(p => p == null))
                {
                    Svc.Logger.Warning($"[WARNING] Skipping {serviceType.Name} due to unresolvable parameters.");
                    continue;
                }

                constructor.Invoke(parameters);
            }


        }
        catch (AggregateException ex)
        {
            // join all the inner exception strings together by \n newline.
            var fullException = string.Join("\n\n", ex.InnerExceptions.Select(e => e.Message.ToString()));
            throw new InvalidOperationException(fullException);
        }
        catch (Bagagwa ex)
        {
            throw new InvalidOperationException("ValidateDependencyInjector error detected.", ex);
            // Log the exception to catch any circular dependencies
        }
    }
}
