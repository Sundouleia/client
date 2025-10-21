using CkCommons.Gui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using Dalamud.Bindings.ImGui;
using OtterGui.Text;
using CkCommons.Raii;

namespace Sundouleia.Gui.Components;
public class DrawUserPair
{
    protected readonly SundouleiaMediator _mediator;
    protected readonly MainHub _hub;
    protected readonly IdDisplayHandler _nameHandler;

    protected Sundesmo _pair;
    private readonly string _id;
    private bool _hovered = false;
    public DrawUserPair(string id, Sundesmo entry, SundouleiaMediator mediator, MainHub hub, IdDisplayHandler nameDisp)
    {
        _id = id;
        _pair = entry;
        _mediator = mediator;
        _hub = hub;
        _nameHandler = nameDisp;
    }

    public Sundesmo Pair => _pair;

    public bool DrawPairedClient()
    {
        var selected = false;
        // get the current cursor pos
        var cursorPos = ImGui.GetCursorPosX();
        using var id = ImRaii.PushId(GetType() + _id);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        using (CkRaii.Child(GetType() + _id, childSize, _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 0f))
        {
            ImUtf8.SameLineInner();
            DrawLeftSide();
            ImGui.SameLine();
            var posX = ImGui.GetCursorPosX();
            var rightSide = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - CkGui.IconButtonSize(FAI.EllipsisV).X;
            rightSide = DrawRightSide();
            selected = DrawName(posX, rightSide);            
        }
        _hovered = ImGui.IsItemHovered();
        // if they were a supporter, go back to the start and draw the image.
        if (_pair.UserData.Tier is not CkVanityTier.NoRole)
            DrawSupporterIcon(cursorPos);

        return selected;
    }

    private void DrawSupporterIcon(float cursorPos)
    {
        var Image = CosmeticService.GetSupporterInfo(Pair.UserData);
        if (Image.SupporterWrap is { } wrap)
        {
            ImGui.SameLine(cursorPos);
            ImGui.SetCursorPosX(cursorPos - CkGui.IconSize(FAI.EllipsisV).X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.Image(wrap.Handle, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
            CkGui.AttachToolTip(Image.Tooltip);
        }
        // return to the end of the line.
    }

    private void DrawLeftSide()
    {
        var userPairText = string.Empty;
        ImGui.AlignTextToFramePadding();
        if (!_pair.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            CkGui.IconText(FAI.User);
            userPairText = $"{_pair.GetNickAliasOrUid()} is offline";
        }
        else if (_pair.IsRendered)
        {
            CkGui.IconText(FAI.Eye, ImGuiColors.ParsedGreen);
            userPairText = $"{_pair.GetNickAliasOrUid()} is visible ({_pair.PlayerName})--SEP--Click to target this player";
            if (ImGui.IsItemClicked())
                _mediator.Publish(new TargetSundesmoMessage(_pair));
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            CkGui.IconText(FAI.User);
            userPairText = $"{_pair.GetNickAliasOrUid()} is online";
        }
        CkGui.AttachToolTip(userPairText);

        ImGui.SameLine();
    }

    private bool DrawName(float leftSide, float rightSide)
        => _nameHandler.DrawPairText(_id, _pair, leftSide, () => rightSide - leftSide);

    private float DrawRightSide()
    {
        var permissionsButtonSize = CkGui.IconButtonSize(FAI.Cog);
        var barButtonSize = CkGui.IconButtonSize(FAI.EllipsisV);
        var spacingX = ImGui.GetStyle().ItemSpacing.X / 2;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - barButtonSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.EllipsisV))
            _mediator.Publish(new TogglePermissionWindow(_pair));

        // currentRightSide -= permissionsButtonSize.X + spacingX;
        // could draw here if they are a favorite or something idk
        // ImGui.SameLine(currentRightSide);
        return currentRightSide;
    }
}
