using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.DrawSystem;

// Drawer specifically for handling incoming sundesmo requests.
// Holds its own selection cache and individual reply variables from pending requests.
// This allows to cache reply progress when switching between the two.
public class RequestsInDrawer : DynamicDrawer<RequestEntry>
{
    private static readonly string ToolTip =
        "--COL--[L-CLICK]--COL-- Single-Select for bulk responding." +
  "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all between current & last selected ";

    private readonly MainHub _hub;
    private readonly FolderConfig _config;
    private readonly RequestsManager _manager;
    private readonly SundesmoManager _sundesmos;
    private readonly SidePanelService _sidePanel;

    private RequestCache _cache => (RequestCache)FilterCache;

    private IDynamicNode? _hoveredReplyNode;    // From last frame.
    private IDynamicNode? _newHoveredReplyNode; // Tracked each frame.
    private DateTime? _hoverExpiry;             // time until we should hide the hovered reply node.

    public RequestsInDrawer(ILogger<RadarDrawer> logger, MainHub hub, FolderConfig folders, 
        RequestsManager manager, SundesmoManager sundesmos, SidePanelService sidePanel,
        RequestsDrawSystem ds) 
        : base("##RequestsDrawer", Svc.Logger.Logger, ds, new RequestCache(ds))
    {
        _hub = hub;
        _config = folders;
        _manager = manager;
        _sundesmos = sundesmos;
        _sidePanel = sidePanel;
    }

    #region Search
    // Special top area here due to how it displays either essential config or bulk selection options.
    protected override void DrawSearchBar(float width, int length)
    {
        // Update the side panel if currently set to none, but drawing incoming.
        if (_sidePanel.DisplayMode is not SidePanelMode.IncomingRequests)
            _sidePanel.ForRequests(_cache, Selector);

        var tmp = FilterCache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Wrench).X + CkGui.IconTextButtonSize(FAI.Envelope, "Incoming");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            FilterCache.Filter = tmp;
        
        // Draw the config if it is opened.
        if (_cache.FilterConfigOpen)
            DrawConfig(width);

        void DrawButtons()
        {
            // For swapping which drawer is displayed. (Should also swap what is present in the service if multi-selecting.
            if (CkGui.IconTextButton(FAI.Envelope, "Incoming", null, true, _cache.FilterConfigOpen))
            {
                _config.Current.ViewingIncoming = !_config.Current.ViewingIncoming;
                _config.Save();
                _sidePanel.ClearDisplay();
            }
            CkGui.AttachToolTip($"Switch to outgoing requests.");

            ImGui.SameLine(0, 0);
            if (CkGui.IconButton(FAI.Wrench, inPopup: !_cache.FilterConfigOpen))
                _cache.FilterConfigOpen = !_cache.FilterConfigOpen;
            CkGui.AttachToolTip("Configure preferences for requests handling.");
        }
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_cache.FilterConfigOpen)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }
    #endregion Search

    protected override void UpdateHoverNode()
    {
        // if we are hovering something new, accept immidiately
        if (_newHoveredReplyNode != null)
        {
            _hoveredReplyNode = _newHoveredReplyNode;
            _hoverExpiry = null;
        }
        else if (_hoveredReplyNode != null)
        {
            _hoverExpiry ??= DateTime.Now.AddMilliseconds(350);
            // Check expiry every frame while still hovered, then gracefully clear.
            if (DateTime.Now >= _hoverExpiry)
            {
                _hoveredReplyNode = null;
                _hoverExpiry = null;
            }
        }

        _newHoveredReplyNode = null;
        base.UpdateHoverNode();
    }

    #region Custom Calls
    // Custom draw method to display the list of selected requests, allowing for them to be removed.
    public void DrawSelectedRequests(float width, DynamicFlags flags = DynamicFlags.None)
    {
        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var minusX = CkGui.IconButtonSize(FAI.Minus).X;
        // Perform the following for each row.
        foreach (var leaf in Selector.Leaves.ToList())
        {
            DrawLeftSide(leaf.Data, flags);
            ImUtf8.SameLineInner();

            // Store the pos at the point we draw out the name area.
            using (ImRaii.PushFont(UiBuilder.MonoFont))
                CkGui.TextFrameAligned(leaf.Data.SenderAnonName);

            if (leaf.Data.IsTemporaryRequest)
            {
                ImGui.SameLine();
                CkGui.IconTextAligned(FAI.Stopwatch, ImGuiColors.DalamudGrey2);
                CkGui.AttachToolTip("A temporary pairing, that expires unless you make it permanent.");
            }

            ImGui.SameLine(endX - minusX);
            if (CkGui.IconButton(FAI.Minus, null, leaf.Name, UiService.DisableUI, true))
                Selector.Deselect(leaf);
            CkGui.AttachToolTip("Remove from selection.");
        }
    }


    // Custom draw method spesifically for our incoming folder.
    public void DrawRequests(float width, DynamicFlags flags = DynamicFlags.None)
    {
        // Obtain the folder first before handling the draw logic.
        if (!DrawSystem.FolderMap.TryGetValue(Constants.FolderTagRequestInc, out var folder))
            return;

        // Ensure the child is at least draw to satisfy the expected drawn content region.
        using var _ = ImRaii.Child(Label, new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_) return;

        // Handle any main context interactions such as right-click menus and the like.
        HandleMainContext();
        // Update the cache to its latest state.
        FilterCache.UpdateCache();

        if (!FilterCache.CacheMap.TryGetValue(folder, out var cachedNode))
            return;

        if (cachedNode is not DynamicFolderCache<RequestEntry> incRequests)
            return;

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
            .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        // Do not include any indentation.
        DrawIncomingRequests(incRequests, flags);
        PostDraw();
    }

    // We dont need to overprotect against ourselves when we know what we're drawing.
    // The only thing that should ever be drawn here is the incoming folder.
    // As such, create our own override for this drawer. 
    private void DrawIncomingRequests(DynamicFolderCache<RequestEntry> cf, DynamicFlags flags)
    {
        using var id = ImRaii.PushId($"DDS_{Label}_{cf.Folder.ID}");

        DrawFolderBanner(cf.Folder, flags);
        // The below, the request entries.
        DrawFolderLeaves(cf, flags);
    }

    #endregion Custom Calls

    #region Custom Sub-Calls
    private void DrawFolderBanner(IDynamicFolder<RequestEntry> f, DynamicFlags flags)
    {
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"df_{Label}_{f.ID}", width, ImUtf8.FrameHeight, f.BgColor, f.BorderColor, 5f, 1f);

        // No interactions, keep open.
        ImUtf8.SameLineInner();
        CkGui.IconTextAligned(f.Icon, f.IconColor);
        CkGui.ColorTextFrameAlignedInline(f.Name, f.NameColor);
        CkGui.ColorTextFrameAlignedInline($"[{f.TotalChildren}]", ImGuiColors.DalamudGrey2);

        DrawFolderButtons((RequestFolder)f);
    }


    private float DrawFolderButtons(RequestFolder folder)
    {
        var byWorldSize = CkGui.IconTextButtonSize(FAI.Globe, "In World");
        var byAreaSize = CkGui.IconTextButtonSize(FAI.Map, "In Area");
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - byWorldSize;

        ImGui.SameLine(currentRightSide);
        if (CkGui.IconTextButton(FAI.Globe, "In World", null, true, UiService.DisableUI))
        {
            // Try and add to the selection all leaves within the folder that are from the current world.
            var curWorld = PlayerData.CurrentWorldId;
            var toAdd = ((IDynamicFolder<RequestEntry>)folder).Children.Where(x => x.Data.SentFromWorld(curWorld));
            Selector.SelectMultiple(toAdd);
        }
        CkGui.AttachToolTip("Select requests sent from your current world.");

        currentRightSide -= byAreaSize;
        ImGui.SameLine(currentRightSide);
        if (CkGui.IconTextButton(FAI.Map, "In Area", null, true, UiService.DisableUI))
        {
            // Try and add to the selection all leaves within the folder that are from the current area.
            var curWorld = PlayerData.CurrentWorldId;
            var curTerritory = PlayerContent.TerritoryID;
            var toAdd = ((IDynamicFolder<RequestEntry>)folder).Children.Where(x => x.Data.SentFromCurrentArea(curWorld, curTerritory));
            Selector.SelectMultiple(toAdd);
        }
        CkGui.AttachToolTip("Select requests sent from your current area.");

        return currentRightSide;
    }
    #endregion Custom Sub-Calls


    // Override each drawn leaf for its unique display in the request folder.
    protected override void DrawLeafInner(IDynamicLeaf<RequestEntry> leaf, Vector2 region, DynamicFlags flags)
    {
        DrawLeftSide(leaf.Data, flags);
        ImUtf8.SameLineInner();

        // Store the pos at the point we draw out the name area.
        var posX = ImGui.GetCursorPosX();
        // Draw out the responce area, and get where it ends.
        var rightSide = DrawRightSide(leaf, region.Y, flags);

        // Bounce back to the name area.
        ImGui.SameLine(posX);
        // Draw out the invisible button over the area to draw in.
        if (ImGui.InvisibleButton($"{leaf.FullPath}-hoverspace", new Vector2(rightSide - posX, region.Y)))
            HandleLeftClick(leaf, flags);
        HandleDetections(leaf, flags);
        CkGui.AttachToolTip(ToolTip, ImGuiColors.DalamudOrange);

        // Bounce back and draw out the name.
        ImGui.SameLine(posX);
        using var _ = ImRaii.PushFont(UiBuilder.MonoFont);
        CkGui.TextFrameAligned(leaf.Data.SenderAnonName);

        if (leaf.Data.IsTemporaryRequest)
        {
            ImGui.SameLine();
            CkGui.IconTextAligned(FAI.Stopwatch, ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip("A temporary pairing, that expires unless you make it permanent.");
        }
    }

    private void DrawLeftSide(RequestEntry entry, DynamicFlags flags)
    {
        // If there was an attached message then we should show it.
        if (entry.HasMessage)
            CkGui.FramedHoverIconText(FAI.CommentDots, ImGuiColors.TankBlue.ToUint());
        else
            CkGui.FramedIconText(FAI.CommentDots, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        CkGui.AttachToolTip($"--COL--Attached Message:--COL----SEP--{entry.Message}", !entry.HasMessage, ImGuiColors.ParsedGold);
    }

    // Draw out the responder entry.
    private float DrawRightSide(IDynamicLeaf<RequestEntry> leaf, float height, DynamicFlags flags)
    {
        // Get if this leaf if currently in a responding state.
        var replying = _hoveredReplyNode == leaf;

        // Grab the end of the selectable region.
        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var timeTxt = leaf.Data.GetRemainingTimeString();
        var buttonSize = CkGui.IconButtonSize(FAI.Times).X;
        var timeTxtWidth = ImGui.CalcTextSize(timeTxt).X;
        var spacing = ImUtf8.ItemInnerSpacing.X;

        var childWidth = replying
            ? buttonSize + (buttonSize + spacing) * 2
            : buttonSize;

        endX -= childWidth;
        ImGui.SameLine(endX);
        using (CkRaii.Child("reply", new Vector2(childWidth, height), ImGui.GetColorU32(ImGuiCol.FrameBg), 12f))
        {
            if (replying)
            {
                using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f))
                {
                    // Draw out the initial frame with a small outer boarder.
                    if (CkGui.IconButtonColored(FAI.Check, CkColor.TriStateCheck.Uint(), UiService.DisableUI))
                        AcceptRequest(leaf.Data);
                    CkGui.AttachToolTip("Accept this request.");
                    ImUtf8.SameLineInner();
                    if (CkGui.IconButtonColored(FAI.Times, CkColor.TriStateCross.Uint(), UiService.DisableUI))
                        RejectRequest(leaf.Data);
                    CkGui.AttachToolTip("Reject this request.");
                    ImUtf8.SameLineInner();
                }
            }

            CkGui.FramedHoverIconText(FAI.Reply, uint.MaxValue);
            CkGui.AttachToolTip("Open Quick-Responder");
        }
        // Should be if we hover anywhere in the area.
        if (ImGui.IsItemHovered())
            _newHoveredReplyNode = leaf;

        // Now the time.
        if (!replying)
        {
            endX -= (timeTxtWidth + spacing);
            ImGui.SameLine(endX);
            CkGui.ColorTextFrameAligned(timeTxt, ImGuiColors.ParsedGrey);
            CkGui.AttachToolTip("Time left to respond to this request.");
        }

        return endX;
    }


    private void DrawConfig(float width)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("IncReqConfig", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        // Maybe move the vars into a config so we can store them between plugin states.
        CkGui.FramedIconText(FAI.PeopleGroup);
        CkGui.TextFrameAlignedInline("Dummy Text");
    }

    // Accepts a single request.
    private void AcceptRequest(RequestEntry request)
    {
        UiService.SetUITask(async () =>
        {
            // Wait for the response.
            Log.Information($"Accepting request from {request.SenderAnonName} ({request.SenderUID})");
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
            else
            {
                Log.Warning($"Failed to accept request from {request.SenderAnonName} ({request.SenderUID}): {res.ErrorCode}");
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
            var res = await _hub.UserRejectRequest(new(new(request.RecipientUID))).ConfigureAwait(false);
            if (res.ErrorCode is SundouleiaApiEc.Success)
                _manager.RemoveRequest(request);
            else
            {
                Log.Warning($"Failed to reject request to {request.RecipientAnonName} ({request.RecipientUID}): {res.ErrorCode}");
            }
        });
    }

    private void RejectRequests(IEnumerable<RequestEntry> requests)
    {
        // Process the TO BE ADDED Bulk reject server call, then handle responses accordingly.
        // For now, do nothing.
    }
}

