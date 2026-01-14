using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.PlayerClient;

// Need to be careful with how we make changes here as things that affect the draw system but not the config can lead to desyncs.

/// <summary> 
///     Config Management for all Server related configs in one, including
///     helper methods to make interfacing with config data easier.
/// </summary>
public class GroupsManager : DisposableMediatorSubscriberBase
{
    private readonly FolderConfig _config;
    private readonly SundesmoManager _sundesmos;

    public GroupsManager(ILogger<GroupsManager> logger, SundouleiaMediator mediator, 
        FolderConfig config, SundesmoManager sundesmos)
        : base(logger, mediator)
    {
        _config = config;
        _sundesmos = sundesmos;

        // Update the groups based on location changes.
        Mediator.Subscribe<TerritoryChanged>(this, _ => LinkByMatchingLocation());
        // Run a check after each hub connection.
        // Ensure that a newly rendered sundesmo is checked against for location-sorted groups.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, _ => LinkByMatchingLocation(_.Sundesmo));
    }

    public FolderStorage Config => _config.Current;
    public Dictionary<string, SundesmoGroup> Groups => _config.Current.Groups;
    public List<SundesmoGroup> GroupsList => _config.Current.Groups.Values.ToList();

    public void LinkByMatchingLocation()
    {
        var curVisibleUids = _sundesmos.GetVisibleConnected().Select(s => s.UID).ToHashSet();

        // Check against all location-scoped groups. If there is any groups that match,
        // and can have new users appended, append them. Ensure matches are validated using
        // the groups LocationScope.
        foreach (var group in GroupsList.Where(g => g.AreaBound && g.Scope > 0).ToList())
        {
            // If no location data is present, skip.
            if (group.Scope is LocationScope.None || group.Location is not { } location)
                continue;

            // Ensure that it matches to our current location scope.
            if (!LocationSvc.IsMatch(location, group.Scope))
                continue;

            Logger.LogInformation($"Group {{{group.Label}}} matches current location scope {{{group.Scope}}}.");
            Logger.LogInformation($"Group Current Users: {string.Join(", ", group.LinkedUids)}");
            Logger.LogInformation($"Visible Users: {string.Join(", ", curVisibleUids)}");
            var toAdd = curVisibleUids.Except(group.LinkedUids).ToList();
            Logger.LogInformation($"Found new Users to add: {string.Join(", ", toAdd)}");

            // Link them to the group and save.
            LinkToGroup(toAdd, group);
            // Placeholder.
            Mediator.Publish(new FolderUpdateGroups());
        }
    }

    public void LinkByMatchingLocation(Sundesmo sundesmo)
    {
        // Avoid if not fully connected yet.
        if (!MainHub.IsConnectionDataSynced)
            return;

        var uid = sundesmo.UserData.UID;
        foreach (var group in GroupsList.Where(g => g.AreaBound && g.Scope > 0).ToList())
        {
            if (group.LinkedUids.Contains(uid))
                continue;

            // If no location data is present, skip.
            if (group.Scope is LocationScope.None || group.Location is not { } location)
                continue;
            // Ensure that it matches to our current location scope.
            if (!LocationSvc.IsMatch(location, group.Scope))
                continue;
            
            Logger.LogInformation($"[{group.Label}] Matches Loc. Scope ({group.Scope}) for {sundesmo.GetDisplayName()}.");
            LinkToGroup(uid, group);
            Mediator.Publish(new FolderUpdateGroups());
        }
    }

    #region Filter Edits
    // Moves the sort filters of selected indexes to a new target index location in the list
    public bool MoveFilters(SundesmoGroup group, int[] fromIndices, int targetIdx)
    {
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

    public bool RemoveFilter(SundesmoGroup group, int idx)
    {
        group.SortOrder.RemoveAt(idx);
        _config.Save();
        return true;
    }

    public bool AddFilter(SundesmoGroup group, FolderSortFilter filter)
    {
        if (group.SortOrder.Contains(filter))
            return false;

        group.SortOrder.Add(filter);
        _config.Save();
        return true;
    }

    public bool ClearFilters(SundesmoGroup group)
    {
        group.SortOrder.Clear();
        _config.Save();
        return true;
    }
    #endregion FilterEdits

    public bool TryRename(SundesmoGroup group, string newName)
    {
        if (group is null)
            throw new ArgumentNullException(nameof(group));
        
        var oldName = group.Label;

        // No-op rename
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return true;

        // Ensure the group exists under its current label
        if (!Groups.TryGetValue(oldName, out var existing) || !ReferenceEquals(existing, group))
        {
            Logger.LogError($"Group lookup desync for {{{oldName}}}.");
            return false;
        }

        // Move the SAME instance to the new key
        Groups.Remove(oldName);
        group.Label = newName;
        Groups.Add(newName, group);
        _config.Save();
        return true;
    }

    #region Style Edits
    public void SetIcon(SundesmoGroup group, FAI newIcon, uint newColor)
    {
        group.Icon = newIcon;
        group.IconColor = newColor;
        _config.Save();
    }

    public void SetStyle(SundesmoGroup group, uint icon, uint label, uint border, uint gradient)
    {
        group.IconColor = icon;
        group.LabelColor = label;
        group.BorderColor = border;
        group.GradientColor = gradient;
        _config.Save();
    }

    public void SetState(SundesmoGroup group, bool showOffline)
    {
        group.ShowOffline = showOffline;
        _config.Save();
    }

    public void Save() => _config.Save();

    #endregion Style Edits

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
        Config.Groups.Remove(from.Label);
        _config.Save();
    }

    // Attempts to add a new sundesmoGroup to the config.
    // Fails if the name already exists. 
    public bool TryAddNewGroup(SundesmoGroup group)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));

        if (Groups.ContainsKey(group.Label))
        {
            Logger.LogWarning($"A group with the name {{{group.Label}}} already exists!");
            return false;
        }
        // Default the linked UID's.
        Logger.LogInformation($"Adding new group {{{group.Label}}} to config.");
        Config.Groups.TryAdd(group.Label, group);
        _config.Save();
        return true;
    }

    public void LinkToGroup(string uid, SundesmoGroup group)
    {
        group.LinkedUids.Add(uid);
        _config.Save();
    }

    public void LinkToGroup(IEnumerable<string> uids, SundesmoGroup group)
    {
        group.LinkedUids.UnionWith(uids);
        _config.Save();
    }

    public void UnlinkFromGroup(string uid, SundesmoGroup group)
    {
        group.LinkedUids.Remove(uid);
        _config.Save();
    }

    public void UnlinkFromGroup(IEnumerable<string> uids, SundesmoGroup group)
    {
        group.LinkedUids.ExceptWith(uids);
        _config.Save();
    }

    public void DeleteGroup(SundesmoGroup group)
    {
        Config.Groups.Remove(group.Label);
        Logger.LogInformation($"Deleted group {{{group.Label}}} from config.");
        _config.Save();
    }
}
