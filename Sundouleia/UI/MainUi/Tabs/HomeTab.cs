using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
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
        var buttonH = CkGui.GetFancyButtonHeight();
        // The threshold to draw 2 or 1 rows.
        var thresholdHeight = buttonH * 8 + ImUtf8.ItemSpacing.Y * 7;
        // if we draw compact (2 columns) or full (1 column)
        var showCompact = region.Y < thresholdHeight;
        // Finalized Height of the child.
        var finalHeight = buttonH * (showCompact ? 4 : 8) + ImUtf8.ItemSpacing.Y * (showCompact ? 3 : 7);

        if (showCompact)
            DrawCompactButtons(region);
        else
            DrawButtonList(region);
    }

    private void DrawCompactButtons(Vector2 region)
    {
        var buttonWidth = (region.X - ImUtf8.ItemInnerSpacing.X) / 2;
        using (ImRaii.Group())
        {
            if (CkGui.FancyButton(FAI.MagnifyingGlassChart, "Actor Analyzer", buttonWidth, false))
                _mediator.Publish(new UiToggleMessage(typeof(ActorOptimizerUI)));
            CkGui.AttachToolTip("Inspect data of owned actors!");

            if (CkGui.FancyButton(FAI.FolderTree, "SMA Manager", buttonWidth, false))
                _mediator.Publish(new UiToggleMessage(typeof(SMAManagerUI)));
            CkGui.AttachToolTip("Organize and manage --COL--Sundouleia Modular Actor--COL-- files.", ImGuiColors.DalamudOrange);

            if (CkGui.FancyButton(FAI.FileExport, "SMA Creator", buttonWidth, false))
                _mediator.Publish(new UiToggleMessage(typeof(SMACreatorUI)));
            CkGui.AttachToolTip("Create (Sundouleia Modular Actor) Base, Outfit, Item, & ItemPack files." +
                "--SEP----COL--For Privacy (forced customization), porting MCDF's is not supported.--COL--", ImGuiColors.DalamudOrange);

            if (CkGui.FancyButton(FAI.Trophy, "Achievements", buttonWidth, true))
            {
                // Something.
            }
            CkGui.AttachToolTip("View Achievement Progress & Rewards." +
                "--SEP--Still WIP, Coming in future updates.");
        }
        ImUtf8.SameLineInner();
        using (ImRaii.Group())
        {
            if (CkGui.FancyButton(FAI.Cog, "Open Settings", buttonWidth, false))
                _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
            CkGui.AttachToolTip("Opens the Settings UI.");

            if (CkGui.FancyButton(FAI.Bell, "Events Viewer", buttonWidth, false))
                _mediator.Publish(new UiToggleMessage(typeof(DataEventsUI)));
            CkGui.AttachToolTip("View fired events for pair updates.");

            if (CkGui.FancyButton(FAI.Coffee, "Support Sundouleia", buttonWidth, false))
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/cw/Sundouleia", UseShellExecute = true }); }
                catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the Patreon link. {e.Message}"); }
            }
            CkGui.AttachToolTip("If you like my work, you can toss any support here ♥");

            if (CkGui.FancyButton(FAI.Wrench, "Open Config", buttonWidth, false))
            {
                try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.SundouleiaDirectory, UseShellExecute = true }); }
                catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
            }
            CkGui.AttachToolTip("Opens the Config Folder.--NL--(Useful for debugging)");
        }
    }

    private void DrawButtonList(Vector2 region)
    {
        if (CkGui.FancyButton(FAI.MagnifyingGlassChart, "Actor Analyzer", region.X, false))
            _mediator.Publish(new UiToggleMessage(typeof(ChangelogUI)));
        CkGui.AttachToolTip("Inspect data of owned actors!");

        if (CkGui.FancyButton(FAI.FolderTree, "SMA Manager", region.X, false))
            _mediator.Publish(new UiToggleMessage(typeof(SMAManagerUI)));
        CkGui.AttachToolTip("Organize and manage --COL--Sundouleia Modular Actor--COL-- files.", ImGuiColors.DalamudOrange);

        if (CkGui.FancyButton(FAI.FileExport, "SMA Creator", region.X, false))
            _mediator.Publish(new UiToggleMessage(typeof(SMACreatorUI)));
        CkGui.AttachToolTip("Create (Sundouleia Modular Actor) Base, Outfit, Item, & ItemPack files." +
            "--SEP----COL--For Privacy (forced customization), porting MCDF's is not supported.--COL--", ImGuiColors.DalamudOrange);

        if (CkGui.FancyButton(FAI.Trophy, "Achievements", region.X, true))
        {
            // Something.
        }
        CkGui.AttachToolTip("View Achievement Progress & Rewards.--SEP--Still WIP, Coming in future updates.");

        if (CkGui.FancyButton(FAI.Cog, "Open Settings", region.X, false))
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        CkGui.AttachToolTip("Opens the Settings UI.");

        if (CkGui.FancyButton(FAI.Bell, "Events Viewer", region.X, false))
            _mediator.Publish(new UiToggleMessage(typeof(DataEventsUI)));
        CkGui.AttachToolTip("View fired events for pair updates.");

        if (CkGui.FancyButton(FAI.Coffee, "Support Sundouleia", region.X, false))
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/cw/Sundouleia", UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the Patreon link. {e.Message}"); }
        }
        CkGui.AttachToolTip("If you like my work, you can toss any support here ♥");

        if (CkGui.FancyButton(FAI.Wrench, "Open Config", region.X, false))
        {
            try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.SundouleiaDirectory, UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
        }
        CkGui.AttachToolTip("Opens the Config Folder.--NL--(Useful for debugging)");
    }
}
