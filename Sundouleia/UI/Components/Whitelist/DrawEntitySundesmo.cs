using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Handlers;
using Sundouleia.Gui.MainWindow;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;

namespace Sundouleia.Gui.Components;
public class DrawEntitySundesmo
{
    private readonly SundouleiaMediator _mediator;
    private readonly FavoritesConfig _favorites;
    private readonly InteractionsHandler _interactions;
    private readonly IdDisplayHandler _nameHandler;

    /// <summary>
    ///     Identifier for this draw entity. Usually the folder + uid.
    /// </summary>
    private readonly string _id;
    private bool _hovered = false;
    private Sundesmo _sundesmo;
    public DrawEntitySundesmo(string id, Sundesmo sundesmo, SundouleiaMediator mediator, 
        FavoritesConfig favorites, InteractionsHandler interactions, IdDisplayHandler nameDisp)
    {
        _id = id;
        _sundesmo = sundesmo;
        _mediator = mediator;
        _favorites = favorites;
        _interactions = interactions;
        _nameHandler = nameDisp;
    }

    public Sundesmo Sundesmo => _sundesmo;

    public bool DrawListItem()
    {
        var selected = false;
        // get the current cursor pos
        var cursorPos = ImGui.GetCursorPosX();
        using var id = ImRaii.PushId(GetType() + _id);
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        using (CkRaii.Child(GetType() + _id, childSize, _hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 5f))
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
        if (_sundesmo.UserData.Tier is not CkVanityTier.NoRole)
            DrawSupporterIcon(cursorPos);

        return selected;
    }

    private void DrawSupporterIcon(float cursorPos)
    {
        var Image = CosmeticService.GetSupporterInfo(_sundesmo.UserData);
        if (Image.SupporterWrap is { } wrap)
        {
            ImGui.SameLine(cursorPos);
            ImGui.SetCursorPosX(cursorPos - ImUtf8.FrameHeight - ImUtf8.ItemInnerSpacing.X);
            ImGui.Image(wrap.Handle, new Vector2(ImUtf8.FrameHeight));
            CkGui.AttachToolTip(Image.Tooltip);
        }
    }

    private void DrawLeftSide()
    {
        var userPairText = string.Empty;
        ImGui.AlignTextToFramePadding();
        if (!_sundesmo.IsOnline)
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            CkGui.IconText(FAI.User);
            userPairText = $"{_sundesmo.GetNickAliasOrUid()} is offline";
        }
        else if (_sundesmo.IsRendered)
        {
            CkGui.IconText(FAI.Eye, ImGuiColors.ParsedGreen);
            userPairText = $"{_sundesmo.GetNickAliasOrUid()} is visible ({_sundesmo.PlayerName})--SEP--Click to target this player";
            if (ImGui.IsItemClicked())
                _mediator.Publish(new TargetSundesmoMessage(_sundesmo));
        }
        else
        {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
            CkGui.IconText(FAI.User);
            userPairText = $"{_sundesmo.GetNickAliasOrUid()} is online";
        }
        CkGui.AttachToolTip(userPairText);

        ImGui.SameLine();
    }

    private bool DrawName(float leftSide, float rightSide)
        => _nameHandler.DrawPairText(_id, _sundesmo, leftSide, () => rightSide - leftSide);

    private float DrawRightSide()
    {
        var interactionsSize = CkGui.IconButtonSize(FAI.ChevronRight);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - interactionsSize.X;

        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        if (CkGui.IconButton(FAI.ChevronRight, inPopup: true))
            _interactions.OpenSundesmoInteractions(_sundesmo);

        currentRightSide -= interactionsSize.X;
        ImGui.SameLine(currentRightSide);
        ImGui.AlignTextToFramePadding();
        SundouleiaEx.DrawFavoriteStar(_favorites, _sundesmo.UserData.UID, true);

        _interactions.DrawIfOpen(_sundesmo);

        return currentRightSide;
    }
}
