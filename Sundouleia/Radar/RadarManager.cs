using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using SundouleiaAPI.Network;

namespace Sundouleia.Radar;

/// <summary>
///     Manages the current radar zone and all resolved, valid users. <para />
///     identifies players valid for chatting and list display, with additional info.
/// </summary>
public sealed class RadarManager
{
    private readonly ILogger<RadarManager> _logger;
    private readonly CharaObjectWatcher _watcher;

    private HashSet<RadarUser> _users = new();
    private List<RadarUser> _rendered = new();

    public RadarManager(ILogger<RadarManager> logger, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _watcher = watcher;
    }

    public IReadOnlyCollection<RadarUser> AllUsers => _users;
    public IReadOnlyCollection<RadarUser> RenderedUsers => _rendered;

    public bool HasUser(OnlineUser user)
        => _users.Any(u => u.UID == user.User.UID);
    public bool IsUserValid(UserData user)
        => _users.FirstOrDefault(u => u.UID == user.UID) is { } match && match.IsValid;

    // Add a user, regardless of visibility.
    public void AddRadarUser(OnlineUser user, IntPtr address)
    {
        _logger.LogDebug($"Adding radar user {user.User.AnonName} with address {address:X}.", LoggerType.RadarManagement);
        _users.Add(new(user, address));
        // recreate the rendered list.
        RecreateLists();
    }

    // Remove a user, regardless of visibility.
    public void RemoveRadarUser(UserData user)
    {
        _logger.LogDebug($"(Radar) A user was removed.", LoggerType.RadarManagement);
        _users.RemoveWhere(u => u.UID == user.UID);
        // recreate the rendered list.
        RecreateLists();
    }

    public void UpdateVisibility(UserData existing, IntPtr address)
    {
        if (_users.FirstOrDefault(u => u.UID == existing.UID) is not { } user)
            return;
        // Update visibility.
        _logger.LogDebug($"(Radar) {user.AnonymousName} is now {(address != IntPtr.Zero ? "rendered" : "unrendered")}.", LoggerType.RadarManagement);
        user.BindToAddress(address);
        // recreate the rendered list.
        RecreateLists();
    }

    public void UpdateUserState(OnlineUser newState)
    {
        // Firstly determine if the user even exists. If they dont, return.
        if (_users.FirstOrDefault(u => u.UID == newState.User.UID) is not { } match)
            return;        
        // Update their state.
        match.UpdateState(newState);
        
        // If their new hashedIdent changed, check for visibility.
        if (!string.IsNullOrEmpty(match.HashedIdent) && match.IsValid)
            // Try and bind them to an address.
            if (_watcher.TryGetExisting(match.HashedIdent, out IntPtr foundAddress))
                match.BindToAddress(foundAddress);

        // recreate the rendered list.
        RecreateLists();
    }

    public void ClearUsers()
    {
        _logger.LogDebug("Clearing all valid radar users.", LoggerType.RadarManagement);
        _users.Clear();
        // recreate the rendered list.
        RecreateLists();
    }

    private void RecreateLists()
        => _rendered = _users.Where(u => u.IsValid).ToList();
}