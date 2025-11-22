using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Handlers;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;

namespace Sundouleia.Gui.Components;
public class DrawEntitySundesmo : IDrawEntity<Sundesmo>
{
    private static readonly string DragDropTooltip =
        "--COL--[L-CLICK & DRAG]--COL-- Drag-Drop this User to another Folder." +
        "--NL----COL--[CTRL + L-CLICK]--COL-- Single-Select this item for multi-select Drag-Drop" +
        "--NL----COL--[SHIFT + L-CLICK]--COL-- Select/Deselect all users between current and last selection";
    private static readonly string NormalTooltip =
        "--COL--[L-CLICK]--COL-- Swap Between Name/Nick/Alias & UID." +
        "--NL----COL--[M-CLICK]--COL-- Open Profile" +
        "--NL----COL--[R-CLICK]--COL-- Edit Nickname";

    private readonly SundouleiaMediator _mediator;
    private readonly MainConfig _config;
    private readonly FavoritesConfig _favorites;
    private readonly IdDisplayHandler _nameHandler;

    private bool       _hovered = false;
    private bool       _showingUID = false;
    private DateTime?  _lastHoverTime;
    private bool       _popupProfileShown = false;

    private DynamicPairFolder _parentFolder;
    private Sundesmo   _sundesmo;

    public DrawEntitySundesmo(DynamicPairFolder parent, Sundesmo sundesmo, SundouleiaMediator mediator,
        MainConfig config, FavoritesConfig favorites, IdDisplayHandler nameDisp)
    {
        DistinctId = GetType() + parent.Label + sundesmo.UserData.UID;
        _parentFolder = parent;
        _sundesmo = sundesmo;

        _mediator = mediator;
        _config = config;
        _favorites = favorites;
        _nameHandler = nameDisp;
    }

    public string DistinctId { get; init; }
    public string DisplayName => _sundesmo.GetDrawEntityName();
    public string EntityId => _sundesmo.UserData.UID;
    public Sundesmo Item => _sundesmo;

    /// <summary>
    ///     Returns if the name region was clicked.
    /// </summary>
    public bool Draw(bool selected)
    {
        var clicked = false;
        var cursorPos = ImGui.GetCursorPos();
        var childSize = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        var hovered = !_nameHandler.IsEditing(DistinctId) && (_hovered || selected);
        var bgCol = hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;

        using (var _ = CkRaii.Child(DistinctId, childSize, bgCol, 5f))
        {
            ImUtf8.SameLineInner();
            DrawLeftSide();
            ImGui.SameLine();

            var pos = ImGui.GetCursorPos();
            var rightSide = DrawRightSide();
            ImGui.SameLine(pos.X);
            clicked = DrawNameArea(new(rightSide - pos.X, _.InnerRegion.Y));
        }
        _hovered = ImGui.IsItemHovered();

        // if they were a supporter, go back to the start and draw the image.
        if (_sundesmo.UserData.Tier is not CkVanityTier.NoRole)
            DrawSupporterIcon(cursorPos.X);

        return clicked;
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
        var icon = _sundesmo.IsRendered ? FAI.Eye : FAI.User;
        var color = _sundesmo.IsOnline ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudRed;
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(icon, color);
        CkGui.AttachToolTip(TooltipText());
        if (!_parentFolder.Options.DragDropItems && _sundesmo.IsRendered && ImGui.IsItemClicked())
            _mediator.Publish(new TargetSundesmoMessage(_sundesmo));
        ImGui.SameLine();
    }

    private bool DrawNameArea(Vector2 area)
    {
        // Handle the case of editing state.
        if (_nameHandler.IsEditing(DistinctId))
        {
            _nameHandler.DrawEditor(DistinctId, _sundesmo, area.X);
            return false;
        }

        // Otherwise draw out the name handle.
        var pos = ImGui.GetCursorPos();
        // Draw an invisible button covering the available area for interaction.
        var pressed = ImGui.InvisibleButton($"{DistinctId}-name-area", area);
        // Handle logic according to state.
        if (_parentFolder.Options.DragDropItems)
            AsDragDropSource();
        //else
        //    HandleClickLogic();

        // Return to the position and draw out the contents.
        ImGui.SameLine(pos.X);
        using (ImRaii.PushFont(UiBuilder.MonoFont, _showingUID))
            CkGui.TextFrameAligned(_showingUID ? EntityId : DisplayName);
        CkGui.AttachToolTip(_parentFolder.Options.DragDropItems ? DragDropTooltip : NormalTooltip, ImGuiColors.DalamudOrange);
        
        // handle hover logic if not a drag-drop item.
        //if (!_parentFolder.Options.DragDropItems)
        //    HandleTextHoverLogic(ImGui.IsItemHovered());

        return pressed;
    }

    private void AsDragDropSource()
    {
        if (!_parentFolder.Options.DragDropItems)
            return;
        // Process the item as a drag-drop source via the parent folder.
        _parentFolder.AsDragDropSource(this);
    }

    private void HandleClickLogic()
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            _showingUID = !_showingUID;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Middle))
            _mediator.Publish(new ProfileOpenMessage(_sundesmo.UserData));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            _nameHandler.ToggleEditModeForID(DistinctId, _sundesmo);
    }

    private void HandleTextHoverLogic(bool isHovered)
    {
        if (isHovered) 
        {
            // If the profile is not shown, start the timer.
            if (!_popupProfileShown && _lastHoverTime is null)
                _lastHoverTime = DateTime.UtcNow.AddSeconds(_config.Current.ProfileDelay);
            // If the time has elapsed and we are not showing the profile, show it.
            if (!_popupProfileShown && _lastHoverTime < DateTime.UtcNow && _config.Current.ShowProfiles)
            {
                _popupProfileShown = true;
                _mediator.Publish(new OpenProfilePopout(_sundesmo.UserData));
            }
        }
        else
        {
            if (_popupProfileShown)
            {
                // Reset the hover time and close the popup.
                _popupProfileShown = false;
                _lastHoverTime = null;
                _mediator.Publish(new CloseProfilePopout());
            }
        }
    }

    private string TooltipText()
    {
        var str = $"{_sundesmo.GetNickAliasOrUid()} is ";
        if (_sundesmo.IsRendered) str += $"visible ({_sundesmo.PlayerName})--SEP--Click to target this player";
        else if (_sundesmo.IsOnline) str += "online";
        else str += "offline";
        return str;
    }

    private float DrawRightSide()
    {
        var interactionsSize = CkGui.IconButtonSize(FAI.ChevronRight);
        var windowEndX = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth();
        var currentRightSide = windowEndX - interactionsSize.X;

        ImGui.SameLine(currentRightSide);
        if (!_parentFolder.Options.DragDropItems)
        {
            ImGui.AlignTextToFramePadding();
            if (CkGui.IconButton(FAI.ChevronRight, inPopup: true))
                _mediator.Publish(new ToggleSundesmoInteractionUI(_sundesmo, ToggleType.Toggle));

            currentRightSide -= interactionsSize.X;
            ImGui.SameLine(currentRightSide);
        }

        ImGui.AlignTextToFramePadding();
        SundouleiaEx.DrawFavoriteStar(_favorites, _sundesmo.UserData.UID, true);

        return currentRightSide;
    }
}
