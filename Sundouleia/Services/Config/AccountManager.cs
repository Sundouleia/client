using CkCommons;
using Sundouleia.Gui.Components;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Services.Configs;

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class AccountManager
{
    private readonly ILogger<AccountManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly AccountConfig _config;


    private Dictionary<int, CharaAuthentication> _authByKey = new();


    public AccountManager(ILogger<AccountManager> logger, SundouleiaMediator mediator, AccountConfig config)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
    }

    public AccountStorage Config => _config.Current;

    public void SaveConfig() => _config.Save();

    public bool TryGetAuthForPlayer([NotNullWhen(true)] out CharaAuthentication auth)
    {
        // fetch the cid of our current player.
        var cid = Svc.Framework.RunOnFrameworkThread(() => PlayerData.ContentId).Result;
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


    ///// <summary>
    /////     Retrieves the SecretKey for the currently logged in character.
    ///// </summary>
    //public AccountProfile? GetProfileForCharacter()
    //{
    //    if (!TryGetAuthForPlayer(out var auth))
    //        return null;
    //    // There was an account, so get the key index.
    //    var profileIdx = auth.ProfileIdx;

    //    // Now obtain the secret key using the profile index.
    //    if (!AccountStorage.Profiles.TryGetValue(profileIdx, out var profile))
    //        return null;
    //    // Return the key as it exists.
    //    return profile;
    //}

    public void UpdateAuthForNameAndWorldChange(ulong cid)
    {
        // locate the auth with the matching local content ID, and update the name and world if they do not match.
        if (Config.LoginAuths.Find(la => la.ContentId == cid) is not { } auth)
            return;
        // Id was valid, compare against current.
        var currentName = PlayerData.NameInstanced;
        var currentWorld = PlayerData.HomeWorldIdInstanced;
        // update the name if it has changed.
        if (auth.PlayerName != currentName) auth.PlayerName = currentName;
        // update the world ID if it has changed.
        if (auth.WorldId != currentWorld) auth.WorldId = currentWorld;
    }

    //public bool CharaHasLoginAuth() => AccountStorage.LoginAuths.Any(a => a.ContentId == PlayerData.ContentId);
    //public bool CharaHasValidLoginAuth()
    //{
    //    if (AccountStorage.LoginAuths.Find(a => a.ContentId == PlayerData.ContentId && a.ProfileIdx != -1) is not { } auth)
    //        return false;
    //    if (AccountStorage.Profiles.TryGetValue(auth.ProfileIdx, out var profile))
    //        return !string.IsNullOrEmpty(profile.Key);
    //    return false;
    //}

    //public void GenerateAuthForCurrentCharacter()
    //{
    //    var name = PlayerData.NameInstanced;
    //    var world = PlayerData.HomeWorldIdInstanced;
    //    var cid = PlayerData.ContendIdInstanced;

    //    // If we already have an auth for this character, do nothing.
    //    if (AccountStorage.LoginAuths.Any(a => a.ContentId == cid))
    //        return;

    //    _logger.LogDebug("Generating new auth for current character");
    //    var autoSelectedKey = AccountStorage.Profiles.Keys.DefaultIfEmpty(-1).First();
    //    AccountStorage.LoginAuths.Add(new CharaAuthentication
    //    {
    //        PlayerName = PlayerData.NameInstanced,
    //        WorldId = PlayerData.HomeWorldIdInstanced,
    //        ContentId = PlayerData.ContendIdInstanced,
    //        ProfileIdx = autoSelectedKey
    //    });
    //    Save();
    //}

    //public int AddProfileToAccount(AccountProfile profile)
    //{
    //    // throw an exception if any existing profiles have a matching key.
    //    if (AccountStorage.Profiles.Values.Any(p => p.Key == profile.Key))
    //        throw new Exception("A profile with the same key already exists.");
    //    // Append this to the dictionary, increasing its idx by 1 from the last.
    //    var idx = AccountStorage.Profiles.Any() ? AccountStorage.Profiles.Max(p => p.Key) + 1 : 0;
    //    if (AccountStorage.Profiles.TryAdd(idx, profile))
    //    {
    //        _logger.LogInformation($"Added new profile: {profile.ProfileLabel}");
    //        return idx;
    //    }
    //    return -1;
    //}

    //public void SetProfileForLoginAuth(ulong cid, int profileIdx)
    //{
    //    if (!AccountStorage.Profiles.ContainsKey(profileIdx))
    //        throw new Exception("The specified profile index does not exist.");

    //    if (AccountStorage.LoginAuths.Find(la => la.ContentId == cid) is not { } auth)
    //        throw new Exception("No authentication found for the current character.");

    //    auth.ProfileIdx = profileIdx;
    //    Save();
    //}

    ///// <summary> 
    /////     Updates the authentication.
    ///// </summary>
    //public void UpdateAuthentication(string secretKey, ConnectionResponse response)
    //{
    //    // Locate the profile that we just connected with.
    //    if (AccountStorage.Profiles.Values.FirstOrDefault(p => p.Key == secretKey) is not { } match)
    //    {
    //        Svc.Logger.Error("SHOULD NEVER SEE THIS.");
    //        return;
    //    }

    //    // Mark that it had a valid connection and update the userUID.
    //    if (string.IsNullOrEmpty(match.ProfileLabel))
    //        match.ProfileLabel = $"Auto-Profile Label [{DateTime.Now:yyyy-MM-dd}]";
    //    match.UserUID = response.User.UID;
    //    match.HadValidConnection = true;

    //    // now we should iterate through the rest of our profiles, and check the UID's.
    //    // if there is a UID that is listed inside that does not match any UIDS in the connected accounts,
    //    // they are outdated.
    //    var accountUids = new HashSet<string>(response.ActiveAccountUidList) { response.User.UID };
    //    foreach (var (idx, profile) in AccountStorage.Profiles.ToArray())
    //    {
    //        if (string.IsNullOrWhiteSpace(profile.UserUID))
    //            continue;
    //        // If the account Uid list does not contain this profile's UID,
    //        // it was deleted via discord, or a cleanup service.
    //        if (!accountUids.Contains(profile.UserUID))
    //        {
    //            _logger.LogWarning($"Removing outdated profile {profile.ProfileLabel} with UID {profile.UserUID}");
    //            AccountStorage.Profiles.Remove(idx);
    //        }
    //    }
    //}

    ///// <summary> Checks if the configuration is valid </summary>
    ///// <returns> True if the current server storage object is not null </returns>
    //public bool HasValidAccount() => AccountStorage.Profiles.Count is not 0;

    ///// <summary> Requests to save the configuration service file to the clients computer. </summary>
    //public void Save()
    //{
    //    var caller = new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? "Unknown";
    //    _logger.LogDebug("{caller} Calling config save", caller);
    //    _accountConfig.Save();
    //}

    ///// <summary>Retrieves the nickname associated with a given UID (User Identifier).</summary>
    ///// <returns>Returns the nickname as a string if found; otherwise, returns null.</returns>
    //internal string? GetNicknameForUid(string uid)
    //{
    //    if (NicknameStorage.Nicknames.TryGetValue(uid, out var nickname))
    //    {
    //        if (string.IsNullOrEmpty(nickname))
    //            return null;
    //        // Return the found nickname
    //        return nickname;
    //    }
    //    return null;
    //}


    ///// <summary> Set a nickname for a user identifier. </summary>
    ///// <param name="uid">the user identifier</param>
    ///// <param name="nickname">the nickname to add</param>
    //internal void SetNicknameForUid(string uid, string nickname)
    //{
    //    if (string.IsNullOrEmpty(uid))
    //        return;

    //    NicknameStorage.Nicknames[uid] = nickname;
    //    _nickConfig.Save();
    //}

    //// Updates the opened default public folders in the groups config.
    //public void ToggleWhitelistFolderState(string folder)
    //{
    //    _groupConfig.Current.OpenedDefaultFolders.SymmetricExceptWith(new[] { folder });
    //    _groupConfig.Save();
    //}
}
