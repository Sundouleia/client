using CkCommons;
using Dalamud.Interface.Textures.TextureWraps;
using Microsoft.IdentityModel.Tokens;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;

namespace Sundouleia.Services;

/// <summary>
///     Reflects the profile information for any sundouleia user. <para />
///     Any updates made are reflected here and applied via the ProfileService.
/// </summary>
public class Profile : DisposableMediatorSubscriberBase
{
    // Profile Data for User.
    private string _profileAvatar;
    private Lazy<byte[]> _imageData;
    private IDalamudTextureWrap? _storedProfileImage;

    public Profile(ILogger<Profile> logger, SundouleiaMediator mediator,
        ProfileContent plateContent, string base64Avatar) : base(logger, mediator)
    {
        // Set the Profile Data
        Info = plateContent;
        ProfileAvatar = base64Avatar;
        // set the image data if the profilePicture is not empty.
        _imageData = new Lazy<byte[]>(() => ProfileAvatar.Length > 0 ? Convert.FromBase64String(ProfileAvatar) : Array.Empty<byte>());

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, MainHub.UID, StringComparison.Ordinal))
            {
                _storedProfileImage?.Dispose();
                _storedProfileImage = null;
            }
        });
    }

    public ProfileContent Info;

    public bool TempDisabled => Info.Disabled || Info.Flagged;

    public string ProfileAvatar
    {
        get => _profileAvatar;
        set
        {
            if (_profileAvatar != value)
            {
                _profileAvatar = value;
                Logger.LogDebug("Profile avatar updated.", LoggerType.Profiles);
                if(!string.IsNullOrEmpty(_profileAvatar))
                {
                    Logger.LogTrace("Refreshing profile image data!", LoggerType.Profiles);
                    _imageData = new Lazy<byte[]>(() => ConvertBase64ToByteArray(ProfileAvatar));
                    Logger.LogTrace("Refreshed profile image data!", LoggerType.Profiles);
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Logger.LogInformation("Disposing profile image data!");
            _storedProfileImage?.Dispose();
            _storedProfileImage = null;
        }
        base.Dispose(disposing);
    }

    public IDalamudTextureWrap GetAvatarOrDefault()
    {
        // If the user does not have a profile set, return the default logo.
        if(string.IsNullOrEmpty(ProfileAvatar) || _imageData.Value.IsNullOrEmpty())
            return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];

        // Otherwise, fetch the profile image for it.
        if(_storedProfileImage is not null)
            return _storedProfileImage;

        Logger.LogTrace("Loading profile image data to wrap.");
        Generic.Safe(() => _storedProfileImage = Svc.Texture.CreateFromImageAsync(_imageData.Value).Result);
        return CosmeticService.CoreTextures.Cache[CoreTexture.Icon256Bg];
    }

    public PlateBG GetBackground(PlateElement component)
        => component switch
        {
            PlateElement.Plate => Info.MainBG,
            PlateElement.Avatar => Info.AvatarBG,
            PlateElement.Description => Info.DescriptionBG,
            _ => PlateBG.Default
        };

    public PlateBorder GetBorder(PlateElement component)
        => component switch
        {
            PlateElement.Plate => Info.MainBorder,
            PlateElement.Avatar => Info.AvatarBorder,
            PlateElement.Description => Info.DescriptionBorder,
            _ => PlateBorder.Default
        };

    public PlateOverlay GetOverlay(PlateElement component)
        => component switch
        {
            PlateElement.Avatar => Info.AvatarOverlay,
            PlateElement.Description => Info.DescriptionOverlay,
            _ => PlateOverlay.Default
        };

    public void SetBG(PlateElement component, PlateBG bg)
    {
        switch (component)
        {
            case PlateElement.Plate:        Info.MainBG = bg;        break;
            case PlateElement.Avatar:       Info.AvatarBG = bg;      break;
            case PlateElement.Description:  Info.DescriptionBG = bg; break;
        }
    }

    public void SetBorder(PlateElement component, PlateBorder border)
    {
        switch (component)
        {
            case PlateElement.Plate:        Info.MainBorder = border;        break;
            case PlateElement.Avatar:       Info.AvatarBorder = border;      break;
            case PlateElement.Description:  Info.DescriptionBorder = border; break;
        }
    }

    public void SetOverlay(PlateElement component, PlateOverlay overlay)
    {
        switch (component)
        {
            case PlateElement.Avatar:       Info.AvatarOverlay = overlay;      break;
            case PlateElement.Description:  Info.DescriptionOverlay = overlay; break;
        }
    }

    private byte[] ConvertBase64ToByteArray(string base64String)
    {
        if (string.IsNullOrEmpty(base64String))
            return Array.Empty<byte>();

        try
        {
            return Convert.FromBase64String(base64String);
        }
        catch (FormatException ex)
        {
            Logger.LogError(ex, "Invalid Base64 string for profile picture.");
            return Array.Empty<byte>();
        }
    }
}
