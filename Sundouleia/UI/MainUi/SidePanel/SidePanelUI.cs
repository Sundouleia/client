using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui.MainWindow;

// We could ideally have this continuously running but never drawing much
// if anything at all while not expected.
// It would allow us to process the logic in the draw-loop like we want.
public class SidePanelUI : WindowMediatorSubscriberBase
{
    private readonly SundesmoTabs _sundesmoTabs;
    private readonly RequestsInDrawer _requestsDrawer;
    private readonly GroupsDrawer _groupsDrawer;
    private readonly SidePanelInteractions _spInteractions;
    private readonly SidePanelGroups _spGroups;
    private readonly SidePanelService _service;

    // Likely phase this out when we find a better alternative or something, idk.
    // Or instead revise it to be something better if searchable.
    private RequestsGroupSelector GroupSelector;

    public SidePanelUI(ILogger<SidePanelUI> logger, SundouleiaMediator mediator,
        SundesmoTabs sundesmoTabs, RequestsInDrawer requestsDrawer,
        GroupsDrawer groupsDrawer, SidePanelInteractions interactions, 
        SidePanelGroups groups, SidePanelService service, GroupsDrawSystem groupsDDS)
        : base(logger, mediator, "##SundouleiaInteractionsUI")
    {
        _sundesmoTabs = sundesmoTabs;
         _requestsDrawer = requestsDrawer;
        _groupsDrawer = groupsDrawer;
        _spInteractions = interactions;
        _spGroups = groups;
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
            case ResponseCache irc:
                DrawIncomingRequests(irc);
                return;
            case InteractionsCache ic:
                DrawInteractionsPanel(ic);
                return;
            case NewGroupCache ngc:
                DrawNewGroupPanel(ngc);
                return;
            case NewFolderGroupCache nfgc:
                DrawNewFolderGroupPanel(nfgc);
                return;
            case GroupEditorCache gec:
                DrawGroupEditorPanel(gec);
                return;
        }
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

    private void DrawInteractionsPanel(InteractionsCache ic)
    {
        // Draw tabs
        _sundesmoTabs.Draw(ImGui.GetContentRegionAvail().X);

        using var _ = CkRaii.Child("SundesmoInteractions", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;
        var dispName = ic.DisplayName;

        if (ic.Sundesmo is not { } sundesmo)
            return;

        // Draw content based on tab.
        if (_sundesmoTabs.TabSelection is SundesmoTabs.SelectedTab.Interactions)
            _spInteractions.DrawInteractions(ic, sundesmo, dispName, width);
        else
            _spInteractions.DrawPermissions(ic, sundesmo, dispName, width);
    }

    private void DrawNewGroupPanel(NewGroupCache  ngc)
    {
        using var _ = CkRaii.Child("GroupCreator", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered($"Create New Group", UiFontService.Default150Percent);
        _spGroups.DrawCreator(ngc, width);
        ImGui.Separator();
        // Draw the center button for creating.
        CkGui.SetCursorXtoCenter(width * .5f);
        if (CkGui.IconTextButtonCentered(FAI.FolderPlus, "Add New Group", width * .5f, disabled: !ngc.IsGroupValid()))
        {
            _logger.LogDebug($"Adding New Group [{ngc.NewGroup.Label}]");
            if (ngc.TryAddCreatedGroup())
            {
                _logger.LogInformation($"Added New Group [{ngc.NewGroup.Label}]");
                _service.ClearDisplay();
            }
        }
    }

    private void DrawNewFolderGroupPanel(NewFolderGroupCache nfgc)
    {
        using var _ = CkRaii.Child("FolderGroupCreator", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered($"Create New Folder", UiFontService.Default150Percent);
        ImGui.Separator();
        _spGroups.DrawFolderCreator(nfgc, width);
        ImGui.Separator();
        // Draw the center button for creating.
        CkGui.SetCursorXtoCenter(width * .5f);
        if (CkGui.IconTextButtonCentered(FAI.FolderPlus, "Add New Folder", width * .5f))
        {
            _logger.LogDebug($"Adding New Folder [{nfgc.NewFolderName}]");
            if (nfgc.TryAddCreatedFolderGroup())
                _logger.LogInformation($"Added New Folder [{nfgc.NewFolderName}]");
        }
    }

    private void DrawGroupEditorPanel(GroupEditorCache gec)
    {
        using var _ = CkRaii.Child("GroupEditor", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        // Include the group name in the title.
        CkGui.FontTextCentered($"Editing {gec.GroupInEditor.Label}", UiFontService.Default150Percent);
        ImGui.Separator();
        _spGroups.DrawGroupEditor(gec, width);
    }
}
