using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;
public class ProfileFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly SundouleiaMediator _mediator;

    public ProfileFactory(ILoggerFactory loggerFactory, SundouleiaMediator mediator)
    {
        _loggerFactory = loggerFactory;
        _mediator = mediator;
    }

    // For placeholder profiles.
    public Profile CreateProfileData()
        => new Profile(_loggerFactory.CreateLogger<Profile>(), _mediator, new ProfileContent(), string.Empty);

    // For real profiles.
    public Profile CreateProfileData(ProfileContent profileInfo, string base64Avatar)
        => new Profile(_loggerFactory.CreateLogger<Profile>(), _mediator, profileInfo, base64Avatar);
}
