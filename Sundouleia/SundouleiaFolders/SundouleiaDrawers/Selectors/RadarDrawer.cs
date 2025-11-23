using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui.Text;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;

namespace Sundouleia.DrawSystem;

public class RadarDrawer : DynamicDrawer<RadarUser>
{
    // Not sure how we will define where the quick-send stuff goes, but we'll figure it out overtime.
    private static readonly string TooltipText =
        "--COL--[L-CLICK]--COL-- Open/Close Request Drafter" +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Quick-Send Request.";

    private bool _configExpanded = false;

    private readonly MainHub _hub;
    private readonly RadarManager _manager;
    private readonly GroupsManager _groups; // For group selection.
    private readonly SundesmoManager _sundesmos; // Know if they are a pair or not.
    private readonly RequestsManager _requests; // Know if they are pending request or not.

    // We want to make sending requests tedious as a group grows, but not too tedious.
    // Because of this we want to ensure that some defaults can be set so things do not
    // need to be assigned all the time.

    // No variable for it, but include a button for 'Create Group for Zone'
    // This should be stored in a config somewhere I think.
    private List<SundesmoGroup> _defaultGroups = []; // Automatically place requests sent with defaults into these when accepted.
    private bool _defaultIsTemporary = true; // the attached message in the request.

    // Private cache states.
    private IDynamicNode? _inDrafter; // The node expanded for request drafting.
    private List<string> _groupsToJoinOnAccept = [];
    private string _requestMsg = string.Empty;
    private bool _asTemporary;

    public RadarDrawer(ILogger<RadarDrawer> logger, MainHub hub, RadarManager manager,
        GroupsManager groups, SundesmoManager sundesmos, RequestsManager requests, RadarDrawSystem ds)
        : base("##RadarDrawer", logger, ds)
    {
        _hub = hub;
        _manager = manager;
        _groups = groups;
        _sundesmos = sundesmos;
        _requests = requests;
    }

    #region Search
    protected override void DrawSearchBar(float width, int length)
    {
        var tmp = Filter;
        var buttonsWidth = CkGui.IconButtonSize(FAI.Cog).X;
        // Update the search bar if things change, like normal.
        if (FancySearchBar.Draw("Filter", width, ref tmp, "filter..", length, buttonsWidth, DrawButtons))
        {
            if (!string.Equals(tmp, Filter, StringComparison.Ordinal))
                Filter = tmp; // Auto-Marks as dirty.
        }

        // If the config is expanded, draw that.
        if (_configExpanded)
            DrawRadarConfig(width);
    }

    // Draws the grey line around the filtered content when expanded and stuff.
    protected override void PostSearchBar()
    {
        if (_configExpanded)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Button), 5f);
    }

    private void DrawButtons()
    {
        if (CkGui.IconButton(FAI.Cog, inPopup: !_configExpanded))
            _configExpanded = !_configExpanded;
        CkGui.AttachToolTip("Configure radar defaults.");
    }
    #endregion Search    

    // Override to check for a match based on the current leaf's data.
    protected override bool IsVisible(IDynamicNode<RadarUser> node)
    {
        // Save on extra work by just returning true if nothing is in the filter.
        if (Filter.Length is 0)
            return true;

        // Check leaves for the sundesmo display name with all possible displays.
        if (node is DynamicLeaf<RadarUser> leaf)
            return leaf.Data.MatchesFilter(Filter);
        // Otherwise just check the base.
        return base.IsVisible(node);
    }

    protected override void DrawFolderBannerInner(IDynamicFolder<RadarUser> folder, Vector2 region, DynamicFlags flags)
        => DrawFolderInner((RadarFolder)folder, region, flags);

    private void DrawFolderInner(RadarFolder folder, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"folder_{folder.ID}", region);
        HandleInteraction(folder, flags);

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

    #region Leaf
    // This override intentionally prevents the inner method from being called so that we can call our own inner method.
    protected override void DrawLeaf(IDynamicLeaf<RadarUser> leaf, DynamicFlags flags, bool selected)
    {
        var drafting = _inDrafter == leaf;
        var height = drafting ? CkStyle.GetFrameRowsHeight(3) : ImUtf8.FrameHeight;
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), height);
        var bgCol = (!drafting && selected) ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;
        var frameCol = drafting ? ImGui.GetColorU32(ImGuiCol.Button) : 0;
        using (var _ = CkRaii.FramedChild(Label + leaf.Name, size, bgCol, frameCol, 5f, 1f))
            DrawLeafInner(leaf, _.InnerRegion, flags, drafting);
    }

    // Inner leaf called by the above drawfunction, serving as a replacement for the default DrawLeafInner.
    private void DrawLeafInner(IDynamicLeaf<RadarUser> leaf, Vector2 region, DynamicFlags flags, bool drafting)
    {
        DrawTopRow(leaf, region.X, flags);
        if (drafting)
            DrawDrafter(leaf, region.X, flags);
    }

    private void DrawTopRow(IDynamicLeaf<RadarUser> leaf, float width, DynamicFlags flags)
    {
        bool blockDraft = leaf.Data.IsPair || _requests.Outgoing.Any(r => r.RecipientUID == leaf.Data.UID);
        ImUtf8.SameLineInner();
        bool examining = DrawLeftSide(leaf.Data);
        ImGui.SameLine();

        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"node_{leaf.FullPath}", new(width - pos.X, ImUtf8.FrameHeight));
        HandleInteraction(leaf, flags);
        if (!blockDraft)
            HandleDraftInteraction(leaf);
        CkGui.AttachToolTip(TooltipText, blockDraft, ImGuiColors.DalamudOrange);

        // Go back and draw the name.
        ImGui.SameLine(pos.X);
        if (_sundesmos.GetUserOrDefault(new(leaf.Data.UID)) is { } match)
        {
            CkGui.TextFrameAligned(match.GetNickAliasOrUid());
            CkGui.ColorTextFrameAlignedInline($"({match.UserData.AnonTag})", ImGuiColors.DalamudGrey2);
        }
        else
            ImGui.Text(leaf.Data.AnonymousName);
    }

    private void HandleDraftInteraction(IDynamicLeaf<RadarUser> user)
    {
        // Only other thing we care about is what to do on selection, or shift selection.
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (ImGui.GetIO().KeyShift && clicked)
        {
            // Perform a quick-send request.
            SendRequest(user, _defaultGroups.Select(g => g.Label).ToList());
        }
        // Otherwise just set the drafter to this node and clear all drafter variables.
        else if (clicked)
        {
            if (_inDrafter == user)
            {
                _inDrafter = null;
            }
            else
            {
                _inDrafter = user;
                _groupsToJoinOnAccept = _defaultGroups.Select(g => g.Label).ToList();
                _asTemporary = _defaultIsTemporary;
                _requestMsg = string.Empty;
            }
        }
    }

    protected override void HandleInteraction(IDynamicLeaf<RadarUser> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;
        // Do not handle base interaction. (Yes, I know what im doing)
    }

    private void DrawDrafter(IDynamicLeaf<RadarUser> leaf, float width, DynamicFlags flags)
    {
        var sendRequestSize = CkGui.IconTextButtonSize(FAI.CloudUploadAlt, "Send");

        ImGui.SetNextItemWidth(width - sendRequestSize - ImUtf8.ItemInnerSpacing.X);
        ImGui.InputTextWithHint("##sendRequestMsg", "Attach Message (Optional)", ref _requestMsg, 100);
        ImUtf8.SameLineInner();
        if (CkGui.IconTextButton(FAI.CloudUploadAlt, "Send", disabled: UiService.DisableUI))
            SendRequest(leaf, _groupsToJoinOnAccept);
    }

    private unsafe bool DrawLeftSide(RadarUser user)
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

    private void SendRequest(IDynamicLeaf<RadarUser> node, List<string> preAddToGroups)
    {
        UiService.SetUITask(async () =>
        {
            var res = await _hub.UserSendRequest(new(new(node.Data.UID), _asTemporary, _requestMsg));
            if (res.ErrorCode is SundouleiaApiEc.Success && res.Value is { } sentRequest)
            {
                Svc.Logger.Information($"Successfully sent sundesmo request to {node.Data.AnonymousName}");
                _requests.AddNewRequest(sentRequest);
                _groupsToJoinOnAccept = _defaultGroups.Select(g => g.Label).ToList();
                _asTemporary = _defaultIsTemporary;
                _requestMsg = string.Empty;
                // premptively add this user to all of these groups as users, or place them in some hidden pending list.
                return;
            }
            // Notify failure.
            Svc.Logger.Warning($"Request to {node.Data.AnonymousName} failed with error code {res.ErrorCode}");
        });
    }
    #endregion Leaf

    #region Config
    private void DrawRadarConfig(float width)
    {
        var bgCol = _configExpanded ? ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.FrameBg), 0.4f) : 0;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImUtf8.ItemSpacing.Y);
        using var child = CkRaii.ChildPaddedW("RadarConfig", width, CkStyle.GetFrameRowsHeight(2), bgCol, 5f);

        // Maybe move the vars into a config so we can store them between plugin states.
        CkGui.FramedIconText(FAI.PeopleGroup);
        // Combo inline here holding the groups.
        CkGui.ColorTextFrameAlignedInline("[Dummy Combo Placeholder]", ImGuiColors.ParsedGrey);
        // Note that when the combo is dropped down, there are buttons to clear, search filter, and also to add a new one.
        // Selections also will not close the combo.
        // We could even make a smallbutton that shows "view current' or something, idk.
        ImUtf8.SameLineInner();
        CkGui.FramedHoverIconText(FAI.InfoCircle, ImGuiColors.TankBlue.ToUint());
        if (ImGui.IsItemHovered())
        {
            // Show the popup, at the location of the framed icon text to the right, displaying the groups that the request, when accepted, are added to.
        }

        // next row, checkbox.
        CkGui.FramedIconText(FAI.Stopwatch);
        ImUtf8.SameLineInner();
        if (ImGui.Checkbox("Requests Are Temporary", ref _defaultIsTemporary))
        {
            Log.LogInformation($"Default Temporary Request setting changed to [{_defaultIsTemporary}]");
        }
    }

    #endregion Config
}

