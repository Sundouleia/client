using CkCommons;
using CkCommons.Gui;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using SundouleiaAPI.Data;
using System.Collections.Immutable;

namespace Sundouleia.Gui;
public class GroupsSelector : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly GroupsConfig _groups;
    private readonly FavoritesConfig _favorites;
    private readonly IdDisplayHandler _displayer;
    private readonly SundesmoManager _sundesmos;

    public GroupsSelector(ILogger<GroupsSelector> logger, SundouleiaMediator mediator,
        MainConfig config, GroupsConfig groups, FavoritesConfig favorites,
        IdDisplayHandler displayer, SundesmoManager sundesmos) 
        : base(logger, mediator)
    {
        _config = config;
        _groups = groups;
        _favorites = favorites;
        _displayer = displayer;
        _sundesmos = sundesmos;

        // Should become refresh groups or something i dont know.
        Mediator.Subscribe<RefreshWhitelistMessage>(this, _ => UpdatePairList());
    }

    private ImmutableList<Sundesmo>  _immutablePairs = ImmutableList<Sundesmo>.Empty;
    // Internal Storage.
    private string              _searchValue = string.Empty;
    private float               _availableWidth = 0f;
    private HashSet<Sundesmo>   _selected = new HashSet<Sundesmo>();
    private Sundesmo?           _lastSundesmo = null;

    public ImmutableList<Sundesmo>  FilteredPairs => _immutablePairs;
    public HashSet<Sundesmo>        SelectedPairs => _selected;

    public void DrawSearch()
    {
        if (FancySearchBar.Draw("Pair Search", ImGui.GetContentRegionAvail().X, "Search for Pairs", ref _searchValue, 128, ImGui.GetFrameHeight(), FavoritesFilter))
        {
            UpdatePairList();
        }
    }

    // Filter Variables. Defines if our latest filter toggle was to select or deselect all.
    private bool _curFavState = false;

    private void FavoritesFilter()
    {
        if (CkGui.IconButton(FAI.Star, inPopup: true))
        {
            var favoritePairs = _immutablePairs.Where(p => _favorites.SundesmoUids.Contains(p.UserData.UID));
            Logger.LogDebug("FavoritePairs: {0}", favoritePairs.Count());
            if (_curFavState)
            {
                Logger.LogDebug("Removing FavoritePairs: " + string.Join(", ", favoritePairs.Select(p => p.UserData.UID)));
                _selected.ExceptWith(favoritePairs);
            }
            else
            {
                Logger.LogDebug("Adding FavoritePairs: " + string.Join(", ", favoritePairs.Select(p => p.UserData.UID)));
                _selected.UnionWith(favoritePairs);
            }
            _curFavState = !_curFavState;
        }
        CkGui.AttachToolTip(_curFavState ? "Deselect all favorited pairs." : "Select all favorited pairs.");
    }
    
    public void DrawResultList()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(2f));
        using var font = ImRaii.PushFont(UiBuilder.DefaultFont);
        using var buttonKiller = ImRaii.PushColor(ImGuiCol.Button, 0xFF000000)
            .Push(ImGuiCol.ButtonHovered, 0xFF000000).Push(ImGuiCol.ButtonActive, 0xFF000000);

        _availableWidth = ImGui.GetContentRegionAvail().X;
        var skips = ImGuiClip.GetNecessarySkips(ImGui.GetFrameHeight());
        var remainder = ImGuiClip.FilteredClippedDraw(_immutablePairs, skips, CheckFilter, DrawSelectable);
        ImGuiClip.DrawEndDummy(remainder, ImGui.GetFrameHeight());
    }

    public void UpdatePairList()
    {
        // Get direct pairs, then filter them.
        var filteredPairs = _sundesmos.DirectPairs
            .Where(p =>
            {
                if (_searchValue.IsNullOrEmpty())
                    return true;
                // Match for Alias, Uid, Nick, or PlayerName.
                return p.UserData.AliasOrUID.Contains(_searchValue, StringComparison.OrdinalIgnoreCase)
                    || (p.GetNickname()?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (p.PlayerName?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false);
            });

        // Take the remaining filtered list, and sort it.
        _immutablePairs = filteredPairs
            .OrderByDescending(u => u.IsRendered)
            .ThenByDescending(u => u.IsOnline)
            .ThenBy(pair => !pair.PlayerName.IsNullOrEmpty()
                ? (_config.Current.PreferNicknamesOverNames ? pair.GetNickAliasOrUid() : pair.PlayerName)
                : pair.GetNickAliasOrUid(), StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();

        // clear any pairs in the selected that are no longer present.
        _selected.RemoveWhere(p => !_immutablePairs.Contains(p));
    }


    private bool CheckFilter(Sundesmo pair)
    {
        return pair.UserData.AliasOrUID.Contains(_searchValue, StringComparison.OrdinalIgnoreCase)
            || (pair.GetNickname()?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false)
            || (pair.PlayerName?.Contains(_searchValue, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void DrawSelectable(Sundesmo pair)
    {
        var selected = _selected.Contains(pair);
        var dispText = _displayer.GetPlayerText(pair);
        var shiftRegion = Vector2.Zero;
        // Create a child that draws out the pair element, and all its internals.
        using (ImRaii.Child("Selectable"+ pair.UserData.UID, new Vector2(_availableWidth, ImGui.GetFrameHeight())))
        {
            using (ImRaii.Group())
            {
                ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
                DrawLeftIcon(dispText.text, pair.IsRendered, pair.IsOnline);

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(dispText.text);
            }
            CkGui.AttachToolTip("Hold CTRL to multi-select.--SEP--Hold SHIFT to group multi-select");

            ImGui.SameLine(0, 0);
            shiftRegion = DrawRight(dispText.text, pair);
        }
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsMouseHoveringRect(min, max - shiftRegion);
        var color = selected
            ? CkColor.ElementBG.Uint()
            : hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : ImGui.GetColorU32(ImGuiCol.ChildBg);
        // draw the "hovered" frame color.
        ImGui.GetWindowDrawList().AddRectFilled(min, max, color);

        // handle how we select it.
        if(ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if(ImGui.GetIO().KeyCtrl)
            {
                if (selected) _selected.Remove(pair);
                else _selected.Add(pair);
                _lastSundesmo = pair;
            }
            else if (ImGui.GetIO().KeyShift && _lastSundesmo is { } lastPair)
            {
                var idx = _immutablePairs.IndexOf(lastPair);
                var newIdx = _immutablePairs.IndexOf(pair);
                // Add all pairs between the last selected and this one.
                var start = Math.Min(idx, newIdx);
                var end = Math.Max(idx, newIdx);
                var pairsToToggle = _immutablePairs.Skip(start).Take(end - start + 1);
                // If our last selection is not in the list, we should remove all entries between.
                var lastContained = _selected.Contains(lastPair);
                var curContained = _selected.Contains(pair);

                // If both active, set all to inactive.
                switch((lastContained, curContained))
                {
                    case (true, true):
                        _selected.ExceptWith(pairsToToggle);
                        break;
                    case (true, false):
                        _selected.UnionWith(pairsToToggle);
                        _selected.Add(pair);
                        break;
                    case (false, true):
                        _selected.ExceptWith(pairsToToggle);
                        break;
                    case (false, false):
                        _selected.UnionWith(pairsToToggle);
                        _selected.Add(pair);
                        break;

                }

                _lastSundesmo = pair;
            }
            else if (ImGui.GetIO().KeyAlt)
            {
                // toggle the favorite state of all selected pairs, based on if the current pair is favorited.
                if(_favorites.SundesmoUids.Contains(pair.UserData.UID))
                    _favorites.RemoveUsers(_selected.Select(p => p.UserData.UID));
                else
                    _favorites.AddUsers(_selected.Select(p => p.UserData.UID));
            }
            else
            {
                // if we didnt hold control, make what we select the only selection.
                _selected.Clear();
                _selected.Add(pair);
                _lastSundesmo = pair;
            }
        }
    }

    private void DrawLeftIcon(string displayName, bool isVisible, bool isOnline)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, isOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed);
        var userPairText = string.Empty;

        ImGui.AlignTextToFramePadding();
        if (!isOnline)
        {
            CkGui.IconText(FAI.User);
            CkGui.AttachToolTip(displayName + " is offline.");
        }
        else if (isVisible)
        {
            CkGui.IconText(FAI.Eye);
            CkGui.AttachToolTip(displayName + " is visible.");
        }
        else
        {
            CkGui.IconText(FAI.User);
            CkGui.AttachToolTip(displayName + " is online.");
        }
        ImGui.SameLine();
    }

    private Vector2 DrawRight(string dispName, Sundesmo pair)
    {
        var endX = ImGui.GetContentRegionAvail().X;
        var currentX = endX - ImGui.GetTextLineHeightWithSpacing();
        ImGui.SameLine(0, currentX);
        // Draw the favoriteStar.
        _favorites.DrawFavoriteStar(pair.UserData.UID, false);

        // If we should draw the icon, adjust the currentX and draw it.
        if (pair.UserData.Tier is { } tier)
        {
            currentX -= ImGui.GetFrameHeight();
            ImGui.SameLine(0, currentX);
            DrawTierIcon(dispName, pair.UserData, tier);
        }
        return new Vector2(endX - currentX, 0);
    }

    private void DrawTierIcon(string displayName, UserData userData, CkVanityTier tier)
    {
        if (tier is CkVanityTier.NoRole)
            return;

        var img = CosmeticService.GetSupporterInfo(userData);
        if (img.SupporterWrap is { } wrap)
        {
            ImGui.Image(wrap.Handle, new Vector2(ImGui.GetFrameHeight()));
            CkGui.AttachToolTip(img.Tooltip);
        }

    }
}
