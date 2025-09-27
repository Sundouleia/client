using Sundouleia.Localization;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Tutorial;
using Microsoft.Extensions.Hosting;

namespace Sundouleia;

/// <summary>
///     Processes localization for Sundouleia.
/// </summary>
public class SundouleiaLoc : IHostedService
{
    private readonly ILogger<SundouleiaLoc> _logger;
    private readonly Dalamud.Localization _localization;
    private readonly MainConfig _mainConfig;
    private readonly TutorialService _tutorialService;

    public SundouleiaLoc(ILogger<SundouleiaLoc> logger, Dalamud.Localization localization, MainConfig config, TutorialService tutorial)
    {
        _logger = logger;
        _localization = localization;
        _mainConfig = config;
        _tutorialService = tutorial;
    }

    private void LoadLocalization(string languageCode)
    {
        _logger.LogInformation($"Loading Localization for {languageCode}");
        _localization.SetupWithLangCode(languageCode);
        CkLoc.ReInitialize();
        // re-initialize tutorial strings.
        _tutorialService.InitializeTutorialStrings();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Sundouleia Localization Service.");
        _localization.SetupWithLangCode(Svc.PluginInterface.UiLanguage);
        CkLoc.ReInitialize();
        // load tutorial strings.
        _tutorialService.InitializeTutorialStrings();

        // subscribe to any localization changes.
        Svc.PluginInterface.LanguageChanged += LoadLocalization;
        _logger.LogInformation("Sundouleia Localization Service started successfully.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Sundouleia Localization Service.");
        Svc.PluginInterface.LanguageChanged -= LoadLocalization;
        return Task.CompletedTask;
    }
}
