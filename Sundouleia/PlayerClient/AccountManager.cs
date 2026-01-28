using CkCommons;
using CkCommons.Helpers;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class AccountManager
{
    private readonly ILogger<AccountManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly AccountConfig _config;
    private readonly ConfigFileProvider _fileProvider;

    // Some kind of cached mapping to the linked profiles for the selected.

    public AccountManager(ILogger<AccountManager> logger, SundouleiaMediator mediator, 
        AccountConfig config, ConfigFileProvider files)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _fileProvider = files;
    }

    public ConnectionKind ConnectionKind
    {
        get => _config.ConnectionKind;
        set
        {
            _config.ConnectionKind = value;
            _mediator.Publish(new ConnectionKindChanged(value));
            _config.Save();
        }
    }

    public HashSet<AccountProfile> Profiles => _config.Current.Profiles;
    public Dictionary<ulong, TrackedPlayer> TrackedPlayers => _config.Current.TrackedPlayers;
    
    // Avoid calling this wherever possible maybe?
    public void Save()
        => _config.Save();

    public void UpdateFileProviderForConnection(ConnectionResponse response)
        => _fileProvider.UpdateConfigs(response.User.UID);

    // If any profiles, and a player 
    public bool HasValidAccount()
        => HasValidProfile() && TrackedPlayers.Any(kvp => kvp.Value.LinkedProfile is not null);

    // If any profiles exist.
    public bool HasValidProfile()
        => Profiles.Count is not 0;

    public bool CharaIsTracked()
        => TrackedPlayers.ContainsKey(PlayerData.CID);
    public bool CharaHasValidProfile()
        => TrackedPlayers.TryGetValue(PlayerData.CID, out var a) && a.LinkedProfile is not null;

    public AccountProfile? GetMainProfile()
        => Profiles.FirstOrDefault(p => p.IsPrimary);

    public AccountProfile? GetCharaProfile()
        => TrackedPlayers.TryGetValue(PlayerData.CID, out var a) ? a.LinkedProfile : null;

    // Might need to run on framework thread? Not sure.
    // Does a Name & World update on connection.
    public TrackedPlayer? GetTrackedCharaOrDefault()
    {
        if (!TrackedPlayers.TryGetValue(PlayerData.CID, out var auth))
        {
            _logger.LogDebug("No tracked player found for current character.");
            return null;
        }

        var curName = PlayerData.Name;
        var curWorld = PlayerData.HomeWorldId;
        // If no name/world change, return.
        if (auth.PlayerName == curName && auth.WorldId == curWorld)
            return auth;

        // Otherwise update and save.
        auth.PlayerName = curName;
        auth.WorldId = curWorld;
        _config.Save();
        return auth;
    }

    /// <summary>
    ///     Generates a new TrackedPlayer entry for the currently logged in player.
    /// </summary>
    public void CreateTrackedPlayer()
    {
        var name = PlayerData.Name;
        var world = PlayerData.HomeWorldId;
        var cid = PlayerData.CID;

        if (TrackedPlayers.ContainsKey(cid))
        {
            _logger.LogError("A TrackedPlayer entry with this Player's ID already exists!");
            return;
        }

        // Generate it.
        TrackedPlayers[cid] = new TrackedPlayer
        {
            ContentId = cid,
            PlayerName = name,
            WorldId = world,
        };
        _config.Save();
    }

    /// <summary>
    ///     Adds a blank new profile to the service.
    /// </summary>
    public void AddNewProfile()
    {
        var newName = RegexEx.EnsureUniqueName("New Profile", Profiles, p => p.ProfileLabel);
        Profiles.Add(new AccountProfile()
        {
            ProfileLabel = newName
        });
        _config.Save();
    }

    public bool TryUpdateSecretKey(AccountProfile profile, string newKey)
    {
        if (profile.HadValidConnection)
            return false;

        // Update the key and save.
        profile.Key = newKey;
        _config.Save();
        return true;
    }

    public void LinkPlayerToProfile(ulong contentID, AccountProfile toLink)
    {
        if (!TrackedPlayers.TryGetValue(contentID, out var player))
        {
            _logger.LogError("Could not link the provided contentID, as no TrackedPlayer entry exists for it.");
            return;
        }

        if (Profiles.Contains(toLink))
        {
            _logger.LogError("Could not link the provided account profile, as it is not a stored profile!");
            return;
        }

        player.LinkedProfile = toLink;
        _config.Save();
    }

    /// <summary> 
    ///     Updates the authentication.
    /// </summary>
    public void UpdateAuthentication(string secretKey, ConnectionResponse response)
    {
        // If no profile exists with this key, this is a big red flag.
        if (Profiles.FirstOrDefault(p => p.Key == secretKey) is not { } profile)
        {
            _logger.LogError("Somehow connected with a SecretKey not stored, you should NEVER see this!");
            return;
        }

        // Mark the profile as having a valid connection, and set the UID.
        if (string.IsNullOrEmpty(profile.ProfileLabel))
            profile.ProfileLabel = RegexEx.EnsureUniqueName("New Profile", Profiles, p => p.ProfileLabel);
        
        profile.UserUID = response.User.UID;
        profile.HadValidConnection = true;

        // now we should iterate through the rest of our profiles, and check the UID's.
        // Any UID's listed in the profiles that are not in the associated profiles from the response are outdated.
        HashSet<string> accountProfileUids = [..response.ActiveAccountUidList, response.User.UID];

        foreach (var checkedProfile in Profiles.ToList())
        {
            if (string.IsNullOrWhiteSpace(checkedProfile.UserUID) || !checkedProfile.HadValidConnection)
                continue;

            // If the account UID list no longer has the profile UserUID, it was deleted via discord or a cleanup service.
            if (!accountProfileUids.Contains(checkedProfile.UserUID, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"Removing outdated profile {checkedProfile.ProfileLabel} with UID {checkedProfile.UserUID}");
                Profiles.Remove(checkedProfile);
                _config.Save();
            }
        }
    }
}
