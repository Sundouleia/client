using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.DrawSystem;

// Requests are by far the most complex, because I want to get it right,
// but it should all be possible now.

// SIDENOTE: Should be possible to make both 'incomingFolder' and 'pendingFolder' now.
public class RequestsDrawer : DynamicDrawer<RequestEntry>
{
    // Requests are intricate, in a way.
    // Revise overtime but the entries work like so:
    // - Each entry has a way to quick-accept or quick-reject without expanding an entry.
    // - An entry can be single selected to expand another row of what nick to give, or group(s) to place them in.
    // - Entries can also be selected for bulk responding.
    // - Support SHIFT & CTRL for selections.
    // - When in Bulk response mode, either in the right side window, or at the top, display bulk responce options.
    // - This bulk responses would likely go under the search bar or something, where the config may normally show.
    private static readonly string ToolTip =
        "--COL--[L-CLICK]--COL-- Single-Select for bulk responding." +
  "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all between current & last selected ";

    private readonly MainHub _hub;
    private readonly FolderConfig _config; // Groups and defaults.
    private readonly RequestsManager _manager;
    private readonly SundesmoManager _sundesmos;

    private bool _configExpanded = false;

    // Internal vars for defaults
    private List<SundesmoGroup> _defaultGroups = [];// DefaultGroups accepted people go into.
    private bool _allowRequestedNick = true;        // Allow requests including desired nicks to set them.
    // Internal vars for temps.
    private IDynamicNode? _inResponder = null;      // For single-entry selections.
    private List<string> _addToGroupsOnAccept = []; // Groups to add to on accept.
    private string _nickOnAccept = string.Empty;    // Nick to give on accept.
    private bool _useRequestedNick = true;          // if we use the requested nick on accept.

    public RequestsDrawer(ILogger<RadarDrawer> logger, MainHub hub, FolderConfig folderConfig, 
        RequestsManager manager, SundesmoManager sundesmos, RequestsDrawSystem ds) 
        : base("##RequestsDrawer", logger, ds, new RequestCache(ds))
    {
        _hub = hub;
        _config = folderConfig;
        _manager = manager;
        _sundesmos = sundesmos;
        // We can handle interaction stuff via customizable buttons later that we will figure out as things go on.
    }

    public bool ViewingIncoming => _config.Current.ViewingIncoming;
    public bool BulkSelecting => Selector.Leaves.Count != 0;

    // Wishlist:
    // - Button to quick-add a new group for the area.
    // - Button to quick select all requests from current territory.
    // - Button to quick select all requests from current world.

    #region Search
    // Special top area here due to how it displays either essential config or bulk selection options.
    protected override void DrawSearchBar(float width, int length)
    {
        var icon = ViewingIncoming ? FAI.Envelope : FAI.Stopwatch;
        var text = ViewingIncoming ? "Incoming" : "Outgoing";
        var tmp = Cache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Wrench).X + CkGui.IconTextButtonSize(icon, text);
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            Cache.Filter = tmp;

        // If we are bulk selecting, draw the bulk selection options.
        if (BulkSelecting)
            DrawBulkSelector(width);
        else if (_configExpanded)
            DrawConfig(width);

        void DrawButtons()
        {
            if (CkGui.IconTextButton(icon, text, null, true, BulkSelecting || _configExpanded))
            {
                _config.Current.ViewingIncoming = !ViewingIncoming;
                _config.Save();
                // also clear selection when swapping.
                Selector.ClearSelected();
            }
            CkGui.AttachToolTip($"Switch to {(ViewingIncoming ? "Outgoing" : "Incoming")} requests.");

            ImGui.SameLine(0, 0);
            if (CkGui.IconButton(FAI.Wrench, disabled: BulkSelecting, inPopup: !_configExpanded))
                _configExpanded = !_configExpanded;
            CkGui.AttachToolTip("Configure preferences for requests handling.");
        }
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (BulkSelecting || _configExpanded)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }
    #endregion Search

    // We draw the folders, but they are not interactable, and are static, open.
    #region Folder Control
    protected override void DrawFolderBannerInner(IDynamicFolder<RequestEntry> folder, Vector2 region, DynamicFlags flags)
    {
        if (folder is RequestFolder rf)
            DrawRequestFolder(rf, region, flags);
        else
            base.DrawFolderBannerInner(folder, region, flags);
    }

    // Split between an incoming and outgoing folder maybe.
    private void DrawRequestFolder(RequestFolder folder, Vector2 region, DynamicFlags flags)
    {
        // No interactions, keep open.
        ImUtf8.SameLineInner();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        CkGui.ColorTextFrameAlignedInline($"[{folder.TotalChildren}]", ImGuiColors.DalamudGrey2);

        // Some buttons off to the side, but that is dependant on the folder kind. So update later.
    }

    #endregion Folder Control

    #region Leaf Control
    protected override void DrawLeaf(IDynamicLeaf<RequestEntry> leaf, DynamicFlags flags, bool selected)
    {
        if (leaf.Data.FromClient)
            DrawOutgoingEntry(leaf, flags, selected);
        else
            DrawIncomingEntry(leaf, flags, selected);
    }

    private void DrawIncomingEntry(IDynamicLeaf<RequestEntry> leaf, DynamicFlags flags, bool selected)
    {
        var responding = _inResponder == leaf;
        // Not sure how much i like this concept atm, might revise later.
        var height = responding ? CkStyle.ThreeRowHeight() : ImUtf8.FrameHeight;
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), height);
        var bgCol = (!responding && selected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        var frameCol = responding ? ImGui.GetColorU32(ImGuiCol.Button) : 0;
        using (var _ = CkRaii.FramedChild(Label + leaf.Name, size, bgCol, frameCol, 5f, 1f))
            DrawIncomingInner(leaf, _.InnerRegion, flags, responding);
    }

    private void DrawOutgoingEntry(IDynamicLeaf<RequestEntry> leaf, DynamicFlags flags, bool selected)
    {
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using (var _ = CkRaii.Child(Label + leaf.Name, size, bgCol, 5f))
            DrawOutgoingInner(leaf, _.InnerRegion, flags);
    }

    protected override void HandleInteraction(IDynamicLeaf<RequestEntry> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;
        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableLeaves) && ImGui.IsItemClicked())
        {
            // If there are no other items selected at the time of selection, mark it as the node in the responder.
            _inResponder = (_inResponder == node || BulkSelecting) ? null : node;
            // Then perform the selection.
            Selector.SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
        }
        // Handle Drag and Drop.
        if (flags.HasAny(DynamicFlags.DragDropLeaves))
        {
            AsDragDropSource(node);
            AsDragDropTarget(node);
        }
    }

    #endregion Leaf Control

    #region Incoming Leaf
    private void DrawIncomingInner(IDynamicLeaf<RequestEntry> leaf, Vector2 region, DynamicFlags flags, bool responding)
    {
        // We can customize the displays here, but for now just give the name or whatever should be printed out.
        ImUtf8.SameLineInner();
        var posX = ImGui.GetCursorPosX();
        using (ImRaii.PushFont(UiBuilder.MonoFont))
            CkGui.TextFrameAlignedInline(leaf.Data.SenderAnonName);

        // store the cursorX
        var rightX = DrawIncomingRightInfo(leaf, flags);

        ImGui.SameLine(posX);
        ImGui.InvisibleButton($"request_{leaf.FullPath}", new Vector2(rightX - posX, ImUtf8.FrameHeight));
        HandleInteraction(leaf, flags);
        CkGui.AttachToolTip(ToolTip, ImGuiColors.DalamudOrange);

        // Below the top row, draw responder if responding.
        if (responding)
            DrawSingleResponder(leaf, region.X, flags);
    }

    private float DrawIncomingRightInfo(IDynamicLeaf<RequestEntry> leaf, DynamicFlags flags)
    {
        var timeLeftText = $"{leaf.Data.TimeToRespond.Days}d {leaf.Data.TimeToRespond.Hours}h {leaf.Data.TimeToRespond.Minutes}m";
        var iconSize = ImUtf8.FrameHeight;
        var timeLeft = ImGui.CalcTextSize(timeLeftText).X;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - iconSize;

        ImGui.SameLine(currentRightSide);
        CkGui.FramedHoverIconText(FAI.InfoCircle, ImGuiColors.TankBlue.ToUint());
        ShowRequestDetails(leaf.Data);

        currentRightSide -= timeLeft;
        ImGui.SameLine(currentRightSide);
        CkGui.ColorTextFrameAligned(timeLeftText, ImGuiColors.DalamudViolet);
        CkGui.AttachToolTip("Time left to respond to this request.");
        return currentRightSide;
    }

    private void ShowRequestDetails(RequestEntry request)
    {
        if (!ImGui.IsItemHovered())
            return;

        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One * 6f)
            .Push(ImGuiStyleVar.WindowRounding, 4f)
            .Push(ImGuiStyleVar.PopupBorderSize, 1f);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.ParsedPink);
        using var _ = ImRaii.Tooltip();
        // Can add later requested nick here.
        CkGui.ColorText("Is Temporary:", ImGuiColors.ParsedGold);
        CkGui.BooleanToColoredIcon(request.IsTemporaryRequest, true);
        // Include message.
        if (request.AttachedMessage.Length > 0)
        {
            CkGui.ColorText("Attached Message:", ImGuiColors.ParsedGold);
            CkGui.TextWrapped(request.AttachedMessage);
        }
    }

    private void DrawSingleResponder(IDynamicLeaf<RequestEntry> leaf, float width, DynamicFlags flags)
    {
        // Draw the area for single responding to a request.
        CkGui.ColorText("Include selector for groups, and an accept/reject button.", ImGuiColors.ParsedGold);
        // Dummy placeholders.
        if (CkGui.IconTextButton(FAI.PersonCircleCheck, "Accept", null, true, UiService.DisableUI))
            AcceptRequest(leaf.Data);
        CkGui.AttachToolTip("Accept this sundesmo request.");

        ImGui.SameLine();
        if (CkGui.IconTextButton(FAI.PersonCircleXmark, "Reject", null, true, UiService.DisableUI))
            RejectRequest(leaf.Data);
    }

    #endregion Incoming Leaf

    #region Outgoing Leaf
    private void DrawOutgoingInner(IDynamicLeaf<RequestEntry> leaf, Vector2 region, DynamicFlags flags)
    {
        var timeLeftText = $"{leaf.Data.TimeToRespond.Days}d {leaf.Data.TimeToRespond.Hours}h {leaf.Data.TimeToRespond.Minutes}m";
        
        // Start the line, then store the pos.
        ImUtf8.SameLineInner();
        var posX = ImGui.GetCursorPosX();
        // Show name.
        using (ImRaii.PushFont(UiBuilder.MonoFont))
            CkGui.TextFrameAlignedInline(leaf.Data.RecipientAnonName);
        // Calculate the right area.
        ImGui.SameLine(posX);
        var rightW = ImUtf8.FrameHeight * 2 + ImGui.CalcTextSize(timeLeftText).X;
        ImGui.InvisibleButton($"req_{leaf.FullPath}", new Vector2(region.X - rightW, ImUtf8.FrameHeight));
        HandleInteraction(leaf, flags);
        
        CkGui.ColorTextFrameAlignedInline(timeLeftText, ImGuiColors.ParsedGold);
        CkGui.AttachToolTip("Time left until this request expires.");

        // Draw the right buttons.
        ImGui.SameLine(0, 0);
        CkGui.FramedHoverIconText(FAI.InfoCircle, ImGuiColors.TankBlue.ToUint());
        // Might need to make a more detailed view if this doesnt work out, which i dont think it will overtime. (TEMP)
        CkGui.AttachToolTip($"--COL--[Requested Nickname]:--COL-- <UNKNOWN>" +
            $"--NL----COL--[Message]:--COL----NL--{leaf.Data.AttachedMessage}", ImGuiColors.DalamudOrange);

        ImGui.SameLine(0, 0);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
            if (CkGui.IconButton(FAI.Cross, null, $"{leaf.FullPath}_cancel", UiService.DisableUI))
                CancelRequest(leaf.Data);
        CkGui.AttachToolTip("Cancel this sundesmo request.");

    }
    #endregion Outgoing Leaf

    #region Utility
    private void DrawBulkSelector(float width)
    {
        // Generic config options for how to reply to a bulk selection.
        // Also should only draw for incoming requests.
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("BulkReqArea", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        CkGui.FramedIconText(FAI.ObjectGroup);
        CkGui.TextFrameAlignedInline("Bulk Selector Area");
    }

    private void DrawConfig(float width)
    {
        if (ViewingIncoming)
            DrawIncomingConfig(width);
        else
            DrawPendingConfig(width);
    }

    private void DrawIncomingConfig(float width)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("IncReqConfig", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        // Maybe move the vars into a config so we can store them between plugin states.
        CkGui.FramedIconText(FAI.PeopleGroup);
        CkGui.TextFrameAlignedInline("Dummy Text");
    }

    private void DrawPendingConfig(float width)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("IncReqConfig", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        CkGui.FramedIconText(FAI.Question);
        CkGui.TextFrameAlignedInline("Do we even need this? Or could it all be one??");
    }

    // Accepts a single request.
    private void AcceptRequest(RequestEntry request)
    {
        UiService.SetUITask(async () =>
        {
            // Wait for the response.
            var res = await _hub.UserAcceptRequest(new(new(request.SenderUID))).ConfigureAwait(false);
            
            // If already paired, we should remove the request from the manager.
            if (res.ErrorCode is SundouleiaApiEc.AlreadyPaired)
                _manager.RemoveRequest(request);
            // Otherwise, if successful, proceed with pairing operations.
            else if (res.ErrorCode is SundouleiaApiEc.Success)
            {
                // Remove the request from the manager.
                _manager.RemoveRequest(request);
                // Add the Sundesmo to the SundesmoManager.
                _sundesmos.AddSundesmo(res.Value!.Pair);
                // If they are online, mark them online.
                if (res.Value!.OnlineInfo is { } onlineSundesmo)
                    _sundesmos.MarkSundesmoOnline(onlineSundesmo);
                
                // TODO: Add them to the groups we wanted to add them to.
                // TODO: Set their nick to the desired nick.
            }
        });
    }

    private void AcceptRequests(IEnumerable<RequestEntry> requests)
    {
        // Process the TO BE ADDED Bulk accept server call, then handle responses accordingly.

        // For now, do nothing.
    }

    private void RejectRequest(RequestEntry request)
    {
        UiService.SetUITask(async () =>
        {
            if (await _hub.UserRejectRequest(new(new(request.RecipientUID))) is { } res && res.ErrorCode is SundouleiaApiEc.Success)
                _manager.RemoveRequest(request);
        });
    }

    private void RejectRequests(IEnumerable<RequestEntry> requests)
    {
        // Process the TO BE ADDED Bulk reject server call, then handle responses accordingly.
        // For now, do nothing.
    }

    private void CancelRequest(RequestEntry request)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserCancelRequest(new(new(request.RecipientUID)));
            if (res.ErrorCode is SundouleiaApiEc.Success)
                _manager.RemoveRequest(request);
        });
    }

    private void CancelRequests(IEnumerable<RequestEntry> requests)
    {
        // Process the TO BE ADDED Bulk cancel server call, then handle responses accordingly.
        // For now, do nothing.
    }
    #endregion Utility
}

