using CkCommons;
using CkCommons.Classes;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Extensions;
using OtterGui.Text;
using OtterGui.Text.EndObjects;
using OtterGuiInternal;
using Sundouleia.Localization;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui;

public class ProfilesTab
{
    // Used for the sources in a profiles linked characters, and the target of the lower box.
    private const string DRAGDROP_ADD_CHARA = "CHARA_PROFILE_ADD";
    // Used for the sources of the lower box characters, and the target of the linked characters.
    private const string DRAGDROP_REM_CHARA = "CHARA_PROFILE_REMOVE";

    private readonly ILogger<ProfilesTab> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly MainHub _hub;
    private readonly MainConfig _mainConfig;
    private readonly AccountManager _account;
    private readonly ProfileService _profiles;
    private readonly ConfigFileProvider _files;

    private readonly Queue<Action> _postDrawActions = new();

    public ProfilesTab(ILogger<ProfilesTab> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, AccountManager account,
        ProfileService profiles, ConfigFileProvider files)
    {
        _logger = logger;
        _mediator = mediator;
        _hub = hub;
        _mainConfig = config;
        _account = account;
        _profiles = profiles;
        _files = files;
    }

    // Cached Internal Helpers (May change overtime.)
    private AccountProfile? _selected = null;
    private AccountProfile? _editingSecretKey = null;
    private AccountProfile? _showingKey = null;

    // Later, add the list of TrackedPlayers linked to this profile, and the others not in the profile.
    // We can also set this data after selecting a profile likely.
    private List<TrackedPlayer> _linkedPlayers = new();
    private List<TrackedPlayer> _otherPlayers = new();

    // The currently dragged nodes. Each label has its own drop action
    // so we dont need to perform in post or whatever, it can just be plugged into a queue.
    private TrackedPlayer? _draggedLinkedPlayer;
    private TrackedPlayer? _draggedOtherPlayer;

    private bool _isDraggingLinked => _draggedLinkedPlayer != null;
    private bool _isDraggingOther => _draggedOtherPlayer != null;

    private float GetAvatarScaleRatio(float width)
    {
        var baseWidth = ImGuiHelpers.ScaledVector2(154).X;
        return width <= baseWidth ? 1f : width / baseWidth;
    }

    // Updates the cached style references for this frame.
    private void InitStyle()
    {
        _wdl = ImGui.GetWindowDrawList();
        _style = ImGui.GetStyle();
        _frameH = ImUtf8.FrameHeight;
        _frameHSpacingWidth = ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X;

        _ckFrameCol = SundColor.Silver.Uint();
        _clientCol = SundColor.Gold.Uint();

        _txtCol = ImGui.GetColorU32(ImGuiCol.Text);
        _txtDisableCol = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        _frameBgCol = ImGui.GetColorU32(ImGuiCol.FrameBg);
        _frameBgHoverCol = ImGui.GetColorU32(ImGuiCol.FrameBgHovered);

        _bendS = _style.FrameRounding * 1.25f;
        _bendM = _style.FrameRounding * 1.75f;
        _bendL = _style.FrameRounding * 2f;
        _bendXL = _style.FrameRounding * 3f;
        
        _shadowSize = ImGuiHelpers.ScaledVector2(1);
        _styleOffset = ImGuiHelpers.ScaledVector2(2);
        _buttonPadding = _styleOffset + _style.FramePadding;

        _playerNodeH = ImUtf8.TextHeightSpacing + ImUtf8.TextHeight + ImUtf8.FramePadding.Y * 2 + _styleOffset.Y * 2;
    }

    private ImDrawListPtr _wdl;
    private ImGuiStylePtr _style;

    private float _frameH;
    private float _frameHSpacingWidth;

    private uint _ckFrameCol;
    private uint _clientCol;

    private uint _txtCol;
    private uint _txtDisableCol;
    private uint _frameBgCol;
    private uint _frameBgHoverCol;

    private float _bendS;
    private float _bendM;
    private float _bendL;
    private float _bendXL;
    private Vector2 _shadowSize;
    private Vector2 _styleOffset;
    private Vector2 _buttonPadding;

    private float _playerNodeH;
    private float _lineH => 5 * ImGuiHelpers.GlobalScale;

    // Cached profile display data. (Size is deterministic of other factors).
    private float Ratio { get; set; } = 1f;
    private Vector2 ProfileSizeBase => ImGuiHelpers.ScaledVector2(154);
    private Vector2 ProfileSize => ImGuiHelpers.ScaledVector2(154 * Ratio);
    private Vector2 RectMin { get; set; } = Vector2.Zero;
    private Vector2 AvatarPos => RectMin + ImGuiHelpers.ScaledVector2(4.2f * Ratio);
    private Vector2 AvatarSize => ImGuiHelpers.ScaledVector2(145.6f * Ratio); // Default

    public void DrawContent()
    {
        InitStyle();
        // Immidiately get the drawlist, position, size, and area available for drawing.
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var max = pos + size;
        var halfY = pos with { Y = pos.Y + size.Y / 2f };

        // Draw out the scalable background style, then fill it with a backdrop.
        // (can reference the actual profile instead once we reference the profile and not the index.
        if (CosmeticService.TryGetPlateBg(PlateElement.Plate, PlateBG.Default, out var plateBG))
            _wdl.AddDalamudImageRounded(plateBG, pos, size, _bendS);
        _wdl.AddRectFilledMultiColor(halfY, max, uint.MinValue, uint.MinValue, 0x44000000, 0x44000000);

        // Now border this with the color framing.
        using (var _ = CkRaii.FramedChildPaddedWH("Account", size, 0, _ckFrameCol, _bendM, wFlags: WFlags.NoScrollbar))
        {
            DrawProfileList(_.InnerRegion.Y);
            ImGui.SameLine();
            DrawProfilePanel(ImGui.GetContentRegionAvail());
        }

        // Perform any post-draw actions we need to.
        while (_postDrawActions.TryDequeue(out Action? action))
        {
            // Safely execute each post-draw action until the queue is empty.
            Generic.Safe(() => action());
        }

        // If drag-drop is no longer active, clear the dragged nodes.
        if (!ImGuiP.IsDragDropActive())
        {
            _draggedLinkedPlayer = null;
            _draggedOtherPlayer = null;
        }
    }

    // Profile Elements -/- Components.

    // Draws out all elements in the list, along with a add and remove profile button.
    private static float ProfileListWidth => 150f * ImGuiHelpers.GlobalScale;
    private void DrawProfileList(float height)
    {
        // The Profile list in itself is a child (padding)
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, ImGui.GetStyle().WindowPadding * .75f);
        using var _ = CkRaii.FramedChildPaddedH("profile-list", ProfileListWidth, height, 0, _ckFrameCol, _bendL);

        // ProfileSelection is itself a nested child (no padding)
        var listSize = _.InnerRegion - new Vector2(0, (CkGui.GetFancyButtonHeight() + ImUtf8.ItemSpacing.Y) * 2);
        using (CkRaii.Child("profiles", listSize, wFlags: WFlags.NoScrollbar))
        {
            var size = new Vector2(listSize.X, ImUtf8.FrameHeight + ImUtf8.TextHeightSpacing);
            foreach (var profile in _account.Profiles)
            {
                if (SelectableProfile(profile, size))
                    SetSelectedProfile(profile);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    _selected = null;
                    _editingSecretKey = null;
                    _showingKey = null;
                }
            }
        }

        // Draw the add and remove buttons below.
        if (CkGui.FancyButton(FAI.Plus, CkLoc.Settings.Accounts.AddProfile, listSize.X))
            _account.AddNewProfile();
        CkGui.AttachToolTip(CkLoc.Settings.Accounts.AddProfileTT);

        // Draw the remove button.
        if (CkGui.FancyButton(FAI.Minus, CkLoc.Settings.Accounts.RemoveProfile, listSize.X, (!ImGui.GetIO().KeyCtrl || !ImGui.GetIO().KeyShift)))
        {
            if (_selected is not null)
            {
                if (_selected.HadValidConnection)
                    ImGui.OpenPopup("Delete Account Confirmation");
                else
                {
                    _postDrawActions.Enqueue(() =>
                    {
                        // We can just remove it plainly as it is not bound to any profile serverside.
                        _account.Profiles.Remove(_selected);
                        _account.Save();
                    });
                }
            }
        }
        CkGui.AttachToolTip(CkLoc.Settings.Accounts.RemoveProfileTT);

        // Fire if true.
        AccountDeletionPopup(_selected);
    }

    #region DEBUGGER
    public static void DumpButtonColors()
    {
        var states = new[]
        {
            (active: false, hovered: false, disabled: false, name: "Idle"),
            (active: false, hovered: true,  disabled: false, name: "Hovered"),
            (active: true,  hovered: false, disabled: false, name: "Active"),
            (active: false, hovered: false, disabled: true,  name: "Disabled"),
        };

        foreach (var s in states)
        {
            uint shadowCol = 0x64000000;
            uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, CkGui.GetBorderAlpha(s.active, s.hovered, s.disabled));
            uint bgCol = CkGui.ApplyAlpha(0x64000000, CkGui.GetBgAlpha(s.active, s.hovered, s.disabled));
            uint textFade = CkGui.ApplyAlpha(0xFF1E191E, s.disabled ? 0.5f : 1f);
            uint textCol = CkGui.ApplyAlpha(0xFFFFFFFF, s.disabled ? 0.5f : 1f);

            LogColors(
                s.name,
                shadowCol,
                borderCol,
                bgCol,
                textFade,
                textCol
            );
        }

        void LogColors(string state, uint shadow, uint border, uint bg, uint textFade, uint text)
        {
            static string Hex(uint v) => $"0x{v:X8}";
            Svc.Logger.Information($"[{state}][Shadow: {Hex(shadow)}][Border: {Hex(border)}][BG: {Hex(bg)}][TextFade: {Hex(textFade)}][Text: {Hex(text)}]");
        }
    }
    #endregion DEBUGGER

    private bool DrawAddUser(Vector2 size)
    {
        // Get the internal window directly.
        var window = ImGuiInternal.GetCurrentWindow();
        if (window.SkipItems)
            return false;

        // Aquire our ID for this new internal item
        var id = ImGui.GetID("add-user");
        var pos = window.DC.CursorPos;
        var frameH = ImUtf8.FrameHeight;
        var style = ImGui.GetStyle();

        // Get the scaled versions of the border and shadow.
        var shadowSize = ImGuiHelpers.ScaledVector2(1);
        var styleOffset = ImGuiHelpers.ScaledVector2(2);
        var buttonPadding = styleOffset = style.FramePadding;
        var bend = size.Y * .5f;
        var trueH = frameH + 2 * styleOffset.Y;

        // Aquire a bounding box for our location.
        var itemSize = new Vector2(size.X, trueH);
        var hitbox = new ImRect(pos, pos + itemSize);

        // Add the item into the ImGuiInternals
        // (2nd paramater tells us how much from the outer edge to shift for text)
        ImGuiInternal.ItemSize(itemSize, style.FramePadding.Y + buttonPadding.Y);
        if (!ImGuiP.ItemAdd(hitbox, id, null))
            return false;

        // Process interaction with this 'button'
        var hovered = false;
        var active = false;
        var clicked = ImGuiP.ButtonBehavior(hitbox, id, ref hovered, ref active);

        // Render possible nav highlight space over the bounding box region.
        ImGuiP.RenderNavHighlight(hitbox, id);

        // Define our colors based on states.
        uint shadowCol = 0x64000000;
        uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, active ? 0.7f : hovered ? 0.63f : 0.39f);
        uint bgCol = CkGui.ApplyAlpha(0x64000000, active ? 0.19f : hovered ? 0.26f : 0.39f);

        // Text computation.
        var textSize = ImGui.CalcTextSize("Add User");
        var iconTextWidth = frameH + textSize.X;
        var iconPos = hitbox.Min + new Vector2((size.X - iconTextWidth) / 2f, (trueH - textSize.Y) / 2f);
        var textPos = iconPos + new Vector2(frameH, 0);

        // Outer Drop Shadow on bottom.
        window.DrawList.AddRectFilled(hitbox.Min, hitbox.Max, shadowCol, bend, ImDrawFlags.RoundCornersRight);
        // Draw over with inner border, greyish look.
        window.DrawList.AddRectFilled(hitbox.Min + shadowSize, hitbox.Max - shadowSize, borderCol, bend, ImDrawFlags.RoundCornersRight);
        // Draw over again with the bgColor.
        window.DrawList.AddRectFilled(hitbox.Min + _styleOffset, hitbox.Max - _styleOffset, bgCol, bend, ImDrawFlags.RoundCornersRight);
        // Then draw out the icon and text.
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            window.DrawList.AddText(FAI.Plus.ToIconString(), iconPos, ImGui.GetColorU32(ImGuiCol.Text));
        window.DrawList.AddText("Add User", textPos, ImGui.GetColorU32(ImGuiCol.Text));

        return clicked;
    }

    // Shift these stylizations to be calculated prior to our draws so we can use them throughout the drawframe without calculating every time.
    private bool SelectableProfile(AccountProfile profile, Vector2 size)
    {
        var window = ImGuiInternal.GetCurrentWindow();
        if (window.SkipItems)
            return false;

        // Aquire our ID for this new internal item.
        var id = ImGui.GetID(profile.Identifier.ToString());

        // Get the position and styles for our draw-space.
        var pos = window.DC.CursorPos;

        // Get the offsets and true height.
        var trueH = size.Y + _styleOffset.Y * 2;

        // Aquire a valid bounding box for this button interaction
        var itemSize = new Vector2(size.X, trueH);
        var hitbox = new ImRect(pos, pos + itemSize);
        var drawArea = new ImRect(hitbox.Min + _buttonPadding, hitbox.Max - _buttonPadding);

        // Add the item to ImGuiInternal via ImGuiP for direct integration.
        // (Note that the 2nd paramater tells us how far to shift for the text)
        ImGuiInternal.ItemSize(itemSize, _style.FramePadding.Y + _styleOffset.Y);
        if (!ImGuiP.ItemAdd(hitbox, id, null))
            return false;

        // Process interactions for our created 'Button'
        var hovered = false;
        var active = false;
        var clicked = ImGuiP.ButtonBehavior(hitbox, id, ref hovered, ref active);

        // Define our colors based on states. (Update with static values later)
        uint shadowCol = 0x64000000;
        uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, active ? 0.7f : hovered ? 0.63f : 0.39f);
        uint bgCol = CkGui.ApplyAlpha(0x64000000, active ? 0.19f : hovered ? 0.26f : 0.39f);

        // (Picture draw order like placing sticky notes on our monitor, stacking them towards us)
        window.DrawList.AddRectFilled(hitbox.Min, hitbox.Max, shadowCol, _bendM, ImDrawFlags.RoundCornersAll);
        // Draw over with inner border, greyish look.
        window.DrawList.AddRectFilled(hitbox.Min + _shadowSize, hitbox.Max - _shadowSize, borderCol, _bendM, ImDrawFlags.RoundCornersAll);
        // Draw over again with the bgColor.
        window.DrawList.AddRectFilled(hitbox.Min + _styleOffset, hitbox.Max - _styleOffset, bgCol, _bendM, ImDrawFlags.RoundCornersAll);


        // Allow for 'ImGui.IsItemHovered' to be reconized by this hitbox.
        ImGuiP.RenderNavHighlight(hitbox, id);

        // Now we need to draw out the actual contents within this area.
        var iconSize = CkGui.IconSize(FAI.CheckCircle);
        var txtSize = ImGui.CalcTextSize(profile.ProfileLabel);
        var innerClip = new ImRect(hitbox.Min + _styleOffset, new Vector2(hitbox.Max.X - iconSize.X - _style.ItemSpacing.X * 2 - _styleOffset.X, hitbox.Max.Y - _styleOffset.Y));
        
        ImGuiInternal.RenderTextClipped(window.DrawList, drawArea.Min, drawArea.Max, profile.ProfileLabel, Vector2.Zero, txtSize, innerClip, true);

        var iconPosTR = new Vector2(drawArea.Max.X - iconSize.X, drawArea.Min.Y);
        var iconPosBL = new Vector2(drawArea.Min.X, drawArea.Min.Y + ImUtf8.TextHeightSpacing);
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            window.DrawList.AddText(FAI.CheckCircle.ToIconString(), iconPosTR, profile.HadValidConnection ? CkColor.TriStateCheck.Uint() : _frameBgHoverCol);
        if (ImGui.IsMouseHoveringRect(iconPosTR, iconPosTR + iconSize))
            CkGui.ToolTipInternal(profile.HadValidConnection ? "Had a Successful Connection & Aquired UID." : "Profile has not yet connected to the server.");

        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            window.DrawList.AddText(FAI.IdCard.ToIconString(), iconPosBL, ImGuiColors.DalamudGrey.ToUint());

        window.DrawList.AddText(iconPosBL + new Vector2(iconSize.X + _style.ItemInnerSpacing.X, 0), 
            profile.HadValidConnection ? ImGuiColors.DalamudGrey2.ToUint() : ImGuiColors.DalamudRed.ToUint(), 
            profile.HadValidConnection ? profile.UserUID : "No UID Assigned");
        return clicked;
    }

    /// <summary>
    ///     Draws out the content for the currently selected profile, and the lower, registered players area.
    /// </summary>
    private void DrawProfilePanel(Vector2 region)
    {
        // Outer group
        using var _ = ImRaii.Child("profile-panel", region);
        var cursorMin = ImGui.GetCursorPos();
        var leftGapY = 0f;
        var rightGapY = 0f;
        // If no profile is selected, just draw nothing is selected and return.
        if (_selected is not { } profile)
        {
            CkGui.FontText("No Profile Selected", UiFontService.UidFont);
            return;
        }
        // We need to determine the maximum left width to know how to scale the profile image.
        var maxLeft = _frameH + CkGui.IconButtonSize(FAI.Edit).X + _style.ItemInnerSpacing.X * 2;
        using (ImRaii.PushFont(UiBuilder.MonoFont)) maxLeft += ImGui.CalcTextSize(new string('*', 32)).X;

        // Check if we exceed the maximum left side or not when compared against the base avatar size, which determines if we scale it.
        var exceedsLeftMax = region.X - ProfileSizeBase.X - _style.ItemSpacing.X > maxLeft;
        var leftWidth = exceedsLeftMax ? maxLeft : region.X - ProfileSizeBase.X - _style.ItemSpacing.X;

        using (ImRaii.Group())
        {
            CkGui.FontText(profile.ProfileLabel, UiFontService.UidFont);
            var lineSize = new Vector2(leftWidth, _lineH);
            _wdl.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.AchievementLineSplit], ImGui.GetCursorScreenPos(), lineSize);
            ImGui.Dummy(lineSize);
            DrawLabelUidAndKey(profile, leftWidth);
            leftGapY = ImGui.GetCursorPosY() + _style.ItemSpacing.Y;
        }

        // Now sameline and do the avatar.
        ImGui.SameLine();

        // Take the leftmax, subtract lineH, and divide by 2, this will be how it divides the splits.
        var maxY = cursorMin.Y + region.Y;
        var playerDrawRegionsH = (maxY - leftGapY - _lineH) / 2;

        // Get the Y pos of the line, by cursorMinY + region.Y - lowerHalfY
        var splitLinePosY = maxY - playerDrawRegionsH - _lineH;

        // Ensure the ratio does not exceed the max allowed height.
        var maxRatioValue = (splitLinePosY - cursorMin.Y) / ProfileSizeBase.Y;
        Ratio = exceedsLeftMax ? Math.Clamp(GetAvatarScaleRatio(ImGui.GetContentRegionAvail().X), 1f, maxRatioValue) : 1f;

        // Correctly draw the profile.
        DrawAvatar(profile);
        var rightPos = ImGui.GetCursorScreenPos() + new Vector2(0, ProfileSize.Y);
        // Using the cursorPos from min, add the scaled ProfileSizeY to get the YPos.
        rightGapY = cursorMin.Y + ProfileSize.Y + _style.ItemSpacing.Y;

        // Determine the height on the left via LinePosY - LeftHeightEndY
        var leftTopAvailH = splitLinePosY - leftGapY;
        // Determine the height on the right by using LinePosY - ProfilePosY
        var rightTopAvailH = splitLinePosY - rightGapY;

        var leftOverRight = (leftTopAvailH >= rightTopAvailH * 1.5f);
        var canFitOnRight = (rightTopAvailH >= (_playerNodeH));
        var shouldDrawLeftBox = leftOverRight || !canFitOnRight;
        // Get the deterministic dimentions.
        var linkedWidth = shouldDrawLeftBox ? leftWidth : region.X;
        var newPos = shouldDrawLeftBox ? new Vector2(cursorMin.X, leftGapY) : new Vector2(cursorMin.X, rightGapY);

        ImGui.SetCursorPos(newPos);
        // Left aligned box, starting at the LeftStartY.
        DrawLinkedPlayers(profile, linkedWidth, shouldDrawLeftBox ? leftTopAvailH : rightTopAvailH);
        CkGui.AttachToolTip("The Characters linked to this Profile.");
        // Draw the line and lower area
        DrawOtherPlayers();
    }

    private void DrawLabelUidAndKey(AccountProfile profile, float width)
    {
        var showEditor = _editingSecretKey == profile;
        var showKey = _showingKey == profile;

        using var font = ImRaii.PushFont(UiBuilder.MonoFont);
        CkGui.FramedIconText(FAI.Font);
        ImUtf8.SameLineInner();
        ImGui.SetNextItemWidth(width - _frameHSpacingWidth);
        var labelTmp = profile.ProfileLabel;
        if (ImGui.InputTextWithHint("##accentLabel", "Account Label...", ref labelTmp, 20))
        {
            profile.ProfileLabel = labelTmp;
            _account.Save();
        }

        CkGui.FramedIconText(FAI.IdBadge);
        CkGui.TextFrameAlignedInline("UID:");
        var noUid = string.IsNullOrEmpty(profile.UserUID);
        CkGui.ColorTextFrameAlignedInline(noUid ? "Not Yet Assigned" : profile.UserUID, noUid ? ImGuiColors.DalamudRed : ImGuiColors.TankBlue);
        CkGui.AttachToolTip("Once you successfully connect with the inserted secret key below, your UID will be set!");

        CkGui.FramedHoverIconText(FAI.Key, ImGuiColors.TankBlue.ToUint());
        CkGui.AttachToolTip(CkLoc.Settings.Accounts.ProfileKey);
        if (ImGui.IsItemClicked())
            _showingKey = _showingKey == profile ? null : profile;

        // Draw based on what should be displayed.
        ImUtf8.SameLineInner();
        var innerWidth = width - _frameHSpacingWidth - CkGui.IconButtonSize(FAI.PenSquare).X;

        if (showEditor)
        {
            ImGui.SetNextItemWidth(innerWidth);
            var key = profile.Key;
            if (ImGui.InputTextWithHint("##KeyEditor", "Paste SecretKey Here...", ref key, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                key = key.Trim();
                //Fail if the key already exists.
                if (_account.TryUpdateSecretKey(profile, key))
                    _logger.LogDebug($"Updated SecretKey for {profile.ProfileLabel}");
                // exit edit mode.
               _editingSecretKey = null;
            }
        }
        else
        {
            var pos = ImGui.GetCursorScreenPos();
            var txtSize = new Vector2(innerWidth, ImUtf8.TextHeight);
            var txtRect = new ImRect(pos, pos + txtSize);
            var txt = showKey ? profile.Key : new string('*', Math.Clamp(profile.Key.Length, 0, 32));
            ImGuiInternal.RenderTextClipped(_wdl, txtRect.Min + _style.FramePadding, txtRect.Max - _style.FramePadding, txt, Vector2.Zero, txtSize, txtRect, true);
            ImGui.Dummy(txtSize);
            CkGui.AttachToolTip(CkLoc.Settings.Accounts.CopyKeyTT);
            if (ImGui.IsItemClicked())
                ImGui.SetClipboardText(profile.Key);
        }
        // Add the edit button.
        if (profile.HadValidConnection)
        {
            ImGui.SameLine(width - ImUtf8.FrameHeight, 0);
            CkGui.FramedIconText(FAI.CheckCircle);
            CkGui.AttachToolTip(CkLoc.Settings.Accounts.NoEditKeyTT);
        }
        else
        {
            ImGui.SameLine(width - CkGui.IconButtonSize(FAI.PenSquare).X, 0);
            if (CkGui.IconButton(FAI.PenSquare, inPopup: true))
                _editingSecretKey = showEditor ? null : profile;
            CkGui.AttachToolTip(CkLoc.Settings.Accounts.EditKeyTT);
        }
    }
    
    // Scalable avatar display, the other measurements should adapt to this scale.
    private void DrawAvatar(AccountProfile profile)
    {
        if (!MainHub.IsConnectionDataSynced || !profile.HadValidConnection)
            return;

        var profileData = _profiles.GetProfile(new(profile.UserUID));
        var avatar = profileData.GetAvatarOrDefault();
        RectMin = ImGui.GetCursorScreenPos();
        // Draw out the avatar image.
        _wdl.AddDalamudImageRounded(avatar, AvatarPos, AvatarSize, AvatarSize.Y / 2);
        // draw out the border for the profile picture
        if (CosmeticService.TryGetPlateBorder(PlateElement.Avatar, profileData.Info.AvatarBorder, out var pfpBorder))
            _wdl.AddDalamudImageRounded(pfpBorder, RectMin, ProfileSize, ProfileSize.Y / 2);
    }

    private void DrawLinkedPlayers(AccountProfile profile, float fullWidth, float innerHeight)
    {
        // Outer Child style
        var greenBg = CkColor.TriStateCheck.Vec4();
        var bgCol = _isDraggingOther ? CkGui.Color(Gradient.Get(greenBg, greenBg with { W = greenBg.W / 4 }, 500)) : 0;

        var clientPlayer = _linkedPlayers.FirstOrDefault(c => c.ContentId == PlayerData.CID);
        // Outline the child to draw.
        using (CkRaii.FramedChildPaddedW("LinkedPlayers", fullWidth, innerHeight, bgCol, ImGui.GetColorU32(ImGuiCol.Separator), _bendS))
        {
            var remainingWidth = ImGui.GetContentRegionAvail().X;
            foreach ((var player, var idx) in _linkedPlayers.WithIndex())
            {
                // Get the width of the child area.
                var nameSize = ImGui.CalcTextSize(player.PlayerName);
                var worldSize = ImGui.CalcTextSize(GameDataSvc.WorldData.TryGetValue(player.WorldId, out var worldName) ? worldName : "UNK");
                // Get standard width
                var totalWidth = Math.Max(nameSize.X, worldSize.X) + _frameHSpacingWidth + _style.FramePadding.X * 2;

                remainingWidth -= totalWidth - _style.ItemInnerSpacing.X;
                if (idx is not 0)
                {
                    if (remainingWidth <= 0) remainingWidth = ImGui.GetContentRegionAvail().X;
                    else ImUtf8.SameLineInner();
                }

                // Draw the node.
                DrawPlayerNode(player, totalWidth, nameSize.X, worldSize.X, clientPlayer == player);
                AsDragDropSource(DRAGDROP_REM_CHARA, player);
            }
        }
        // Define this child as a target for the lower other player nodes.
        AsDragDropTarget(DRAGDROP_ADD_CHARA);
    }

    private void DrawOtherPlayers()
    {
        // Shift to the bottom and draw the unlinked, but for now, just draw the unlinked.
        var lineSize = new Vector2(ImGui.GetContentRegionAvail().X, 5 * ImGuiHelpers.GlobalScale);
        _wdl.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.AchievementLineSplit], ImGui.GetCursorScreenPos(), lineSize);
        ImGui.Dummy(lineSize);

        // Outer Style
        var greenBg = CkColor.TriStateCheck.Vec4();
        var bgCol = _isDraggingLinked ? CkGui.Color(Gradient.Get(greenBg, greenBg with { W = greenBg.W / 4 }, 500)) : 0;

        var clientPlayer = _otherPlayers.FirstOrDefault(c => c.ContentId == PlayerData.CID);
        var remainingWidth = ImGui.GetContentRegionAvail().X;

        using (CkRaii.Child("OtherPlayers", ImGui.GetContentRegionAvail(), bgCol))
        {
            foreach ((var player, var idx) in _otherPlayers.WithIndex())
            {
                // Get the width of the child area.
                var nameSize = ImGui.CalcTextSize(player.PlayerName);
                var worldSize = ImGui.CalcTextSize(GameDataSvc.WorldData.TryGetValue(player.WorldId, out var worldName) ? worldName : "UNK");
                // Get standard width
                var totalWidth = Math.Max(nameSize.X, worldSize.X) + _frameHSpacingWidth + _style.FramePadding.X * 2;
                if (player.IsLinked())
                    totalWidth += _frameHSpacingWidth;

                remainingWidth -= totalWidth - _style.ItemInnerSpacing.X;
                if (idx is not 0)
                {
                    if (remainingWidth <= 0) remainingWidth = ImGui.GetContentRegionAvail().X;
                    else ImUtf8.SameLineInner();
                }
                // Draw the node.
                DrawPlayerNode(player, totalWidth, nameSize.X, worldSize.X, clientPlayer == player, player.IsLinked());
                AsDragDropSource(DRAGDROP_ADD_CHARA, player);
                CkGui.AttachToolTip($"Linked to --COL--{player.LinkedProfile?.UserUID ?? "UNK"}--COL--", !player.IsLinked(), ImGuiColors.TankBlue);
            }
        }
        // Define the child as a target for the removal area.
        AsDragDropTarget(DRAGDROP_REM_CHARA);
    }

    private void DrawPlayerNode(TrackedPlayer player, float width, float nameWidth, float worldWidth, bool isClient, bool showLinks = false)
    {
        var window = ImGuiInternal.GetCurrentWindow();
        if (window.SkipItems)
            return;

        // Aquire our ID for this new internal item.
        var id = ImGui.GetID(player.ContentId.ToString());

        // Get the position and styles for our draw-space.
        var pos = window.DC.CursorPos;
        var style = ImGui.GetStyle();

        // Aquire a valid bounding box for this button interaction
        var itemSize = new Vector2(width, _playerNodeH);
        var hitbox = new ImRect(pos, pos + itemSize);
        var drawArea = new ImRect(hitbox.Min + style.FramePadding + _styleOffset, hitbox.Max - style.FramePadding - _styleOffset);

        // Add the item to ImGuiInternal via ImGuiP for direct integration.
        // (Note that the 2nd paramater tells us how far to shift for the text)
        ImGuiInternal.ItemSize(itemSize, style.FramePadding.Y + _styleOffset.Y);
        if (!ImGuiP.ItemAdd(hitbox, id, null))
            return;

        // Process interactions for our created 'Button'
        var hovered = false;
        var active = false;
        var clicked = ImGuiP.ButtonBehavior(hitbox, id, ref hovered, ref active);

        // Allow for 'ImGui.IsItemHovered' to be reconized by this hitbox.
        ImGuiP.RenderNavHighlight(hitbox, id);

        // Define our colors based on states. (Update with static values later)
        uint shadowCol = 0x64000000;
        uint borderCol = CkGui.ApplyAlpha(0xDCDCDCDC, active ? 0.7f : hovered ? 0.63f : 0.39f);
        uint bgCol = CkGui.ApplyAlpha(0x64000000, active ? 0.19f : hovered ? 0.26f : 0.39f);

        // (Picture draw order like placing sticky notes on our monitor, stacking them towards us)

        // Outer Shadow
        window.DrawList.AddRectFilled(hitbox.Min, hitbox.Max, shadowCol, _bendS, ImDrawFlags.RoundCornersAll);
        // Inner Border
        window.DrawList.AddRectFilled(hitbox.Min + _shadowSize, hitbox.Max - _shadowSize, borderCol, _bendS, ImDrawFlags.RoundCornersAll);
        // Main BG
        window.DrawList.AddRectFilled(hitbox.Min + _styleOffset, hitbox.Max - _styleOffset, bgCol, _bendS, ImDrawFlags.RoundCornersAll);

        // If this player is our current player, draw the yellow border.
        if (isClient)
            window.DrawList.AddRect(hitbox.Min, hitbox.Max, _clientCol, _bendS, ImDrawFlags.RoundCornersAll);

        // Now we need to draw out the actual contents within this area.
        var iconSize = CkGui.IconSize(FAI.UserCircle);
        var txtSize = ImGui.CalcTextSize(player.PlayerName);
        // First row.
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            window.DrawList.AddText(FAI.UserCircle.ToIconString(), drawArea.Min, _txtCol);

        window.DrawList.AddText(drawArea.Min + new Vector2(iconSize.X + style.ItemInnerSpacing.X, 0), _txtCol, player.PlayerName);

        // Second row.
        var secondRowY = drawArea.Min.Y + ImUtf8.TextHeightSpacing;
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            window.DrawList.AddText(FAI.Globe.ToIconString(), new Vector2(drawArea.Min.X, secondRowY), _txtDisableCol);

        var worldName = GameDataSvc.WorldData.TryGetValue(player.WorldId, out var wName) ? wName : "UNK";
        window.DrawList.AddText(new Vector2(drawArea.Min.X + iconSize.X + style.ItemInnerSpacing.X, secondRowY), _txtDisableCol, worldName);

        if (showLinks && player.IsLinked())
        {
            var iconPosTR = new Vector2(drawArea.Max.X - iconSize.X, drawArea.Min.Y + (drawArea.GetSize().Y - iconSize.Y) * .5f);
            using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                window.DrawList.AddText(FAI.Link.ToIconString(), iconPosTR, CkColor.TriStateCross.Uint());
        }


    }

    private void AsDragDropSource(string label, TrackedPlayer node)
    {
        using var source = ImUtf8.DragDropSource();
        if (!source) return;

        if (!DragDropSource.SetPayload(label))
        {
            if (label.Equals(DRAGDROP_REM_CHARA) && !node.Equals(_draggedLinkedPlayer))
                _draggedLinkedPlayer = node;
            if (label.Equals(DRAGDROP_ADD_CHARA) && !node.Equals(_draggedOtherPlayer))
                _draggedOtherPlayer = node;
        }

        // Display any current drag text.
        CkGui.InlineSpacing();
        ImGui.Text($"Dragging {(label.Equals(DRAGDROP_REM_CHARA) ? "Linked" : "Other")} Player: {node.PlayerName}...");
    }

    private void AsDragDropTarget(string label)
    {
        using var target = ImRaii.DragDropTarget();
        if (!target)
            return;
        // If we are not dropping the opLabel, or the cache is not active, ignore this.
        if (!ImGuiUtil.IsDropping(label))
            return;

        // Action for linking
        if (DRAGDROP_ADD_CHARA == label)
            _postDrawActions.Enqueue(LinkToProfile);

        // Action for unlinking
        if (DRAGDROP_REM_CHARA == label)
            _postDrawActions.Enqueue(UnlinkFromProfile);
    }

    private void SetSelectedProfile(AccountProfile profile)
    {
        // Do nothing if the same.
        if (_selected == profile)
            return;
        // Update the profile.
        _selected = profile;
        _showingKey = null;
        _editingSecretKey = profile.Key.IsNullOrWhitespace() ? profile : null;
        // Update the linked and other players lists.
        _linkedPlayers = _account.TrackedPlayers.Values.Where(tp => tp.LinkedProfile == profile).ToList();
        _otherPlayers = _account.TrackedPlayers.Values.Where(tp => tp.LinkedProfile != profile).ToList();
    }

    private void RefreshTrackedPlayers()
    {
        if (_selected is not { } profile)
            return;
        _linkedPlayers = _account.TrackedPlayers.Values.Where(tp => tp.LinkedProfile == profile).ToList();
        _otherPlayers = _account.TrackedPlayers.Values.Where(tp => tp.LinkedProfile != profile).ToList();
    }

    /// <summary>
    ///     Takes the currently dragged otherPlayer node, 
    ///     and adds it to the selected profile.
    /// </summary>
    private void LinkToProfile()
    {
        if (_selected is not { } profile)
            return;

        if (_draggedOtherPlayer is not { } player)
            return;

        // If the destination profile is the same as the current, nothing changed, so do nothing.
        if (player.LinkedProfile == profile)
            return;

        var movedClientPlayer = player.ContentId == PlayerData.CID;
        var wasLoggedIntoMoved = player.LinkedProfile?.UserUID == MainHub.UID;
        // Update the linked profile.
        player.LinkedProfile = profile;
        _account.Save();
        // Refresh the tracked players.
        RefreshTrackedPlayers();

        // If we were logged into the moved player, we switched,
        if (movedClientPlayer && wasLoggedIntoMoved)
            UiService.SetUITask(async () => await _hub.Reconnect(DisconnectIntent.Reload).ConfigureAwait(false));
        // For fresh logins
        else if (movedClientPlayer && !MainHub.IsConnected && MainHub.ServerStatus is not ServerState.Connecting)
            UiService.SetUITask(async () => await _hub.Reconnect(DisconnectIntent.Reload).ConfigureAwait(false));

    }

    private void UnlinkFromProfile()
    {
        if (_selected is not { } profile)
        {
            _logger.LogWarning("Profile not valid");
            return;
        }
        if (_draggedLinkedPlayer is not { } player)
        {
            _logger.LogWarning("Dragged Player not valid");
            return;
        }

        var removedClientPlayer = player.ContentId == PlayerData.CID;
        var loggedInCharaRemoved = player.LinkedProfile?.UserUID == MainHub.UID;
        // Remove the profile from the character.
        player.LinkedProfile = null;
        _account.Save();
        // Refresh the tracked players.
        RefreshTrackedPlayers();

        // Reconnect them, and clear the dragged node.
        if (loggedInCharaRemoved && removedClientPlayer)
            UiService.SetUITask(async () => await _hub.Reconnect(DisconnectIntent.Reload).ConfigureAwait(false));
    }

    public void AccountDeletionPopup(AccountProfile? profile)
    {
        if (profile is null)
            return;

        if (!ImGui.IsPopupOpen("Delete Account Confirmation"))
            return;

        // center the hardcore window.
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f));
        // set the size of the popup.
        var size = new Vector2(600f, 345f * ImGuiHelpers.GlobalScale);
        ImGui.SetNextWindowSize(size);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowBorderSize, 1f)
            .Push(ImGuiStyleVar.WindowRounding, 12f);
        using var c = ImRaii.PushColor(ImGuiCol.Border, ImGuiColors.DalamudGrey2);

        using var pop = ImRaii.Popup("Delete Account Confirmation", WFlags.Modal | WFlags.NoResize | WFlags.NoScrollbar | WFlags.NoMove);
        if (!pop)
            return;

        using (ImRaii.Group())
        {
            CkGui.FontTextCentered("WARNING", UiFontService.UidFont, ImGuiColors.DalamudRed);
            CkGui.Separator(ImGuiColors.DalamudRed.ToUint(), size.X);

            if (profile.IsPrimary)
            {
                CkGui.IconText(FAI.ExclamationTriangle, ImGuiColors.DalamudYellow);
                CkGui.TextInline("You are about to delete your PRIMARY account.");
                CkGui.IconText(FAI.ExclamationTriangle, ImGuiColors.DalamudYellow);
                CkGui.ColorTextInline("THIS WILL ALSO DELETE ALL YOUR ALT PROFILES.", ImGuiColors.DalamudYellow);
                ImGui.Spacing();
                CkGui.IconText(FAI.Exclamation, ImGuiColors.DalamudRed);
                CkGui.TextInline("This is effectively a FACTORY RESET of your Account!");
                CkGui.Separator(ImGuiColors.DalamudRed.ToUint(), size.X - ImGui.GetStyle().WindowPadding.X);
            }

            CkGui.IconText(FAI.InfoCircle);
            CkGui.TextInline("Removing your profile erases all stored data associated with it, including:");

            CkGui.IconText(FAI.ArrowRight);
            CkGui.TextInline("All Configured Permissions");
            CkGui.IconText(FAI.ArrowRight);
            CkGui.TextInline("Your Paired Users");
            CkGui.IconText(FAI.ArrowRight);
            CkGui.TextInline("Your uploaded SMA Protected Data");
            CkGui.IconText(FAI.ArrowRight);
            CkGui.TextInline("Saved Achievement Data");
        }
        var yesButton = $"I Understand, Delete Profile for {profile.ProfileLabel}({profile.UserUID})";
        var noButton = "Uhh... Take me back!";
        var yesSize = ImGuiHelpers.GetButtonSize(yesButton);
        var noSize = ImGuiHelpers.GetButtonSize(noButton);
        var offsetX = (size.X - (yesSize.X + noSize.X + ImUtf8.ItemSpacing.X).RemoveWinPadX()) / 2;
        CkGui.SeparatorSpaced();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
        using (ImRaii.Disabled(!(ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl)))
        {
            if (ImGui.Button(yesButton))
            {
                UiService.SetUITask(() => RemoveProfileAndReload(profile));
                ImGui.CloseCurrentPopup();
            }
        }
        CkGui.AttachToolTip("Must hold CTRL+SHIFT to select!");

        ImGui.SameLine();
        if (ImGui.Button(noButton))
            ImGui.CloseCurrentPopup();
    }

    private async Task RemoveProfileAndReload(AccountProfile profile)
    {
        // grab the uid before we delete the user.
        var uid = MainHub.UID;
        var isMain = profile.IsPrimary;
        // Remove the profile from the account config.
        try
        {
            _logger.LogInformation("Removing Authentication for current character.");
            _account.Profiles.Remove(profile);
            // The server automatically handles cleanup of alt profiles, so just clear the manager.
            if (isMain)
            {
                _logger.LogInformation("Removed Primary Profile, removing all other profiles.");
                _account.Profiles.Clear();
            }

            // Update the last logged in UID.
            _mainConfig.Current.LastUidLoggedIn = string.Empty;
            _mainConfig.Save();

            // Extract the UID's so that we know what folders to delete in our config. (If we want to, we could keep them as a backup, idk)
            var accountUids = MainHub.ConnectionResponse?.ActiveAccountUidList ?? [];
            _logger.LogInformation("Deleting Account from Server.");
            await _hub.UserDelete();

            // Delete the folders based off our profile type that was deleted.
            if (isMain)
            {
                var toDelete = Directory.GetDirectories(ConfigFileProvider.SundouleiaDirectory)
                    .Where(d => accountUids.Contains(d, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                foreach (var folder in toDelete)
                    Directory.Delete(folder, true);

                _logger.LogInformation("Removed all deleted profile-related folders.");
                // Cleanup the remaining UID's
                _files.ClearUidConfigs();
                // Fully disconnect and switch back to the intro UI.
                await _hub.Disconnect(ServerState.Disconnected, DisconnectIntent.Reload);
                _mediator.Publish(new SwitchToIntroUiMessage());
            }
            else
            {
                var toDelete = _files.CurrentProfileDirectory;
                if (Directory.Exists(toDelete))
                {
                    _logger.LogDebug("Deleting Config Folder for removed profile.", LoggerType.ApiCore);
                    Directory.Delete(toDelete, true);
                }
                _files.ClearUidConfigs();
                await _hub.Reconnect(DisconnectIntent.Reload);
            }
        }
        catch (Bagagwa ex)
        {
            _logger.LogError("Failed to delete account from server." + ex);
        }
    }
}
