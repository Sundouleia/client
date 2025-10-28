using Sundouleia.Services.Mediator;

namespace Sundouleia.PlayerClient;

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class GroupsManager
{
    public static readonly IEnumerable<string> DefaultLabels = [Constants.FolderTagAll, Constants.FolderTagVisible, Constants.FolderTagOnline, Constants.FolderTagOffline];

    private readonly ILogger<GroupsManager> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly GroupsConfig _config;

    public GroupsManager(ILogger<GroupsManager> logger, SundouleiaMediator mediator, GroupsConfig config)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;
    }

    public GroupsStorage Config
        => _config.Current;

    public void SaveConfig()
        => _config.Save();

    // Could do generic fallback here for groups
    // and defaults if we restrict groups from having names assigned to defaults.
    public bool IsOpen(string label)
    {
        // Check defaults first.
        if (DefaultLabels.Contains(label))
            return Config.OpenedDefaultFolders.Contains(label);
        // Fallback to groups.
        return Config.OpenedGroupFolders.Contains(label);
    }

    public bool IsOpen(SundesmoGroup group)
        => Config.OpenedGroupFolders.Contains(group.Label);

    public void ToggleState(string label)
    {
        // Handle default folders.
        if (DefaultLabels.Contains(label))
        {
            if (!Config.OpenedDefaultFolders.Remove(label))
                Config.OpenedDefaultFolders.Add(label);
            _config.Save();
            return;
        }

        // Handle Group Folders.
        if (!Config.OpenedGroupFolders.Remove(label))
            Config.OpenedGroupFolders.Add(label);
        _config.Save();
    }

    public IEnumerable<string> ActiveGroups()
        => Config.Groups.Where(g => g.LinkedUids.Count > 0).Select(g => g.Label);

    public void ToggleState(SundesmoGroup group)
    {
        if (!Config.OpenedGroupFolders.Remove(group.Label))
            Config.OpenedGroupFolders.Add(group.Label);
        _config.Save();
    }

    // Attempts to add a new sundesmoGroup to the config.
    // Fails if the name already exists. 
    public bool TryAddNewGroup(SundesmoGroup group)
    {
        if (Config.Groups.Any(g => g.Label.Equals(group.Label, StringComparison.Ordinal)))
        {
            _logger.LogWarning($"A group with the name {{{group.Label}}} already exists!");
            return false;
        }
        else if (DefaultLabels.Contains(group.Label))
        {
            _logger.LogWarning($"A group cannot be created with the reserved name {{{group.Label}}}!");
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
        if (Config.Groups.Any(g => g.Label.Equals(newLabel, StringComparison.Ordinal)))
        {
            _logger.LogWarning($"A group with the name {{{newLabel}}} already exists!");
            return false;
        }
        else if (DefaultLabels.Contains(newLabel))
        {
            _logger.LogWarning($"A group cannot be renamed to the reserved name {{{newLabel}}}!");
            return false;
        }

        group.Label = newLabel;
        group.LabelColor = newColor;
        _config.Save();
        return true;
    }

    public void UpdateDescription(SundesmoGroup group, string newDesc, uint newColor)
    {
        group.Description = newDesc;
        group.DescriptionColor = newColor;
        _config.Save();
    }

    public bool UpdateDetails(string existing, SundesmoGroup updated)
    {
        if (Config.Groups.FirstOrDefault(g => g.Label.Equals(existing, StringComparison.Ordinal)) is not { } match)
        {
            _logger.LogWarning($"No group found with the name {{{existing}}} to update.");
            return false;
        }
        else if (DefaultLabels.Contains(updated.Label))
        {
            _logger.LogWarning($"A group cannot be renamed to the reserved name {{{updated.Label}}}!");
            return false;
        }
        
        match.Icon = updated.Icon;
        match.IconColor = updated.IconColor;
        match.Label = updated.Label;
        match.LabelColor = updated.LabelColor;
        match.Description = updated.Description;
        match.DescriptionColor = updated.DescriptionColor;
        match.ShowOffline = updated.ShowOffline;
        _config.Save();
        return true;
    }

    public bool UpdateDetails(SundesmoGroup group, SundesmoGroup updated)
    {
        if (DefaultLabels.Contains(updated.Label))
        {
            _logger.LogWarning($"A group cannot be renamed to the reserved name {{{updated.Label}}}!");
            return false;
        }

        group.Icon = updated.Icon;
        group.IconColor = updated.IconColor;
        group.Label = updated.Label;
        group.LabelColor = updated.LabelColor;
        group.Description = updated.Description;
        group.DescriptionColor = updated.DescriptionColor;
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
