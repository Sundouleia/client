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

    private Dictionary<string, SundesmoGroup> _lookupMap = new();

    public GroupsManager(ILogger<GroupsManager> logger, SundouleiaMediator mediator, FolderConfig config)
    {
        _logger = logger;
        _mediator = mediator;
        _config = config;

        _lookupMap = Config.Groups.ToDictionary(g => g.Label, g => g);
    }

    public FolderStorage Config => _config.Current;

    public List<SundesmoGroup> Groups => _config.Current.Groups;

    #region Filter Edits
    // Moves the sort filters of selected indexes to a new target index location in the list
    public bool MoveFilters(string groupName, int[] fromIndices, int targetIdx)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;

        var sortOrder = group.SortOrder;
        // Sort in descending order for efficient removal
        Array.Sort(fromIndices);
        Array.Reverse(fromIndices);

        // Collect items to move
        var toMove = new List<FolderSortFilter>(fromIndices.Length);
        foreach (var item in fromIndices)
            toMove.Add(sortOrder[item]);

        // Remove from the list in descending order
        foreach (var idx in fromIndices)
            sortOrder.RemoveAt(idx);

        sortOrder.InsertRange(Math.Min(targetIdx, sortOrder.Count), toMove);
        _config.Save();
        return true;
    }

    public bool RemoveFilter(string groupName, int idx)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group) || (idx < 0 || idx >= group.SortOrder.Count))
            return false;
        group.SortOrder.RemoveAt(idx);
        _config.Save();
        return true;
    }

    public bool AddFilter(string groupName, FolderSortFilter filter)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group) || group.SortOrder.Contains(filter))
            return false;
        group.SortOrder.Add(filter);
        _config.Save();
        return true;
    }

    public bool ClearFilters(string groupName)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;
        group.SortOrder.Clear();
        _config.Save();
        return true;
    }
    #endregion FilterEdits

    public bool TryRename(string groupName, string newName)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;
        return TryRename(group, newName);
    }

    public bool TryRename(SundesmoGroup group, string newName)
    {
        if (_config.LabelExists(newName))
        {
            _logger.LogWarning($"A group with the name {{{newName}}} already exists!");
            return false;
        }

        // Remove the old lookup.
        _lookupMap.Remove(group.Label);
        // Update the name.
        group.Label = newName;
        // Update lookup map
        _lookupMap[newName] = group;
        _config.Save();
        return true;
    }

    #region Style Edits
    public bool TrySetIcon(string groupName, FAI newIcon, uint newColor)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;
        SetIcon(group, newIcon, newColor);
        return true;
    }

    public void SetIcon(SundesmoGroup group, FAI newIcon, uint newColor)
    {
        group.Icon = newIcon;
        group.IconColor = newColor;
        _config.Save();
    }

    public bool TrySetStyle(string groupName, uint icon, uint label, uint border, uint gradient)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;
        SetStyle(group, icon, label, border, gradient);
        return true;
    }

    public void SetStyle(SundesmoGroup group, uint icon, uint label, uint border, uint gradient)
    {
        group.IconColor = icon;
        group.LabelColor = label;
        group.BorderColor = border;
        group.GradientColor = gradient;
        _config.Save();
    }

    public bool TrySetState(string groupName, bool showOffline, bool showIfEmpty)
    {
        if (!_lookupMap.TryGetValue(groupName, out var group))
            return false;
        SetState(group, showOffline, showIfEmpty);
        return true;
    }

    public void SetState(SundesmoGroup group, bool showOffline, bool showIfEmpty)
    {
        group.ShowOffline = showOffline;
        group.ShowIfEmpty = showIfEmpty;
        _config.Save();
    }

    #endregion Style Edits
    public bool TryMergeFolder(string fromFolder, string toFolder)
    {
        if (!_lookupMap.TryGetValue(fromFolder, out var fromGroup))
            return false;
        if (!_lookupMap.TryGetValue(toFolder, out var toGroup))
            return false;
        // Perform the merge.
        MergeGroups(fromGroup, toGroup);
        return true;
    }

    /// <summary>
    ///     Merges all users from one group into another, 
    ///     removing the group it was moved from.
    /// </summary>
    /// <param name="from"> The group to move users from. </param>
    /// <param name="to"> The group to move users to. </param>
    /// <remarks> Might add some kind of history system to have a failsafe. </remarks>
    public void MergeGroups(SundesmoGroup from, SundesmoGroup to)
    {
        to.LinkedUids = from.LinkedUids.Union(to.LinkedUids).ToHashSet();
        Config.Groups.Remove(from);
        _config.Save();
    }

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
        _logger.LogInformation($"Adding new group {{{group.Label}}} to config.");
        group.LinkedUids = new();
        Config.Groups.Add(group);
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
