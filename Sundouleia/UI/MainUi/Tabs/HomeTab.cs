using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Profiles;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;
using System.Globalization;

namespace Sundouleia.Gui.MainWindow;

public class HomeTab
{
    private const byte BUTTON_ACTIVE_OPACITY = 170;
    private const byte BUTTON_HOVER_OPACITY = 170;
    private const byte BUTTON_TRANSPARENCY = 100;
    private const int BUTTON_SHADOW_SIZE = 1;
    private const int BUTTON_BORDER_SIZE = 2;

    private readonly SundouleiaMediator _mediator;
    private readonly ProfileService _service;
    private readonly TutorialService _guides;

    public HomeTab(SundouleiaMediator mediator, ProfileService service, TutorialService guides)
    {
        _mediator = mediator;
        _service = service;
        _guides = guides;
    }

    // Profile Draw Helpers.
    private Vector2 ProfileSize => ImGuiHelpers.ScaledVector2(220);
    private Vector2 RectMin { get; set; } = Vector2.Zero;
    private Vector2 AvatarPos => RectMin + ImGuiHelpers.ScaledVector2(6f);
    private Vector2 AvatarSize => ImGuiHelpers.ScaledVector2(208f);
    private Vector2 EditBorderSize => ImGuiHelpers.ScaledVector2(48f);
    private Vector2 EditBorderPos => RectMin + ImGuiHelpers.ScaledVector2(170f, 2f);
    private Vector2 EditIconPos => RectMin + ImGuiHelpers.ScaledVector2(179f, 11f);
    private Vector2 EditIconSize => ImGuiHelpers.ScaledVector2(30f);

    // For tutorials.
    private static Vector2 LastWinPos = Vector2.Zero;
    private static Vector2 LastWinSize = Vector2.Zero;

    public void DrawSection()
    {
        var wdl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();
        var max = pos + size;
        var halfPos = pos with { Y = pos.Y + size.Y / 2f };
        var profile = _service.GetProfile(MainHub.OwnUserData);
        // Background
        if (CosmeticService.TryGetPlateBg(PlateElement.Plate, profile.Info.MainBG, out var plateBG))
            wdl.AddDalamudImageRounded(plateBG, pos, size, CkStyle.ChildRounding());

        // Gradient backdrop
        wdl.AddRectFilledMultiColor(halfPos, max, uint.MinValue, uint.MinValue, 0x44000000, 0x44000000);
        
        using var _ = CkRaii.FramedChildPaddedWH("Account", size, 0, CkColor.VibrantPink.Uint(), CkStyle.ChildRounding(), wFlags: WFlags.NoScrollbar);
        
        DrawProfileInfo(_.InnerRegion, profile);
        ImGui.Spacing();
        DrawMenuOptions();
    }

    private void DrawProfileInfo(Vector2 region, Profile profile)
    {
        var left = region.X - ProfileSize.X - ImUtf8.ItemSpacing.X;
        var wdl = ImGui.GetWindowDrawList();
        using (CkRaii.Child("##AccountInfo", new Vector2(left, ProfileSize.Y)))
        {
            CkGui.FontText(MainHub.DisplayName, UiFontService.UidFont);
            CkGui.AttachToolTip("Your Profile's Alias / UID.");
            CkGui.CopyableDisplayText(MainHub.DisplayName);
            // Line Splitter.
            var pos = ImGui.GetCursorScreenPos();
            var lineSize = new Vector2(region.X - ProfileSize.X - ImUtf8.ItemSpacing.X, 5 * ImGuiHelpers.GlobalScale);
            wdl.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.AchievementLineSplit], pos, lineSize);
            ImGui.Dummy(lineSize);

            ProfileInfoRow(FAI.IdBadge, MainHub.UID, string.Empty);
            CkGui.AttachToolTip("Your Profile's UID.");
            CkGui.CopyableDisplayText(MainHub.DisplayName);

            ProfileInfoRow(FAI.UserSecret, MainHub.OwnUserData.AnonName, "Your Anonymous name used in Requests / Chats.");

            var formattedDate = MainHub.OwnUserData.CreatedOn ?? DateTime.MinValue;
            string createdDate = formattedDate != DateTime.MinValue ? formattedDate.ToString("d", CultureInfo.CurrentCulture) : "MM-DD-YYYY";
            ProfileInfoRow(FAI.Calendar, createdDate, "Date your Sundouleia account was made.");

            ProfileInfoRow(FAI.Award, "0/???", "Current Achievement Progress.--SEP--Still WIP, soon in future updates.");

            ProfileInfoRow(FAI.ExclamationTriangle, $"{MainHub.Reputation.TotalStrikes()} Strikes.", "Reflects current Account Standing." +
                "--SEP--Accumulating too many strikes may lead to restrictions or bans.");
        }
        
        // Then the profile image.
        ImGui.SameLine();
        using (CkRaii.Child("##AccountPFP", ProfileSize))
        {
            var avatar = profile.GetAvatarOrDefault();
            RectMin = ImGui.GetCursorScreenPos();

            // Draw out the avatar image.
            wdl.AddDalamudImageRounded(avatar, AvatarPos, AvatarSize, AvatarSize.Y / 2);
            // draw out the border for the profile picture
            if (CosmeticService.TryGetPlateBorder(PlateElement.Avatar, profile.Info.AvatarBorder, out var pfpBorder))
                wdl.AddDalamudImageRounded(pfpBorder, RectMin, ProfileSize, ProfileSize.Y / 2);

            // Draw out Supporter Icon Black BG base.
            ImGui.SetCursorScreenPos(EditBorderPos);
            if (ImGui.InvisibleButton("##EditProfileButton", EditBorderSize))
                _mediator.Publish(new UiToggleMessage(typeof(ProfileEditorUI)));
            CkGui.AttachToolTip("Open and Customize your Profile!");

            var bgCol = ImGui.IsItemHovered() ? 0xFF444444 : 0xFF000000;
            wdl.AddCircleFilled(EditBorderPos + EditBorderSize / 2, EditBorderSize.X / 2, bgCol);
            // Draw out Edit Icon.
            wdl.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.Edit], EditIconPos, EditIconSize);
            wdl.AddCircle(EditBorderPos + EditBorderSize / 2, EditBorderSize.X / 2, CkColor.VibrantPink.Uint(), 0, 3f * ImGuiHelpers.GlobalScale);
        }
    }

    private void ProfileInfoRow(FAI icon, string text, string tooltip)
    {
        ImGui.Spacing();
        using (ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(icon);
            CkGui.TextFrameAlignedInline(text);
        }
        CkGui.AttachToolTip(tooltip);
    }

    private void DrawMenuOptions()
    {
        var region = ImGui.GetContentRegionAvail();
        // PreCalculations to safe on draw-time performance.
        var borderSize = ImGuiHelpers.ScaledVector2(BUTTON_BORDER_SIZE);
        var shadowSize = ImGuiHelpers.ScaledVector2(BUTTON_SHADOW_SIZE);
        var buttonHeight = ImUtf8.FrameHeight + 2 * borderSize.Y + 2 * shadowSize.Y;
        // The threshold to draw 2 or 1 rows.
        var thresholdHeight = buttonHeight * 8 + ImUtf8.ItemSpacing.Y * 7;
        // if we draw compact (2 columns) or full (1 column)
        var showCompact = region.Y < thresholdHeight.AddWinPadY();
        // Finalized Height of the child.
        var finalHeight = buttonHeight * (showCompact ? 4 : 8) + ImUtf8.ItemSpacing.Y * (showCompact ? 3 : 7);

        if (showCompact)
            DrawCompactButtons(region, buttonHeight, borderSize, shadowSize);
        else
            DrawButtonList(region, buttonHeight, borderSize, shadowSize);
    }

    private void DrawCompactButtons(Vector2 region, float buttonHeight, Vector2 borderSize, Vector2 shadowSize)
    {
        var buttonSize = new Vector2((region.X - ImUtf8.ItemInnerSpacing.X) / 2, buttonHeight);
        using (ImRaii.Group())
        {
            if (FancyButtonCentered(buttonSize, FAI.PeopleGroup, "Manage Groups", borderSize, shadowSize))
                _mediator.Publish(new UiToggleMessage(typeof(GroupsUI)));
            CkGui.AttachToolTip("Create, arrange, delete, and manage Groups.");

            if (FancyButtonCentered(buttonSize, FAI.MagnifyingGlassChart, "Actor Analyzer", borderSize, shadowSize))
                _mediator.Publish(new UiToggleMessage(typeof(ChangelogUI)));
            CkGui.AttachToolTip("Inspect data of owned actors!");

            if (FancyButtonCentered(buttonSize, FAI.FileExport, "MCDF Controller", borderSize, shadowSize))
            {
                // Something.
            }
            CkGui.AttachToolTip("Export and Organize MCDF's.");

            if (FancyButtonCentered(buttonSize, FAI.Trophy, "Achievements", borderSize, shadowSize))
            {
                // Something.
            }
            CkGui.AttachToolTip("View Achievement Progress & Rewards." +
                "--SEP--Still WIP, Coming in future updates.");
        }
        ImUtf8.SameLineInner();
        using (ImRaii.Group())
        {
            if (FancyButtonCentered(buttonSize, FAI.Cog, "Open Settings", borderSize, shadowSize))
                _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            CkGui.AttachToolTip("Opens the Settings UI.");

            if (FancyButtonCentered(buttonSize, FAI.Bell, "Events Viewer", borderSize, shadowSize))
                _mediator.Publish(new UiToggleMessage(typeof(DataEventsUI)));
            CkGui.AttachToolTip("View fired events for pair updates.");

            if (FancyButtonCentered(buttonSize, FAI.Coffee, "Support Sundouleia", borderSize, shadowSize))
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/cw/Sundouleia", UseShellExecute = true }); }
                catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the Patreon link. {e.Message}"); }
            }
            CkGui.AttachToolTip("If you like my work, you can toss any support here ♥");

            if (FancyButtonCentered(buttonSize, FAI.Wrench, "Open Config", borderSize, shadowSize))
            {
                try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.SundouleiaDirectory, UseShellExecute = true }); }
                catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
            }
            CkGui.AttachToolTip("Opens the Config Folder.--NL--(Useful for debugging)");
        }
    }

    private void DrawButtonList(Vector2 region, float buttonHeight, Vector2 borderSize, Vector2 shadowSize)
    {
        var buttonSize = new Vector2(region.X, buttonHeight);
        if (FancyButton(buttonSize, FAI.PeopleGroup, "Manage Groups", borderSize, shadowSize))
            _mediator.Publish(new UiToggleMessage(typeof(GroupsUI)));
        CkGui.AttachToolTip("Create, arrange, delete, and manage Groups.");

        if (FancyButton(buttonSize, FAI.MagnifyingGlassChart, "Actor Analyzer", borderSize, shadowSize))
            _mediator.Publish(new UiToggleMessage(typeof(ChangelogUI)));
        CkGui.AttachToolTip("Inspect data of owned actors!");

        if (FancyButton(buttonSize, FAI.FileExport, "MCDF Controller", borderSize, shadowSize))
        {
            // Something.
        }
        CkGui.AttachToolTip("Export and Organize MCDF's.");

        if (FancyButton(buttonSize, FAI.Trophy, "Achievements", borderSize, shadowSize))
        {
            // Something.
        }
        CkGui.AttachToolTip("View Achievement Progress & Rewards.--SEP--Still WIP, Coming in future updates.");

        if (FancyButton(buttonSize, FAI.Cog, "Open Settings", borderSize, shadowSize))
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        CkGui.AttachToolTip("Opens the Settings UI.");

        if (FancyButton(buttonSize, FAI.Bell, "Events Viewer", borderSize, shadowSize))
            _mediator.Publish(new UiToggleMessage(typeof(DataEventsUI)));
        CkGui.AttachToolTip("View fired events for pair updates.");

        if (FancyButton(buttonSize, FAI.Coffee, "Support Sundouleia", borderSize, shadowSize))
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/cw/Sundouleia", UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the Patreon link. {e.Message}"); }
        }
        CkGui.AttachToolTip("If you like my work, you can toss any support here ♥");

        if (FancyButton(buttonSize, FAI.Wrench, "Open Config", borderSize, shadowSize))
        {
            try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.SundouleiaDirectory, UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
        }
        CkGui.AttachToolTip("Opens the Config Folder.--NL--(Useful for debugging)");
    }

    private bool FancyButtonCentered(Vector2 size, FAI icon, string text, Vector2 borderSize, Vector2 shadowSize)
    {
        // Button Contents.
        var pressed = ImGui.InvisibleButton($"##OptionButton{text}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hoverAlpha = hovered ? BUTTON_HOVER_OPACITY : BUTTON_TRANSPARENCY;
        var activeAlpha = active ? BUTTON_ACTIVE_OPACITY : BUTTON_TRANSPARENCY;

        var iconWidth = ImUtf8.FrameHeight;
        var textSize = ImGui.CalcTextSize(text);
        var fullTextWidth = iconWidth + textSize.X;
        var iconStart = min + new Vector2((size.X - fullTextWidth) / 2f, (size.Y - textSize.Y) / 2f);
        var textStart = iconStart + new Vector2(iconWidth, 0);
        // Visuals.
        var drawList = ImGui.GetWindowDrawList();
        // The Outer Border drop shadow.
        drawList.AddRectFilled(min, max, CkGui.Color(0, 0, 0, BUTTON_TRANSPARENCY), 25f, ImDrawFlags.RoundCornersAll);
        // Inner Border, Greyish
        drawList.AddRectFilled(min + Vector2.One, max - Vector2.One, CkGui.Color(220, 220, 220, hoverAlpha), 25f, ImDrawFlags.RoundCornersAll);
        // The progress bar background
        drawList.AddRectFilled(min + borderSize + shadowSize, max - borderSize - shadowSize, CkGui.Color(0, 0, 0, activeAlpha), 25f, ImDrawFlags.RoundCornersAll);
        // Try draw font text.
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            drawList.OutlinedFont(icon.ToIconString(), iconStart, uint.MaxValue, CkGui.Color(53, 24, 39, 255), BUTTON_SHADOW_SIZE);
        drawList.OutlinedFont(text, textStart, uint.MaxValue, CkGui.Color(53, 24, 39, 255), BUTTON_SHADOW_SIZE);

        return pressed;
    }

    private bool FancyButton(Vector2 size, FAI icon, string text, Vector2 borderSize, Vector2 shadowSize)
    {
        // Button Contents.
        var pressed = ImGui.InvisibleButton($"##OptionButton{text}", size);
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemClicked();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        // Pre-Calculations.
        var iconWidth = ImUtf8.FrameHeight;
        var hoverAlpha = hovered ? BUTTON_HOVER_OPACITY : BUTTON_TRANSPARENCY;
        var activeAlpha = active ? BUTTON_ACTIVE_OPACITY : BUTTON_TRANSPARENCY;

        var iconStart = min + new Vector2(ImUtf8.ItemSpacing.X, (size.Y - ImUtf8.TextHeight) / 2f);
        var textStart = iconStart + new Vector2(iconWidth, 0);
        // Visuals.
        var drawList = ImGui.GetWindowDrawList();
        // The Outer Border drop shadow.
        drawList.AddRectFilled(min, max, CkGui.Color(0, 0, 0, BUTTON_TRANSPARENCY), 25f, ImDrawFlags.RoundCornersAll);
        // Inner Border, Greyish
        drawList.AddRectFilled(min + Vector2.One, max - Vector2.One, CkGui.Color(220, 220, 220, hoverAlpha), 25f, ImDrawFlags.RoundCornersAll);
        // The progress bar background
        drawList.AddRectFilled(min + borderSize + shadowSize, max - borderSize - shadowSize, CkGui.Color(0, 0, 0, activeAlpha), 25f, ImDrawFlags.RoundCornersAll);

        // Try draw font text.
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            drawList.OutlinedFont(icon.ToIconString(), iconStart, uint.MaxValue, CkGui.Color(53, 24, 39, 255), BUTTON_SHADOW_SIZE);
        drawList.OutlinedFont(text, textStart, uint.MaxValue, CkGui.Color(53, 24, 39, 255), BUTTON_SHADOW_SIZE);

        return pressed;
    }
}
