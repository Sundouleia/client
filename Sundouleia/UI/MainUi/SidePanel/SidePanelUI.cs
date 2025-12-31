using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Sundouleia.DrawSystem;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui.MainWindow;

// We could ideally have this continuously running but never drawing much
// if anything at all while not expected.
// It would allow us to process the logic in the draw-loop like we want.
public class SidePanelUI : WindowMediatorSubscriberBase
{
    private readonly GroupsFolderDrawer _folderDrawer;
    private readonly SidePanelInteractions _interactions;
    private readonly SidePanelService _service;

    public SidePanelUI(ILogger<SidePanelUI> logger, SundouleiaMediator mediator,
        GroupsFolderDrawer drawer, SidePanelInteractions interactions,
        SidePanelService service)
        : base(logger, mediator, "##SundouleiaInteractionsUI")
    {
        _folderDrawer = drawer;
        _interactions = interactions;
        _service = service;

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
            case ResponseCache irc when irc.Mode is SidePanelMode.IncomingRequests:
                DrawIncomingRequests(irc);
                return;
            case ResponseCache prc when prc.Mode is SidePanelMode.PendingRequests:
                DrawPendingRequests(prc);
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
        _folderDrawer.DrawContents<GroupFolder>(width, DynamicFlags.Organizer);
    }

    private void DrawIncomingRequests(ResponseCache irc)
    {
        using var _ = CkRaii.Child("RequestResponder", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered("Request Responder", UiFontService.Default150Percent);

        CkGui.FramedIconText(FAI.ObjectGroup);
        CkGui.TextFrameAlignedInline("Bulk Selector Area");
        CkGui.TextFrameAligned($"There are currently: {irc.Selected.Count} selected requests.");
    }

    private void DrawPendingRequests(ResponseCache prc)
    {
        using var _ = CkRaii.Child("PendingRequests", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);
        var width = _.InnerRegion.X;

        CkGui.FontTextCentered("Pending Requests", UiFontService.Default150Percent);

        CkGui.FramedIconText(FAI.ObjectGroup);
        CkGui.TextFrameAlignedInline("Bulk Selector Area");
        CkGui.TextFrameAligned($"There are currently: {prc.Selected.Count} selected requests.");
    }
}
