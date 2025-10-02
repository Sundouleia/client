using Sundouleia.Gui.Profiles;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;
    private readonly ProfileHelper _profileHelper;
    private readonly SundesmoManager _sundesmos;
    private readonly ProfileService _profiles;

    public UiFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator,
        ProfileHelper profileHelper, SundesmoManager sundesmos, ProfileService service)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
        _profileHelper = profileHelper;
        _sundesmos = sundesmos;
        _profiles = service;
    }

    public ProfileUI CreateStandaloneProfileUi(UserData userData)
    {
        return new ProfileUI(_loggerFactory.CreateLogger<ProfileUI>(), _mediator, _profileHelper,
            _sundesmos, _profiles, userData);
    }
}
