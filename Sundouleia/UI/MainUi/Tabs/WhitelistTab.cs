using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly DrawEntityFactory _factory;

    private List<IDrawFolder> _drawFolders;
    private string _filter = string.Empty;
    public WhitelistTab(ILogger<WhitelistTab> logger, SundouleiaMediator mediator,
        MainConfig config, SundesmoManager sundesmos, DrawEntityFactory factory)
        : base(logger, mediator)
    {
        _config = config;
        _sundesmos = sundesmos;
        _factory = factory;

        Mediator.Subscribe<RefreshWhitelistMessage>(this, _ => _drawFolders = GetDrawFolders());
        _drawFolders = GetDrawFolders();
    }

    public void DrawWhitelistSection()
    {
        DrawSearchFilter();
        ImGui.Separator();

        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        
        // Draw whitelist.
        foreach (var item in _drawFolders)
            item.Draw();
    }

    private void DrawSearchFilter()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var buttonSize = CkGui.IconTextButtonSize(FAI.Ban, "Clear") + spacing;
        var searchWidth = width - buttonSize;

        ImGui.SetNextItemWidth(searchWidth);
        if (ImGui.InputTextWithHint("##filter", "filter results", ref _filter, 255))
            _drawFolders = GetDrawFolders();

        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.Ban, "Clear", disabled: _filter.Length is 0))
        {
            _filter = string.Empty;
            _drawFolders = GetDrawFolders();
        }
        CkGui.AttachToolTip("Clears the filter");
    }

    // TODO: Heavily update this with custom managed groups soon.
    /// <summary>
    ///     Fetches the folders to draw in the user pair list (whitelist)
    /// </summary>
    public List<IDrawFolder> GetDrawFolders()
    {
        Logger.LogDebug("Generating draw folders for whitelist tab with filter: " + _filter);
        List<IDrawFolder> drawFolders = [];
        // the list of all direct pairs.
        var allPairs = _sundesmos.DirectPairs;
        // the filters list of pairs will be the pairs that match the filter.
        var filteredPairs = allPairs
            .Where(p =>
            {
                if (_filter.IsNullOrEmpty())
                    return true;
                // return a user if the filter matches their alias or ID, playerChara name, or the nickname we set.
                return p.UserData.AliasOrUID.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                       (p.GetNickname()?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (p.PlayerName?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false);
            });

        // the alphabetical sort function of the pairs.
        string? AlphabeticalSort(Sundesmo u)
            => !string.IsNullOrEmpty(u.PlayerName)
                    ? (_config.Current.PreferNicknamesOverNames ? u.GetNickname() ?? u.UserData.AliasOrUID : u.PlayerName)
                    : u.GetNickname() ?? u.UserData.AliasOrUID;
        // filter based on who is online (or paused but that shouldnt exist yet unless i decide to add it later here)
        bool FilterOnlineOrPausedSelf(Sundesmo u)
            => u.IsOnline || (!u.IsOnline && !_config.Current.ShowOfflineUsersSeparately) || u.UserPair.OwnPerms.PauseVisuals;
        // filter based on who is online or paused, but also allow paused users to be shown if they are self.
        bool FilterPairedOrPausedSelf(Sundesmo u)
             => u.IsOnline || !u.IsOnline || u.UserPair.OwnPerms.PauseVisuals;
        bool FilterOfflineUsers(Sundesmo u) 
            => !u.IsOnline || u.UserPair.OwnPerms.PauseVisuals;
        // collect the sorted list
        List<Sundesmo> BasicSortedList(IEnumerable<Sundesmo> u)
            => u.OrderByDescending(u => u.IsRendered)
                .ThenByDescending(u => u.IsOnline)
                .ThenBy(AlphabeticalSort, StringComparer.OrdinalIgnoreCase)
                .ToList();
        // converter to immutable list
        ImmutableList<Sundesmo> ImmutablePairList(IEnumerable<Sundesmo> u) => u.ToImmutableList();

        // if we wish to display our visible users separately, then do so.
        if (_config.Current.ShowVisibleUsersSeparately)
        {
            var allVisiblePairs = ImmutablePairList(allPairs.Where(u => u.IsRendered && u.IsOnline));
            var filteredVisiblePairs = BasicSortedList(filteredPairs.Where(u => u.IsRendered && u.IsOnline));
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.CustomVisibleTag, filteredVisiblePairs, allVisiblePairs));
        }

        var allOnlinePairs = ImmutablePairList(allPairs.Where(FilterOnlineOrPausedSelf));
        var onlineFilteredPairs = BasicSortedList(filteredPairs.Where(u => u.IsOnline && FilterPairedOrPausedSelf(u)));
        drawFolders.Add(_factory.CreateDefaultFolder(_config.Current.ShowOfflineUsersSeparately ? Constants.CustomOnlineTag : Constants.CustomAllTag, onlineFilteredPairs, allOnlinePairs));

        // if we want to show offline users separately,
        if (_config.Current.ShowOfflineUsersSeparately)
        {
            var allOfflinePairs = ImmutablePairList(allPairs.Where(FilterOfflineUsers));
            var filteredOfflinePairs = BasicSortedList(filteredPairs.Where(FilterOfflineUsers));
            drawFolders.Add(_factory.CreateDefaultFolder(Constants.CustomOfflineTag, filteredOfflinePairs, allOfflinePairs));
        }

        return drawFolders;
    }
}
