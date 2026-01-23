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
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;

namespace Sundouleia.DrawSystem;


// Drawer specifically for handling incoming sundesmo requests.
// Holds its own selection cache and individual reply variables from pending requests.
// This allows to cache reply progress when switching between the two.
public class RequestsOutDrawer : DynamicDrawer<RequestEntry>
{
    private static readonly string ToolTip =
        "--COL--[L-CLICK]--COL-- Single-Select for bulk responding." +
  "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all between current & last selected ";

    private readonly MainHub _hub;
    private readonly FolderConfig _config; // Groups and defaults.
    private readonly RequestsManager _manager;
    private readonly SidePanelService _sidePanel;

    private RequestCache _cache => (RequestCache)FilterCache;

    public RequestsOutDrawer(MainHub hub, FolderConfig folders, RequestsManager manager, 
        SundesmoManager sundesmos, SidePanelService sidePanel,RequestsDrawSystem ds) 
        : base("##RequestsDrawer", Svc.Logger.Logger, ds, new RequestCache(ds))
    {
        _hub = hub;
        _config = folders;
        _manager = manager;
        _sidePanel = sidePanel;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = FilterCache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Wrench).X + CkGui.IconTextButtonSize(FAI.Stopwatch, "Outgoing");
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            FilterCache.Filter = tmp;

        // Draw the config if open.
        if (_cache.FilterConfigOpen)
            DrawConfig(width);

        void DrawButtons()
        {
            if (CkGui.IconTextButton(FAI.Stopwatch, "Outgoing", null, true, _cache.FilterConfigOpen))
            {
                _config.Current.ViewingIncoming = !_config.Current.ViewingIncoming;
                _config.Save();
            }
            CkGui.AttachToolTip($"Switch to incoming requests.");

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

    // Custom draw method spesifically for our incoming folder.
    public void DrawRequests(float width, DynamicFlags flags = DynamicFlags.None)
    {
        // Obtain the folder first before handling the draw logic.
        if (!DrawSystem.FolderMap.TryGetValue(Constants.FolderTagRequestPending, out var folder))
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

        if (cachedNode is not DynamicFolderCache<RequestEntry> outRequests)
            return;

        // Set the style for the draw logic.
        ImGui.SetScrollX(0);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One)
            .Push(ImGuiStyleVar.IndentSpacing, 14f * ImGuiHelpers.GlobalScale);

        // Do not include any indentation.
        DrawPendingRequests(outRequests, flags);
        PostDraw();
    }

    // We dont need to overprotect against ourselves when we know what we're drawing.
    // The only thing that should ever be drawn here is the pending folder.
    // As such, create our own override for this drawer. 
    private void DrawPendingRequests(DynamicFolderCache<RequestEntry> cf, DynamicFlags flags)
    {
        using var id = ImRaii.PushId($"DDS_{Label}_{cf.Folder.ID}");

        DrawFolderBanner(cf.Folder, flags);
        // The below, the request entries.
        DrawFolderLeaves(cf, flags);
    }

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

        if (Selector.Leaves.Count > 0 && f is RequestFolder folder)
            DrawFolderButtons(folder);
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
        CkGui.TextFrameAligned(leaf.Data.RecipientAnonName);
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
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12f);

        // Grab the end of the selectable region.
        var endX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var timeTxt = leaf.Data.GetRemainingTimeString();

        endX -= (ImGui.CalcTextSize(timeTxt).X + ImUtf8.ItemInnerSpacing.X + CkGui.IconButtonSize(FAI.Times).X);
        ImGui.SameLine(endX);
        // Display the time remaining.
        CkGui.ColorTextFrameAligned(timeTxt, ImGuiColors.ParsedGrey);
        CkGui.AttachToolTip("Time left until the request expires.");

        ImUtf8.SameLineInner();
        using (ImRaii.PushColor(ImGuiCol.Text, CkColor.TriStateCross.Uint()))
            if (CkGui.IconButton(FAI.Times, null, leaf.Name, UiService.DisableUI, true))
                CancelRequest(leaf.Data);
        CkGui.AttachToolTip("Cancel this pending request.");
        return endX;
    }

    private void DrawConfig(float width)
    {
        var bgCol = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("IncReqConfig", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        CkGui.FramedIconText(FAI.Question);
        CkGui.TextFrameAlignedInline("Do we even need this? Or could it all be one??");
    }

    private void CancelRequest(RequestEntry request)
    {
        UiService.SetUITask(async () =>
        {
            Log.Information($"Cancelling outgoing request to {request.RecipientAnonName} ({request.RecipientUID})");
            var res = await _hub.UserCancelRequest(new(new(request.RecipientUID)));
            if (res.ErrorCode is SundouleiaApiEc.Success)
                _manager.RemoveRequest(request);
            else
                Log.Warning($"Failed to cancel outgoing request to {request.RecipientAnonName} ({request.RecipientUID}): {res.ErrorCode}");
        });
    }

    private void CancelRequests(IEnumerable<RequestEntry> requests)
    {
        UiService.SetUITask(async () =>
        {
            Log.Information($"Bulk cancelling {requests.Count()} outgoing requests.");
            var res = await _hub.UserCancelRequests(new(requests.Select(x => new UserData(x.RecipientUID)).ToList()));
            if (res.ErrorCode is SundouleiaApiEc.Success)
                _manager.RemoveRequests(requests);
            else
                Log.Warning($"Failed to bulk cancel outgoing requests: {res.ErrorCode}");
        });
    }
}

