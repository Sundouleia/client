using CkCommons.Gui;
using CkCommons.Gui.Utility;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using OtterGui.Widgets;
using Sundouleia.Gui.Components;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using System.Linq;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly DrawEntityFactory _factory;
    private readonly GroupsManager _groups;
    private readonly SundesmoManager _sundesmos;
    private readonly FolderHandler _drawFolders;

    private List<ISundesmoFolder> _mainFolders;
    private List<ISundesmoFolder> _groupFolders;
    private bool _viewingGroups = false;
    private string _filter = string.Empty;
    public WhitelistTab(ILogger<WhitelistTab> logger, SundouleiaMediator mediator,
        MainConfig config, DrawEntityFactory factory, GroupsManager groups, 
        SundesmoManager sundesmos, FolderHandler drawFolders)
        : base(logger, mediator)
    {
        _config = config;
        _factory = factory;
        _groups = groups;
        _sundesmos = sundesmos;
        _drawFolders = drawFolders;
        UpdateFolders();

        Mediator.Subscribe<RefreshFolders>(this, _ => UpdateFolders());
    }

    public void DrawWhitelistSection()
    {
        var folders = _viewingGroups ? _groupFolders : _mainFolders;
        DrawSearchFilter(folders);
        ImGui.Separator();

        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        // Draw Folders out.
        foreach (var folder in folders)
            folder.DrawContents();
    }

    // Find a way to improve this soon, right now it is a little messy.
    private void DrawSearchFilter(IEnumerable<ISundesmoFolder> toDraw)
    {
        // Pre-Determine the right width.
        var icon = _viewingGroups ? FAI.PeopleGroup : FAI.Globe;
        var text = _viewingGroups ? "Groups" : "Basic";
        var rightWidth = CkGui.IconTextButtonSize(icon, text);
        if (_viewingGroups) rightWidth += CkGui.IconButtonSize(FAI.Filter).X + ImUtf8.ItemInnerSpacing.X;
        
        // Draw the search bar.
        if (FancySearchBar.Draw("SundesmoSearch", ImGui.GetContentRegionAvail().X, "filter..", ref _filter, 128, rightWidth, RightContent))
        {
            foreach (var folder in toDraw)
                folder.UpdateItemsForFilter(_filter);
        }

        void RightContent()
        {
            if (_viewingGroups)
            {
                var pressed = CkGui.IconButton(FAI.Filter, inPopup: true);
                var popupDrawPos = ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0);
                CkGui.AttachToolTip("Arrange Group display order and visibility.");

                if (pressed)
                    ImGui.OpenPopup("##GroupConfiguration");

                DrawGroupListPopup(popupDrawPos);

                ImUtf8.SameLineInner();
            }

            if (CkGui.IconTextButton(icon, text, isInPopup: true))
                _viewingGroups = !_viewingGroups;
            CkGui.AttachToolTip(_viewingGroups ? "Switch to Basic View" : "Switch to Groups View");
        }
    }

    private void DrawGroupListPopup(Vector2 openPos)
    {
        using var s = ImRaii.PushStyle(ImGuiStyleVar.PopupBorderSize, 1);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);

        // Begin the popup thingy.
        ImGui.SetNextWindowPos(openPos);
        using var popup = ImRaii.Popup("##GroupConfiguration", ImGuiWindowFlags.AlwaysAutoResize);
        var id = ImGui.GetID("##GroupConfiguration");
        if (popup)
        {
            DrawGroupCheckboxList();
        }
    }
    public void DrawGroupCheckboxList()
    {
        var activeGroups = _groups.Config.VisibleGroupFolders; // HashSet<string>
        var wdl = ImGui.GetWindowDrawList();

        CkGui.ColorText("Visible Groups", ImGuiColors.ParsedGold);
        ImGui.Separator();

        foreach (var group in _groups.Config.Groups)
        {
            var label = group.Label;
            var isVisible = activeGroups.Contains(label);
            var changed = false;

            using var disabled = ImRaii.Disabled(false); // Optional: disable if needed, like locked groups, etc.

            // Push green color for changed state (highlight toggled items)
            using (ImRaii.PushColor(ImGuiCol.CheckMark, ImGuiColors.HealerGreen, changed))
            {
                if (ImGui.Checkbox($"##VisibleGroup_{label}", ref isVisible))
                {
                    if (isVisible)
                    {
                        activeGroups.Add(label);
                        Svc.Logger.Information($"Enabled group visibility: {label}");
                    }
                    else
                    {
                        activeGroups.Remove(label);
                        Svc.Logger.Information($"Disabled group visibility: {label}");
                    }
                    changed = true;
                }

                // Optional: draw an outline or indicator for groups recently toggled
                if (changed)
                    wdl.AddRect(
                        ImGui.GetItemRectMin(),
                        ImGui.GetItemRectMax(),
                        ImGui.GetColorU32(ImGuiCol.CheckMark),
                        ImGui.GetStyle().FrameRounding,
                        ImDrawFlags.RoundCornersAll
                    );
            }

            // Draw label text
            ImUtf8.SameLineInner();
            ImUtf8.TextFrameAligned(label);
        }
    }

    public void UpdateFolders()
    {
        if (_mainFolders is null)
            RecreateDefaultFolders();
        else
        {
            // Regenerate the items otherwise.
            foreach (var folder in _mainFolders)
                folder.RegenerateItems(string.Empty);
        }

        if (_groupFolders is null)
            RecreateGroupFolders();
        else
        {
            // Regenerate the items otherwise.
            foreach (var folder in _groupFolders)
                folder.RegenerateItems(string.Empty);
        }
    }

    private void RecreateDefaultFolders()
    {
        // Create the folders based on the current config options.
        var folders = new List<ISundesmoFolder>();

        if (_config.Current.ShowVisibleUsersSeparately)
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagVisible, FolderOptions.Default));

        if (_config.Current.ShowOfflineUsersSeparately)
        {
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOnline, FolderOptions.Default));
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagOffline, FolderOptions.Default));
        }
        else
            folders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, FolderOptions.DefaultShowEmpty));

        _mainFolders = folders;
    }

    private void RecreateGroupFolders()
    {
        // Create the folders based on the current config options.
        var groupFolders = new List<ISundesmoFolder>();
        foreach (var group in _groups.Config.Groups)
            groupFolders.Add(_factory.CreateGroupFolder(group, FolderOptions.DefaultShowEmpty));
        _groupFolders = groupFolders;
        // Ensure All folder exists.
        _groupFolders.Add(_factory.CreateDefaultFolder(Constants.FolderTagAll, FolderOptions.Default));
    }
}
