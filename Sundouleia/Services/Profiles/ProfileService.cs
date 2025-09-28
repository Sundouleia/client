using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Services;

/// <summary>
///     Helps manage the internal cache of loaded profiles, so that we can monitor 
///     and properly dispose of any rented image data from byte strings.
/// </summary>
public class ProfileService : MediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly ProfileFactory _factory;

    // Make thread safe yes yes. Also use a UserDataComparer for fast access.
    private static ConcurrentDictionary<UserData, Profile> _profiles= new(UserDataComparer.Instance);

    public ProfileService(ILogger<ProfileService> logger, SundouleiaMediator mediator, MainHub hub,
        ProfileFactory factory) : base(logger, mediator)
    {
        _hub = hub;
        _factory = factory;

        // At any point the client or Sundouleia can request a profile refresh via clearing
        // the existing data, so make sure we do that.
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null)
            {
                RemoveProfile(msg.UserData);
            }
            else
            {
                ClearAllProfiles();
            }
        });

        // Clear all profiles on disconnect
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearAllProfiles());
    }

    /// <summary> 
    ///     Fetch a Get the Sundouleia Profile data for a user.
    /// </summary>
    public Profile GetProfile(UserData userData)
    {
        if (_profiles.TryGetValue(userData, out var profile))
            return profile;

        // We must return a valid profile for the requested UserData.
        // If the profile is not cached, assign a default profile to the passed in UserData,
        // And run a task that fetches their profile data from the hub and applies it to that profile.
        Logger.LogTrace($"[ProfileCache Not Found]: {userData.AliasOrUID}, assigning loading Profile.", LoggerType.Profiles);
        _profiles[userData] = _factory.CreateProfileData();
        _ = Task.Run(() => GetProfileFromService(userData));
        return _profiles[userData]; 
    }

    /// <summary>
    ///     CAT EXPLOTANO
    /// </summary>
    private void RemoveProfile(UserData userData)
    {
        if (!_profiles.TryGetValue(userData, out var profile))
            return;

        Logger.LogDebug($"Removing ProfileCache for {userData.AliasOrUID}.", LoggerType.Profiles);
        // Free up the rented image data, then remove from the cache.
        profile.Dispose();
        _profiles.TryRemove(userData, out _);
    }

    /// <summary>
    ///     Mega-Cat Explotano
    /// </summary>
    public void ClearAllProfiles()
    {
        Logger.LogInformation("Clearing all Profiles", LoggerType.Profiles);
        foreach (var profile in _profiles.Values)
            profile.Dispose();
        _profiles.Clear();
    }

    /// <summary>
    ///     Given a <paramref name="data"/>, who has a placeholder loading profile in the cache,
    ///     obtain their profile data from the hub and apply it to the Profile object.
    /// </summary>
    private async Task GetProfileFromService(UserData data)
    {
        try
        {
            Logger.LogTrace("Fetching profile for "+data.UID, LoggerType.Profiles);
            // Fetch userData profile info from server
            var profile = await _hub.UserGetProfileData(new(data)).ConfigureAwait(false);

            // apply the retrieved profile data to the profile object.
            _profiles[data].Info = profile.Info;
            _profiles[data].ProfileAvatar = profile.Base64Image ?? string.Empty;
            Logger.LogDebug("Profile for "+data.UID+" loaded.", LoggerType.Profiles);
        }
        catch (Bagagwa ex)
        {
            // log the failure and set default data.
            Logger.LogWarning($"Failed to get {{{data.AliasOrUID}}}'s profile data! Reason: {ex}");
            _profiles[data].Info = new ProfileContent();
            _profiles[data].ProfileAvatar = string.Empty;
        }
    }
}
