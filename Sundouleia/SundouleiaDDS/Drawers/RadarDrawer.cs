using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.DrawSystem;

public class RadarDrawer : DynamicDrawer<RadarUser>
{
    // Not sure how we will define where the quick-send stuff goes, but we'll figure it out overtime.
    private static readonly string AwaitingResponseTT =
        "An incoming or pending request with this user is in your inbox!";
    private static readonly string RequestableTT =
        "--COL--[L-CLICK]--COL-- Open/Close Request Drafter" +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Quick-Send Request.";


    private readonly MainHub _hub;
    private readonly FolderConfig _config;
    private readonly RadarManager _manager;
    private readonly GroupsManager _groups; // For group selection.
    private readonly SundesmoManager _sundesmos; // Know if they are a pair or not.
    private readonly RequestsManager _requests; // Know if they are pending request or not.

    private RadarCache _cache => (RadarCache)FilterCache;

    private string? _requestMsg = null;

    public RadarDrawer(MainHub hub, FolderConfig config, RadarManager manager,
        GroupsManager groups, SundesmoManager sundesmos, RequestsManager requests,
        RadarDrawSystem ds)
        : base("##RadarDrawer", Svc.Logger.Logger, ds, new RadarCache(ds))
    {
        _hub = hub;
        _config = config;
        _manager = manager;
        _groups = groups;
        _sundesmos = sundesmos;
        _requests = requests;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = FilterCache.Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Cog).X;
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
            FilterCache.Filter = tmp; // Auto-Marks as dirty.

        // If the config is expanded, draw that.
        if (_cache.FilterConfigOpen)
            DrawRadarConfig(width);
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_cache.FilterConfigOpen)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    private void DrawButtons()
    {
        if (CkGui.IconButton(FAI.Cog, inPopup: !_cache.FilterConfigOpen))
            _cache.FilterConfigOpen = !_cache.FilterConfigOpen;
        CkGui.AttachToolTip("Configure radar defaults.");
    }

    private void DrawRadarConfig(float width)
    {
        var bgCol = _cache.FilterConfigOpen ? ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f) : 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("RadarConfig", width, CkStyle.TwoRowHeight(), bgCol, 5f);

        var isTemp = _config.Current.RadarRequestsAreTemp;
        if (ImGui.Checkbox("Requests Are Temporary", ref isTemp))
        {
            _config.Current.RadarRequestsAreTemp = isTemp;
            _config.Save();
        }
        ImGui.SetNextItemWidth(child.InnerRegion.X);
        var defaultMsg = _config.Current.RadarDefaultMessage;
        if (ImGui.InputTextWithHint("##DefaultMsg", "Default Attached Message.", ref defaultMsg, 40))
        {
            _config.Current.RadarDefaultMessage = defaultMsg;
            _config.Save();
        }

    }
    #endregion Search

    protected override void DrawFolderBannerInner(IDynamicFolder<RadarUser> folder, Vector2 region, DynamicFlags flags)
        => DrawFolderInner((RadarFolder)folder, region, flags);

    private void DrawFolderInner(RadarFolder folder, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        if (ImGui.InvisibleButton($"folder_{folder.ID}", region))
            HandleLeftClick(folder, flags);
        HandleDetections(folder, flags);

        // Back to the start, then draw.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(folder.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(folder.Icon, folder.IconColor);
        CkGui.ColorTextFrameAlignedInline(folder.Name, folder.NameColor);
        // Total Context.
        CkGui.ColorTextFrameAlignedInline($"[{folder.TotalChildren}]", ImGuiColors.DalamudGrey2);
        CkGui.AttachToolTip($"{folder.TotalChildren} total. --COL--({folder.Lurkers} lurkers)--COL--", ImGuiColors.DalamudGrey2);
    }

    protected override void DrawLeaf(IDynamicLeaf<RadarUser> leaf, DynamicFlags flags, bool selected)
    {
        // Draw out the leaf, based on it's data type.
        if (leaf.Data.IsPaired)
            DrawPairedUser(leaf, flags, selected);
        else
            DrawUnpairedUser(leaf, flags, selected);
    }

    private void DrawPairedUser(IDynamicLeaf<RadarUser> leaf, DynamicFlags flags, bool selected)
    {
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        using var _ = CkRaii.FramedChild(Label + leaf.Name, size, bgCol, 0, 5f, 1f);
        
        // Draw the paired user row.
        ImUtf8.SameLineInner();
        DrawLeafIcon(leaf.Data);        
        ImGui.SameLine();

        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"node_{leaf.FullPath}", new(_.InnerRegion.X - pos.X, ImUtf8.FrameHeight));
        HandleDetections(leaf, flags);

        // Go back and draw the name.
        ImGui.SameLine(pos.X);
        CkGui.TextFrameAligned(leaf.Data.DisplayName);
        CkGui.ColorTextFrameAlignedInline($"({leaf.Data.AnonTag})", ImGuiColors.DalamudGrey2);
    }

    private void DrawUnpairedUser(IDynamicLeaf<RadarUser> leaf, DynamicFlags flags, bool selected)
    {
        bool drafting = _cache.NodeInDrafter == leaf;
        var height = drafting ? CkStyle.GetFrameRowsHeight(2) : ImUtf8.FrameHeight;
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), height);
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        var frameCol = drafting ? ImGui.GetColorU32(ImGuiCol.Button) : 0;

        using var _ = CkRaii.FramedChild(Label + leaf.Name, size, 0, frameCol, 5f, 1f);

        using (var selectable = CkRaii.Child(leaf.Name, new(_.InnerRegion.X, ImUtf8.FrameHeight), bgCol, 5f))
        {
            // Get the size of the right based on interaction.  
            ImUtf8.SameLineInner();
            DrawLeafIcon(leaf.Data);
            ImGui.SameLine();

            var pos = ImGui.GetCursorPos();
            if (ImGui.InvisibleButton($"node_{leaf.FullPath}", selectable.InnerRegion))
                HandleLeftClick(leaf, flags);
            HandleDetections(leaf, flags);
            // Attach a tooltip based on the node's state
            if (!leaf.Data.IsPaired)
                CkGui.AttachToolTip(leaf.Data.InRequests ? AwaitingResponseTT : RequestableTT, ImGuiColors.DalamudOrange);

            // Go back and draw the name.
            ImGui.SameLine(pos.X);
            CkGui.TextFrameAligned(leaf.Data.DisplayName);
        }

        // Draw the drafter afterwards.
        if (!drafting)
            return;

        var iconSize = CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send");
        var msg = _requestMsg ?? _config.Current.RadarDefaultMessage;
        ImGui.SetNextItemWidth(_.InnerRegion.X - iconSize - ImUtf8.ItemInnerSpacing.X);
        if (ImGui.InputTextWithHint("##sendRequestMsg", "Attach Message (Optional)", ref msg, 50))
            _requestMsg = msg;
        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.CloudUploadAlt, "Send", disabled: UiService.DisableUI))
            SendRequest(leaf);
        CkGui.AttachToolTip("Send a request with the attached message.");
    }

    // We only ever do this for the unpaired leaves so it's ok to handle that logic here.
    protected override void HandleLeftClick(IDynamicLeaf<RadarUser> node, DynamicFlags flags)
    {
        if (!node.Data.CanSendRequests || _requests.ExistsFor(node.Data.UID))
            return;

        // Send quick-request if shift is held.
        if (ImGui.GetIO().KeyShift && _cache.NodeInDrafter != node)
            SendRequest(node);
        // Otherwise just set the drafter to this node and clear all drafter variables.
        else
        {
            // If in drafter for this node, close it.
            if (_cache.NodeInDrafter == node)
                _cache.NodeInDrafter = null;
            // Otherwise, open it, but not if the node is in any requests.
            else
            {
                _cache.NodeInDrafter = node;
                _requestMsg = _config.Current.RadarDefaultMessage;
            }
        }
    }

    private unsafe bool DrawLeafIcon(RadarUser user)
    {
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            if (user.IsValid)
            {
                CkGui.IconText(FAI.Eye, ImGuiColors.ParsedGreen);
                CkGui.AttachToolTip($"Nearby and Rendered / Visible!");
#if DEBUG
                if (ImGui.IsItemHovered())
                {
                    TargetSystem.Instance()->FocusTarget = (GameObject*)user.Address;
                }
                else
                {
                    if (TargetSystem.Instance()->FocusTarget == (GameObject*)user.Address)
                        TargetSystem.Instance()->FocusTarget = null;
                }
#endif
            }
            else
            {
                CkGui.IconText(FAI.EyeSlash, ImGuiColors.DalamudRed);
                CkGui.AttachToolTip($"Not Rendered, or Requesting is disabled. --COL--(Lurker)--COL--", ImGuiColors.DalamudGrey2);
            }
        }
        return ImGui.IsItemHovered();
    }

    private void SendRequest(IDynamicLeaf<RadarUser> node)
    {
        UiService.SetUITask(async () =>
        {
            var attachedMsg = _requestMsg ?? _config.Current.RadarDefaultMessage;
            var details = new RequestDetails(_config.Current.RadarRequestsAreTemp, attachedMsg, LocationSvc.Current.WorldId, LocationSvc.Current.TerritoryId);
            var res = await _hub.UserSendRequest(new(new(node.Data.UID), details));
            if (res.ErrorCode is SundouleiaApiEc.Success && res.Value is { } sentRequest)
            {
                Log.Information($"Successfully sent sundesmo request to {node.Data.DisplayName}");
                _requests.AddNewRequest(sentRequest);
                _manager.RefreshUser(new(node.Data.UID));
                // Clear temp variables
                _requestMsg = string.Empty;
                _cache.NodeInDrafter = null;
                return;
            }
            // Notify failure.
            Log.Warning($"Request to {node.Data.DisplayName} failed with error code {res.ErrorCode}");
        });
    }
}

