using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.Handlers;

/// <summary>
///     Handles the updates for default and group folders.
///     Also provides the results.
/// </summary>
public class FolderHandler : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly DrawEntityFactory _factory;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly RequestsManager _requests;

    // Private collections.
    private List<ISundesmoFolder> _defaultFolders;
    private List<ISundesmoFolder> _groupFolders;
    private List<DrawFolderBase> _requestFolders;

    // For now just onw filter. Maybe make modular later.
    private string _filter = string.Empty;
    private string _requestFilter = string.Empty;

    public FolderHandler(ILogger<FolderHandler> logger, SundouleiaMediator mediator, MainConfig config,
        DrawEntityFactory factory, GroupsManager groups, SundesmoManager sundesmos, RequestsManager requests)
        : base(logger, mediator)
    {
        _config = config;
        _factory = factory;
        _groups = groups;
        _sundesmos = sundesmos;
        _requests = requests;

        UpdateDefaultFolders();
        UpdateGroupFolders();

        Mediator.Subscribe<RefreshFolders>(this, _ =>
        {
            if (_.Whitelist)
                UpdateDefaultFolders();
            if (_.Groups)
                UpdateGroupFolders();
            if (_.Requests)
                UpdateRequestFolders();
        });
    }

    // Definitely optimize this later. Should not be doing a full recalculation on every update.
    public string Filter
    {
        get => _filter;
        set
        {
            if (_filter != value)
            {
                _filter = value;
                Mediator.Publish(new RefreshFolders(true, true, false));
            }
        }
    }

    public string RequestFilter
    {
        get => _requestFilter;
        set
        {
            if (_requestFilter != value)
            {
                _requestFilter = value;
                Mediator.Publish(new RefreshFolders(false, false, true));
            }
        }
    }

    public IReadOnlyList<ISundesmoFolder> DefaultFolders => _defaultFolders;
    public IReadOnlyList<ISundesmoFolder> GroupFolders => _groupFolders;
    public IReadOnlyList<DrawFolderBase> RequestFolders => _requestFolders;


    private void UpdateDefaultFolders()
    {
        Logger.LogDebug($"Getting default folders with filter {{{_filter}}}");
        List<ISundesmoFolder> drawFolders = [];
        var allSundesmos = _sundesmos.DirectPairs;
        // Limit the list by the filter.
        var filteredPairs = allSundesmos.Where(p => CheckFilter(p, _filter));
        
        if (_config.Current.ShowVisibleUsersSeparately)
        {
            var allRendered = allSundesmos.Where(u => u.IsRendered && u.IsOnline).ToImmutableList();
            var renderedFiltered = BasicSortedList(filteredPairs.Where(u => u.IsRendered && u.IsOnline));
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagVisible, renderedFiltered, allRendered));
        }

        if (_config.Current.ShowOfflineUsersSeparately)
        {
            var allOnline = allSundesmos.Where(s => s.IsOnline).ToImmutableList();
            var onlineFiltered = BasicSortedList(filteredPairs.Where(u => u.IsOnline));
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOnline, onlineFiltered, allOnline));

            var allOffline = allSundesmos.Where(FilterOfflineUsers).ToImmutableList();
            var filteredOffline = BasicSortedList(filteredPairs.Where(FilterOfflineUsers));
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOffline, filteredOffline, allOffline));
        }
        else
        {
            // Make the All tag.
            var allFiltered = BasicSortedList(filteredPairs);
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, allFiltered, allSundesmos.ToImmutableList()));
        }
        
        // Update.
        _defaultFolders = drawFolders;
    }

    private void UpdateGroupFolders()
    {
        Logger.LogDebug($"Getting group folders with filter {{{_filter}}}");
        var configGroups = _groups.Config.Groups;
        var groupFolders = new List<ISundesmoFolder>();
        var allSundesmos = _sundesmos.DirectPairs;

        foreach (var group in configGroups)
        {
            // Grab all linked sundesmos for this group.
            var allGroupSundesmos = allSundesmos
                .Where(u => group.LinkedUids.Contains(u.UserData.AliasOrUID))
                .ToImmutableList();
            
            // Apply the search filter to narrow the results.
            var filteredGroup = allGroupSundesmos.Where(u => CheckFilter(u, _filter));

            // Apply the flexible sorting system.
            var filteredAndSorted = ApplySortOrder(filteredGroup, group);

            // Add to final group folder collection.
            groupFolders.Add(_factory.CreateGroupFolder(group, filteredAndSorted, allGroupSundesmos));
        }

        // Make the All tag.
        var filteredAll = allSundesmos.Where(u => CheckFilter(u, _filter));
        var filteredAllSorted = BasicSortedList(filteredAll);
        groupFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, filteredAllSorted, allSundesmos.ToImmutableList()));

        // Update. 
        _groupFolders = groupFolders;
    }

    private List<Sundesmo> BasicSortedList(IEnumerable<Sundesmo> src)
    {
        var ret = new FolderSortBuilder(src)
            .Add(FolderSortFilter.Rendered)
            .Add(FolderSortFilter.Online)
            .Add(FolderSortFilter.Alphabetical);
        if (_config.Current.FavoritesFirst)
            ret.Add(FolderSortFilter.Favorite);
        return ret.Build();
    }

    private void UpdateRequestFolders()
    {
        Logger.LogDebug($"Getting request folders with filter {{{_requestFilter}}}");
        var incoming = _requests.Incoming;
        var pending = _requests.Outgoing;
        var requestFolders = new List<DrawFolderBase>(); // Make Request folder later.
        // Generate the folders and stuff i guess, idk.
    }

    private List<Sundesmo> ApplySortOrder(IEnumerable<Sundesmo> source, SundesmoGroup group)
    {
        var sortBuilder = new FolderSortBuilder(source);

        // If the group defines an explicit sort order, apply it first.
        if (group.SortOrder.Count > 0)
        {
            foreach (var filter in group.SortOrder)
                sortBuilder.Add(filter);
        }
        // Otherwise, fall back to the default sorting stack.
        else
        {
            foreach (var filter in DefaultSortFilter)
                sortBuilder.Add(filter);
        }

        return sortBuilder.Build();
    }

    #region Calculation Helpers
    private List<FolderSortFilter> DefaultSortFilter = [ FolderSortFilter.Rendered, FolderSortFilter.Online, FolderSortFilter.Alphabetical ];

    private bool CheckFilter(Sundesmo u, string filter)
    {
        if (filter.IsNullOrEmpty()) return true;
        // return a user if the filter matches their alias or ID, playerChara name, or the nickname we set.
        return u.UserData.AliasOrUID.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            (u.GetNickname()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (u.PlayerName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }
    
    // Faze out, it's logic is flawed.
    private bool FilterOnlineOrPausedSelf(Sundesmo u)
        => u.IsOnline;
    private bool FilterOfflineUsers(Sundesmo u)
        => !u.IsOnline || u.UserPair.OwnPerms.PauseVisuals;
    #endregion Calculation Helpers
}