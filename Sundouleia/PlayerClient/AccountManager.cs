using CkCommons;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

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

    public AccountManager(ILogger<AccountManager> logger, SundouleiaMediator mediator, 
        AccountConfig config, ConfigFileProvider files)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
        _fileProvider = files;
    }

    public AccountStorage Config => _config.Current;

    public void SaveConfig() => _config.Save();

    public bool HasValidProfile() => Config.Profiles.Count > 0;

    public void UpdateFileProviderForConnection(ConnectionResponse response)
        => _fileProvider.UpdateConfigs(response.User.UID);

    public bool TryGetAuthForPlayer([NotNullWhen(true)] out CharaAuthentication auth)
    {
        // fetch the cid of our current player.
        var cid = Svc.Framework.RunOnFrameworkThread(() => PlayerData.CID).Result;
        // if we cannot find any authentications with this data, it means that none exist.
        if (Config.LoginAuths.Find(la => la.ContentId == cid) is not { } match)
        {
            _logger.LogDebug("No authentication found for the current character.");
            auth = null!;
            return false;
        }
        // a match was found, so mark it, but update name and world before returning.
        auth = match;
        UpdateAuthForNameAndWorldChange(cid);
        return true;
    }

    public bool TryGetMainProfile([NotNullWhen(true)] out AccountProfile profile)
    {
        if (Config.Profiles.Values.FirstOrDefault(p => p.IsPrimary) is { } primary)
        {
            profile = primary;
            return true;
        }
        // Otherwise, ret null and false.
        profile = null!;
        return false;
    }

    /// <summary>
    ///     Retrieves the SecretKey for the currently logged in character.
    /// </summary>
    public AccountProfile? GetProfileForCharacter()
    {
        if (!TryGetAuthForPlayer(out var auth))
            return null;
        // There was an account, so get the key index.
        var profileIdx = auth.ProfileIdx;

        // Now obtain the secret key using the profile index.
        if (!Config.Profiles.TryGetValue(profileIdx, out var profile))
            return null;
        // Return the key as it exists.
        return profile;
    }

    public void UpdateAuthForNameAndWorldChange(ulong cid)
    {
        // locate the auth with the matching local content ID, and update the name and world if they do not match.
        if (Config.LoginAuths.Find(la => la.ContentId == cid) is not { } auth)
            return;
        // Id was valid, compare against current.
        var currentName = PlayerData.Name;
        var currentWorld = PlayerData.HomeWorldId;
        // update the name if it has changed.
        if (auth.PlayerName == currentName && auth.WorldId == currentWorld)
            return;

        // Otherwise update and save.
        if (auth.PlayerName != currentName)
            auth.PlayerName = currentName;
        
        if (auth.WorldId != currentWorld)
            auth.WorldId = currentWorld;
        
        _config.Save();
    }

    /// <summary> Checks if the configuration is valid </summary>
    /// <returns> True if the current server storage object is not null </returns>
    public bool HasValidAccount() => Config.Profiles.Count is not 0;
    public bool CharaHasLoginAuth() => Config.LoginAuths.Any(a => a.ContentId == PlayerData.CID);
    public bool CharaHasValidLoginAuth()
    {
        if (Config.LoginAuths.Find(a => a.ContentId == PlayerData.CID && a.ProfileIdx != -1) is not { } auth)
            return false;
        if (Config.Profiles.TryGetValue(auth.ProfileIdx, out var profile))
            return !string.IsNullOrEmpty(profile.Key);
        return false;
    }

    public void GenerateAuthForCurrentCharacter()
    {
        var name = PlayerData.Name;
        var world = PlayerData.HomeWorldId;
        var cid = PlayerData.CID;

        // If we already have an auth for this character, do nothing.
        if (Config.LoginAuths.Any(a => a.ContentId == cid))
            return;

        _logger.LogDebug("Generating new auth for current character");
        var autoSelectedKey = Config.Profiles.Keys.DefaultIfEmpty(-1).First();
        Config.LoginAuths.Add(new CharaAuthentication
        {
            PlayerName = PlayerData.Name,
            WorldId = PlayerData.HomeWorldId,
            ContentId = PlayerData.CID,
            ProfileIdx = autoSelectedKey
        });
        _config.Save();
    }

    public int AddProfileToAccount(AccountProfile profile)
    {
        // throw an exception if any existing profiles have a matching key.
        if (Config.Profiles.Values.Any(p => p.Key == profile.Key))
            throw new Exception("A profile with the same key already exists.");
        // Append this to the dictionary, increasing its idx by 1 from the last.
        var idx = Config.Profiles.Any() ? Config.Profiles.Max(p => p.Key) + 1 : 0;
        if (!Config.Profiles.TryAdd(idx, profile))
            return -1;

        _logger.LogInformation($"Profile added at index {idx}");
        _config.Save();
        return idx;
    }

    public void SetProfileForLoginAuth(ulong cid, int profileIdx)
    {
        if (!Config.Profiles.ContainsKey(profileIdx))
            throw new Exception("The specified profile index does not exist.");

        if (Config.LoginAuths.Find(la => la.ContentId == cid) is not { } auth)
            throw new Exception("No authentication found for the current character.");

        auth.ProfileIdx = profileIdx;
        _config.Save();
    }

    /// <summary> 
    ///     Updates the authentication.
    /// </summary>
    public void UpdateAuthentication(string secretKey, ConnectionResponse response)
    {
        // Locate the profile that we just connected with.
        if (Config.Profiles.Values.FirstOrDefault(p => p.Key == secretKey) is not { } match)
        {
            Svc.Logger.Error("SHOULD NEVER SEE THIS.");
            return;
        }

        // Mark that it had a valid connection and update the userUID.
        if (string.IsNullOrEmpty(match.ProfileLabel))
            match.ProfileLabel = $"Auto-Profile Label [{DateTime.Now:yyyy-MM-dd}]";
        match.UserUID = response.User.UID;
        match.HadValidConnection = true;

        // now we should iterate through the rest of our profiles, and check the UID's.
        // if there is a UID that is listed inside that does not match any UIDS in the connected accounts,
        // they are outdated.
        var accountUids = new HashSet<string>(response.ActiveAccountUidList) { response.User.UID };
        foreach (var (idx, profile) in Config.Profiles.ToArray())
        {
            if (string.IsNullOrWhiteSpace(profile.UserUID))
                continue;
            // If the account Uid list does not contain this profile's UID,
            // it was deleted via discord, or a cleanup service.
            if (!accountUids.Contains(profile.UserUID))
            {
                _logger.LogWarning($"Removing outdated profile {profile.ProfileLabel} with UID {profile.UserUID}");
                Config.Profiles.Remove(idx);
            }
        }
    }
}
