using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;
using System;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.GroupPoseModule;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace Sundouleia.Gui.MainWindow;

// We could ideally have this continuously running but never drawing much
// if anything at all while not expected.
// It would allow us to process the logic in the draw-loop like we want.
public class SidePanelUI : WindowMediatorSubscriberBase
{
    private readonly GroupOrganizer _folderDrawer;
    private readonly RequestsInDrawer _requestsDrawer;
    private readonly SidePanelInteractions _interactions;
    private readonly SidePanelService _service;

    private RequestsGroupSelector GroupSelector;

    public SidePanelUI(ILogger<SidePanelUI> logger, SundouleiaMediator mediator,
        GroupOrganizer drawer, RequestsInDrawer requestsDrawer, SidePanelInteractions interactions,
        SidePanelService service, GroupsDrawSystem groupsDDS)
        : base(logger, mediator, "##SundouleiaInteractionsUI")
    {
        _folderDrawer = drawer;
        _requestsDrawer = requestsDrawer;
        _interactions = interactions;
        _service = service;

        GroupSelector = new(logger, groupsDDS);

        Flags = WFlags.NoCollapse | WFlags.NoTitleBar | WFlags.NoScrollbar;
    }

    /// <summary>
    ///     Internal logic performed every draw frame regardless of if the window is open or not. <para />
    ///     Lets us Open/Close the window based on logic in the service using minimal computation.
    /// </summary>
    public override void PreOpenCheck()
    {
        IsOpen = _service.CanDraw;
        if (_service.DisplayMode is not SidePanelMode.GroupEditor)
            Flags |= WFlags.NoResize;
        else
            Flags &= ~WFlags.NoResize;
    }
    protected override void PreDrawInternal()
    {
        // Magic that makes the sticky pair window move with the main UI.
        var position = MainUI.LastPos;
        position.X += MainUI.LastSize.X;
        position.Y += ImGui.GetFrameHeightWithSpacing();
        ImGui.SetNextWindowPos(position);
        Flags |= WFlags.NoMove;

        float fixedWidth = _service.DisplayWidth;
        float fixedHeight = MainUI.LastSize.Y - ImGui.GetFrameHeightWithSpacing() * 2;

        if (_service.DisplayMode is SidePanelMode.GroupEditor)
            this.SetBoundaries(new(fixedWidth, fixedHeight), new(1000, fixedHeight));
        else
            this.SetBoundaries(new(fixedWidth, fixedHeight), new(fixedWidth, fixedHeight));
    }

    protected override void PostDrawInternal()
    { }

    // If this runs, it is assumed that for this frame the data is valid for drawing.
    protected override void DrawInternal()
    {
        // If there is no mode to draw, do not draw.
        if (_service.DisplayMode is SidePanelMode.None)
            return;

        // Display the correct mode.
        switch (_service.DisplayCache)
        {
            case GroupOrganizerCache goc:
                DrawGroupOrganizer(goc);
                return;
            case InteractionsCache ic:
                _interactions.DrawContents(ic);
                return;
            case ResponseCache irc:
                DrawIncomingRequests(irc);
                return;
        }
    }

    private void DrawGroupOrganizer(GroupOrganizerCache cache)
    {
        // Should be relatively simple to display this outside of some headers and stylizations.
        using var _ = CkRaii.Child("GroupOrganizer", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        CkGui.FontTextCentered("Group Organizer", UiFontService.Default150Percent);

        _folderDrawer.DrawButtonHeader(width);
        ImGui.Separator();
        _folderDrawer.DrawContents<GroupFolder>(width, DynamicFlags.SelectableDragDrop);
    }

    // TODO: Update this so that it reflects the incoming requests folder format,
    // also set scrollable selections for the selected requests for a more dynamic height.
    // 
    // It might also be more benificial to bind this to a dynamic menu of sorts so that the selections
    // are searchable, reverting back to the selector method we had before, but using a fully custom draw override.
    // This would allow us to have more managable context menus.
    private void DrawIncomingRequests(ResponseCache irc)
    {
        using var _ = CkRaii.Child("RequestResponder", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered("Bulk Request Responder", UiFontService.Default150Percent);
        ImGui.Separator();

        ImGui.Text("Selected Requests");
        _requestsDrawer.DrawSelectedRequests(width);

        ImGui.Separator();
        ImGui.Text("Added Groups");

        if (CkGui.IconTextButton(FAI.Plus, "In Area"))
        {
            _logger.LogInformation("Adding selected requests from player location.");
        }
        CkGui.AttachToolTip("Add Groups linked to this area.--NL--(All inner scopes are also included)");
        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.Plus, "In World"))
        {
            _logger.LogInformation("Adding selected requests from player world.");
        }
        CkGui.AttachToolTip("Add Groups linked to this world.--NL--(All inner scopes are also included)");
        ImUtf8.SameLineInner();
        var comboWidth = ImGui.GetContentRegionAvail().X;
        GroupSelector.DrawSelectorCombo("ReqResp", "Add To Groups..", comboWidth, comboWidth * 1.5f);

        var selHeight = ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing;
        using (var sel = CkRaii.FramedChildPaddedWH("Selected", new(width, selHeight), 0, ImGui.GetColorU32(ImGuiCol.FrameBg), 0, 1, wFlags: WFlags.NoScrollbar))
            GroupSelector.DrawSelectedList("Selected", sel.InnerRegion.X);

        // Accept and reject all buttons.
        var halfW = (width - ImUtf8.ItemInnerSpacing.X) / 2;
        using (ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.HealerGreen))
            if (CkGui.IconTextButtonCentered(FAI.Check, "Accept All", halfW))
                _logger.LogInformation("Accepting all selected requests.");
        CkGui.AttachToolTip("Accept all selected requests.");

        ImUtf8.SameLineInner();
        using (ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.DPSRed))
            if (CkGui.IconTextButtonCentered(FAI.Times, "Reject All", halfW))
                _logger.LogInformation("Rejecting all selected requests.");
        CkGui.AttachToolTip("Reject all selected requests.");
    }

}
