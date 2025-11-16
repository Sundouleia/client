using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab : DisposableMediatorSubscriberBase
{
    private const string BASIC_POPUP_ID = "##FolderPreferences";
    private const string GROUP_POPUP_ID = "##GroupConfiguration";

    private readonly MainConfig _config;
    private readonly FolderConfig _folderConfig;
    private readonly DrawEntityFactory _factory;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;

    private List<IDynamicFolder> _mainFolders;
    private List<IDynamicFolder> _groupFolders;
    
    private string _filterMain    = string.Empty;
    private string _filterGroups  = string.Empty;

    private string _openPopup     = string.Empty;
    private bool   _viewingGroups = false;
    private bool   _basicExpanded = false;

    private float  _popoutWidth   => 200f * ImGuiHelpers.GlobalScale;
    public WhitelistTab(ILogger<WhitelistTab> logger, SundouleiaMediator mediator,
        MainConfig config, FolderConfig folderConfig, DrawEntityFactory factory, 
        GroupsManager groups, SundesmoManager sundesmos)
        : base(logger, mediator)
    {
        _config = config;
        _folderConfig = folderConfig;
        _factory = factory;
        _groups = groups;
        _sundesmos = sundesmos;

        // Need to regen main folders on config setting changes.
        GenerateFoldersBasic();
        GenerateFoldersGroups();

        Mediator.Subscribe<FolderUpdateSundesmos>(this, _ => GenerateFoldersGroups());
    }

    public List<IDynamicFolder> MainFolders => _mainFolders;
    public List<IDynamicFolder> GroupFolders => _groupFolders;

    private void GenerateFoldersBasic()
    {
        var folders = new List<IDynamicFolder>();
        if (_folderConfig.Current.VisibleFolder)
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagVisible, FolderOptions.DefaultShowEmpty));
        if (_folderConfig.Current.OfflineFolder)
        {
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOnline, FolderOptions.Default));
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOffline, FolderOptions.Default));
        }
        else
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, FolderOptions.DefaultShowEmpty));
        _mainFolders = folders;
    }

    private void GenerateFoldersGroups()
    {
        var groupFolders = new List<IDynamicFolder>();
        foreach (var group in _groups.Config.Groups)
            groupFolders.Add(_factory.CreateGroupFolder(group, FolderOptions.DefaultShowEmpty));
        groupFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, FolderOptions.Default));
        _groupFolders = groupFolders;
    }

    public void DrawWhitelistSection()
    {
        if (_viewingGroups)
        {
            DrawGroupSearch();

            using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
            // Draw Folders out.
            foreach (var folder in _groupFolders)
                folder.DrawContents();
        }
        else
        {
            DrawBasicSearch();

            using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
            // Draw Folders out.
            foreach (var folder in _mainFolders)
                folder.DrawContents();
        }
    }

    private void DrawBasicSearch()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = _basicExpanded ? CkStyle.GetFrameRowsHeight(4).AddWinPadY() - ImUtf8.ItemSpacing.Y : ImUtf8.FrameHeightSpacing;
        var rightWidth = CkGui.IconTextButtonSize(FAI.Globe, "Basic") + CkGui.IconButtonSize(FAI.Cog).X;

        using (ImRaii.Group())
        {
            if (FancySearchBar.Draw("SundesmoSearch", width, ref _filterMain, "filter..", 128, rightWidth, RightContent))
                foreach (var folder in _mainFolders)
                    folder.UpdateItemsForFilter(_filterMain);

            if (_basicExpanded)
                DrawBasicConfigOptions(width);
        }
        if (_basicExpanded)
        {
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
        }
        else
        {
            ImGui.Separator();
        }

        void RightContent()
            {
                if (CkGui.IconTextButton(FAI.Globe, "Basic", null, true, _basicExpanded))
                {
                    _basicExpanded = false;
                    _viewingGroups = !_viewingGroups;
                }
                CkGui.AttachToolTip("Switch to Groups View");

                ImGui.SameLine(0, 0);
                if (CkGui.IconButton(FAI.Cog, inPopup: !_basicExpanded))
                    _basicExpanded = !_basicExpanded;
                CkGui.AttachToolTip("Configure preferences for default folders.");
            }
    }

    private void DrawBasicConfigOptions(float width)
    {
        var bgCol = _basicExpanded ? ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f) : 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, ImGui.GetStyle().CellPadding with { Y = 0 });
        using var child = CkRaii.ChildPaddedW("BasicExpandedChild", width, CkStyle.ThreeRowHeight(), bgCol, 5f);
        using var _ = ImRaii.Table("BasicExpandedTable", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV);
        if (!_)
            return;

        ImGui.TableSetupColumn("Displays");
        ImGui.TableSetupColumn("Preferences");
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        var showVisible = _folderConfig.Current.VisibleFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateLabel, ref showVisible))
        {
            _folderConfig.Current.VisibleFolder = showVisible;
            _folderConfig.Save();
            Logger.LogInformation("Regenerating Basic Folders due to Visible Folder setting change.");
            GenerateFoldersBasic();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowVisibleSeparateTT);

        var showOffline = _folderConfig.Current.OfflineFolder;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateLabel, ref showOffline))
        {
            _folderConfig.Current.OfflineFolder = showOffline;
            _folderConfig.Save();
            GenerateFoldersBasic();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.ShowOfflineSeparateTT);

        var favoritesFirst = _folderConfig.Current.FavoritesFirst;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FavoritesFirstLabel, ref favoritesFirst))
        {
            _folderConfig.Current.FavoritesFirst = favoritesFirst;
            _folderConfig.Save();
            GenerateFoldersBasic();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FavoritesFirstTT);

        ImGui.TableNextColumn();

        var nickOverName = _folderConfig.Current.NickOverPlayerName;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.PreferNicknamesLabel, ref nickOverName))
        {
            _folderConfig.Current.NickOverPlayerName = nickOverName;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.PreferNicknamesTT);

        var useFocusTarget = _folderConfig.Current.TargetWithFocus;
        if (ImGui.Checkbox(CkLoc.Settings.GroupPrefs.FocusTargetLabel, ref useFocusTarget))
        {
            _folderConfig.Current.TargetWithFocus = useFocusTarget;
            _folderConfig.Save();
        }
        CkGui.AttachToolTip(CkLoc.Settings.GroupPrefs.FocusTargetTT);
    }

    private void DrawGroupSearch()
    {
        var rightWidth = CkGui.IconTextButtonSize(FAI.PeopleGroup, "Groups") + CkGui.IconButtonSize(FAI.Filter).X;
        if (FancySearchBar.Draw("SundesmoSearch", ImGui.GetContentRegionAvail().X, ref _filterGroups, "filter..", 128, rightWidth, RightContent))
            foreach (var folder in _groupFolders)
                folder.UpdateItemsForFilter(_filterGroups);

        ImGui.Separator();

        void RightContent()
        {
            if (CkGui.IconTextButton(FAI.PeopleGroup, "Groups", isInPopup: true))
            {
                _viewingGroups = !_viewingGroups;
                _openPopup = string.Empty;
            }
            CkGui.AttachToolTip("Switch to Basic View");

            using var col = ImRaii.PushColor(ImGuiCol.Text, !string.IsNullOrEmpty(_openPopup) ? ImGuiColors.ParsedGold : Vector4.One);
            ImGui.SameLine(0, 0);
            var pressed = CkGui.IconButton(FAI.Filter, inPopup: true);
            var popupDrawPos = ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0);
            CkGui.AttachToolTip("Change group visibility and sort order.");

            if (pressed)
                ImGui.OpenPopup(GROUP_POPUP_ID);

            DrawGroupPopup(popupDrawPos);
        }
    }

    private void DrawGroupPopup(Vector2 pos)
    {
        if (!ImGui.IsPopupOpen(GROUP_POPUP_ID))
            return;

        var size = new Vector2(_popoutWidth, ((MainUI.LastPos + MainUI.LastSize) - MainUI.LastBottomTabMenuPos).Y);
        var flags = WFlags.NoMove | WFlags.NoResize | WFlags.NoCollapse | WFlags.NoScrollbar;
        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSize(size);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1f)
            .Push(ImGuiStyleVar.PopupRounding, 5f);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedGold);
        using var popup = ImRaii.Popup(GROUP_POPUP_ID, flags);
        var id = ImGui.GetID(GROUP_POPUP_ID);
        if (!popup)
            return;

        var wdl = ImGui.GetWindowDrawList();
        CkGui.ColorText("Visible Groups", ImGuiColors.ParsedGold);
        ImGui.Separator();

        var activeGroups = _groups.Config.Groups.Where(g => g.Visible);
        foreach (var group in _groups.Config.Groups)
        {
            var label = group.Label;
            var isVisible = activeGroups.Contains(group);
            var changed = false;

            // Push green color for changed state (highlight toggled items)
            using (ImRaii.PushColor(ImGuiCol.CheckMark, ImGuiColors.HealerGreen, changed))
            {
                if (ImGui.Checkbox($"##VisibleGroup_{label}", ref isVisible))
                {
                    if (isVisible)
                    {
                        //activeGroups.Add(label);
                        Svc.Logger.Information($"Enabled group visibility: {label}");
                    }
                    else
                    {
                        //activeGroups.Remove(label);
                        Svc.Logger.Information($"Disabled group visibility: {label}");
                    }
                    changed = true;
                }

                // Optional: draw an outline or indicator for groups recently toggled
                if (changed)
                    wdl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.CheckMark), ImGui.GetStyle().FrameRounding, ImDrawFlags.RoundCornersAll);
            }

            // Draw label text
            ImUtf8.SameLineInner();
            ImUtf8.TextFrameAligned(label);
        }
    }
}
