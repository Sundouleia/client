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
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

public class NewWhitelistTab : DisposableMediatorSubscriberBase
{
    private readonly WhitelistDrawer _drawer;

    private bool _viewingGroups = false;
    public NewWhitelistTab(ILogger<NewWhitelistTab> log, SundouleiaMediator mediator, 
        WhitelistDrawer drawer)
        : base(log, mediator)
    {
        _drawer = drawer;

        // Subscribe to the event that toggles between the whitelist and groups drawers.
    }

    public void DrawWhitelistSection()
    {
        var width = ImGui.GetContentRegionAvail().X;
        // The GroupsDrawer.
        if (_viewingGroups)
        {
            // Do nothing (for now)
        }
        // The BaseFoldersDrawer
        else
        {
            _drawer.DrawFilterRow(width, 64);
            _drawer.DrawContents(width);
        }
    }

    // TODO: Replace with GroupsDrawer
    //private void DrawGroupSearch()
    //{
    //    var rightWidth = CkGui.IconTextButtonSize(FAI.PeopleGroup, "Groups") + CkGui.IconButtonSize(FAI.Filter).X;
    //    if (FancySearchBar.Draw("SundesmoSearch", ImGui.GetContentRegionAvail().X, ref _filterGroups, "filter..", 128, rightWidth, RightContent))
    //        foreach (var folder in _groupFolders)
    //            folder.UpdateItemsForFilter(_filterGroups);

    //    ImGui.Separator();

    //    void RightContent()
    //    {
    //        if (CkGui.IconTextButton(FAI.PeopleGroup, "Groups", isInPopup: true))
    //        {
    //            _viewingGroups = !_viewingGroups;
    //            _openPopup = string.Empty;
    //        }
    //        CkGui.AttachToolTip("Switch to Basic View");

    //        using var col = ImRaii.PushColor(ImGuiCol.Text, !string.IsNullOrEmpty(_openPopup) ? ImGuiColors.ParsedGold : Vector4.One);
    //        ImGui.SameLine(0, 0);
    //        var pressed = CkGui.IconButton(FAI.Filter, inPopup: true);
    //        var popupDrawPos = ImGui.GetItemRectMin() + new Vector2(ImGui.GetItemRectSize().X, 0);
    //        CkGui.AttachToolTip("Change group visibility and sort order.");
    //    }
    //}
}
