using Sundouleia.Services.Mediator;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class GroupsManager
{
    private readonly ILogger<GroupsManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly FolderConfig _config;

    public GroupsManager(ILogger<GroupsManager> logger, SundouleiaMediator mediator, FolderConfig config)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
    }

    public FolderStorage Config => _config.Current;

    public List<SundesmoGroup> Groups => _config.Current.Groups;

    // Slowly migrate the below methods into helper functions for group interactions performed via the GroupsDDS


    // Attempts to add a new sundesmoGroup to the config.
    // Fails if the name already exists. 
    public bool TryAddNewGroup(SundesmoGroup group)
    {
        if (_config.LabelExists(group.Label))
        {
            _logger.LogWarning($"A group with the name {{{group.Label}}} already exists!");
            return false;
        }
        // Default the linked UID's.
        group.LinkedUids = new();
        Config.Groups.Add(group);
        _config.Save();
        return true;
    }

    public void UpdateIcon(SundesmoGroup group, FAI newIcon, uint newColor)
    {
        group.Icon = newIcon;
        group.IconColor = newColor;
        _config.Save();
    }

    public bool UpdateLabel(SundesmoGroup group, string newLabel, uint newColor)
    {
        if (_config.LabelExists(group.Label))
        {
            _logger.LogWarning($"A group with the name {{{group.Label}}} already exists!");
            return false;
        }

        group.Label = newLabel;
        group.LabelColor = newColor;
        _config.Save();
        return true;
    }

    public void UpdateDescription(SundesmoGroup group, string newDesc)
    {
        group.Description = newDesc;
        _config.Save();
    }

    public bool UpdateDetails(string existing, SundesmoGroup updated)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(existing, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{existing}}} to update.");
            return false;
        }

        match.Icon = updated.Icon;
        match.IconColor = updated.IconColor;
        match.Label = updated.Label;
        match.LabelColor = updated.LabelColor;
        match.Description = updated.Description;
        match.ShowOffline = updated.ShowOffline;
        _config.Save();
        return true;
    }

    public bool UpdateDetails(SundesmoGroup group, SundesmoGroup updated)
    {
        if (_config.LabelExists(updated.Label))
        {
            _logger.LogWarning($"A group with the name {{{updated.Label}}} already exists!");
            return false;
        }

        group.Icon = updated.Icon;
        group.IconColor = updated.IconColor;
        group.Label = updated.Label;
        group.LabelColor = updated.LabelColor;
        group.Description = updated.Description;
        group.ShowOffline = updated.ShowOffline;
        _config.Save();
        return true;
    }

    public bool LinkToGroup(string uid, string groupLabel)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(groupLabel, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{groupLabel}}} to link user {{{uid}}} to.");
            return false;
        }

        LinkToGroup(uid, match);
        return true;
    }

    public bool LinkToGroup(IEnumerable<string> uids, string groupLabel)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(groupLabel, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{groupLabel}}} to link users to.");
            return false;
        }

        LinkToGroup(uids, match);
        return true;
    }

    public void LinkToGroup(string uid, SundesmoGroup group)
    {
        group.LinkedUids.Add(uid);
        _config.Save();
    }

    public void LinkToGroup(IEnumerable<string> uids, SundesmoGroup group)
    {
        foreach (string uid in uids)
            group.LinkedUids.Add(uid);
        _config.Save();
    }

    public bool UnlinkFromGroup(string uid, string groupLabel)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(groupLabel, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{groupLabel}}} to unlink user {{{uid}}} from.");
            return false;
        }
        UnlinkFromGroup(uid, match);
        return true;
    }

    public bool UnlinkFromGroup(IEnumerable<string> uids, string groupLabel)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(groupLabel, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{groupLabel}}} to unlink users from.");
            return false;
        }
        UnlinkFromGroup(uids, match);
        return true;
    }

    public void UnlinkFromGroup(string uid, SundesmoGroup group)
    {
        group.LinkedUids.Remove(uid);
        _config.Save();
    }

    public void UnlinkFromGroup(IEnumerable<string> uids, SundesmoGroup group)
    {
        foreach (string uid in uids)
            group.LinkedUids.Remove(uid);
        _config.Save();
    }

    public bool TryRemoveGroup(string groupLabel)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(groupLabel, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{groupLabel}}} to remove.");
            return false;
        }
        
        Config.Groups.Remove(match);
        _config.Save();
        return true;
    }

    public bool TryRemoveGroup(SundesmoGroup group)
    {
        if (!Config.Groups.Contains(group))
        {
            _logger.LogWarning($"No group found with the name {{{group.Label}}} to remove.");
            return false;
        }
        Config.Groups.Remove(group);
        _config.Save();
        return true;
    }
}
