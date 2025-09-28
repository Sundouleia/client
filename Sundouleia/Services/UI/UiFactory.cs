using Sundouleia.Gui.Components;
using Sundouleia.Gui.Profiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

public class UiFactory
{
    // Generic Classes
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly SundesmoManager _sundesmoManager;
    private readonly CosmeticService _cosmetics;
    private readonly ProfileLight _profileLight;
    private readonly ProfileService _profiles;

    public UiFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        MainConfig config, ImageImportTool imageImport, SundesmoManager kinksters,
        CosmeticService cosmetics, ProfileLight lightPlate, ProfileService profiles, TutorialService guides)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _sundesmoManager = kinksters;
        _cosmetics = cosmetics;
        _profileLight = lightPlate;
        _profiles = profiles;
        _textures = textures;
    }

    public ProfileUI CreateStandaloneProfileUi(Sundesmo pair)
    {
        return new ProfileUI(_loggerFactory.CreateLogger<ProfileUI>(), _mediator,
            _sundesmoManager, _profiles, _cosmetics, _textures, pair);
    }

    public ProfileLightUI CreateStandaloneProfileLightUi(UserData pairUserData)
    {
        return new ProfileLightUI(_loggerFactory.CreateLogger<ProfileLightUI>(), _mediator,
            _profileLight, _profiles, _sundesmoManager, pairUserData);
    }
}
