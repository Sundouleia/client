using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Gui.Profiles;
using Sundouleia.Services;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui.MainWindow;

public class AccountTab
{
    private readonly SundouleiaMediator _mediator;
    private readonly ProfileService _service;
    private readonly TutorialService _guides;

    public AccountTab(SundouleiaMediator mediator, ProfileService service, TutorialService guides)
    {
        _mediator = mediator;
        _service = service;
        _guides = guides;
    }

    private static Vector2 LastWinPos = Vector2.Zero;
    private static Vector2 LastWinSize = Vector2.Zero;

    public void DrawAccountSection()
    {
        // get the width of the window content region we set earlier
        var _windowContentWidth = CkGui.GetWindowContentRegionWidth();
        LastWinPos = ImGui.GetWindowPos();
        LastWinSize = ImGui.GetWindowSize();
        var _spacingX = ImGui.GetStyle().ItemSpacing.X;

        // make this whole thing a scrollable child window.
        // (keep the border because we will style it later and helps with alignment visual)
        using var c = CkRaii.Child("Account", new Vector2(CkGui.GetWindowContentRegionWidth(), 0), wFlags: WFlags.NoScrollbar);

        var profile = _service.GetProfile(MainHub.OwnUserData);
        var avatar = profile.GetAvatarOrDefault();
        var dispSize = new Vector2(180f);

        ImGui.Spacing();
        // Shift to center.
        CkGui.SetCursorXtoCenter(dispSize.X);
        var cursorPos = ImGui.GetCursorPos();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddDalamudImageRounded(avatar, pos, dispSize, 90f);
        ImGui.SetCursorPos(new Vector2(cursorPos.X, cursorPos.Y + dispSize.Y));

        // draw the UID header below this.
        DrawUIDHeader();
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.YourUID, LastWinPos, LastWinSize);

        // below this, draw a separator. (temp)
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.AlignTextToFramePadding();
        DrawAccountSettingChild(FAI.PenSquare, "My Profile", "Customize your Profile!", () => _mediator.Publish(new UiToggleMessage(typeof(ProfileEditorUI))));
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ProfileEditing, LastWinPos, LastWinSize,  () => _mediator.Publish(new UiToggleMessage(typeof(ProfileEditorUI))));
        
        ImGui.AlignTextToFramePadding();
        DrawAccountSettingChild(FAI.Cog, "My Settings", "Opens the Settings UI", () => _mediator.Publish(new UiToggleMessage(typeof(SettingsUi))));
        _guides.OpenTutorial(TutorialType.MainUi, StepsMainUi.ConfigSettings, LastWinPos, LastWinSize);
        
        // Actions Notifier thing.
        ImGui.AlignTextToFramePadding();
        DrawAccountSettingChild(FAI.Bell, "Events Viewer", "View what is being sent by your pairs.", () => _mediator.Publish(new UiToggleMessage(typeof(DataEventsUI))));
        
        // now do one for ko-fi
        ImGui.AlignTextToFramePadding();
        DrawAccountSettingChild(FAI.Pray, "Support via Patreon", "-If you like my work, you can toss any support here ♥", () =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://www.patreon.com/cw/Sundouleia", UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the Patreon link. {e.Message}"); }
        });

        ImGui.AlignTextToFramePadding();
        DrawAccountSettingChild(FAI.Wrench, "Open Configs", "Opens the Config Folder", () =>
        {
            try { Process.Start(new ProcessStartInfo { FileName = ConfigFileProvider.SundouleiaDirectory, UseShellExecute = true }); }
            catch (Bagagwa e) { Svc.Logger.Error($"Failed to open the config directory. {e.Message}"); }
        });
    }

    /// <summary>
    ///     Draws the UID header for the currently connected client (you)
    /// </summary>
    private void DrawUIDHeader()
    {
        // fetch the Uid Text of yourself
        var uidText = SundouleiaEx.GetUidText();

        // push the big boi font for the UID
        using (UiFontService.UidFont.Push())
        {
            var uidTextSize = ImGui.CalcTextSize(uidText);
            CkGui.SetCursorXtoCenter(uidTextSize.X);
            ImGui.TextColored(SundouleiaEx.UidColor(), uidText);
        }

        CkGui.CopyableDisplayText(MainHub.DisplayName);
        // if the UID does not equal the display name
        if (!string.Equals(MainHub.DisplayName, MainHub.UID, StringComparison.Ordinal))
        {
            CkGui.SetCursorXtoCenter(ImGui.CalcTextSize(MainHub.UID).X);
            ImGui.TextColored(SundouleiaEx.UidColor(), MainHub.UID);
            CkGui.CopyableDisplayText(MainHub.UID);
        }
    }

    private void DrawAccountSettingChild(FontAwesomeIcon leftIcon, string displayText, string hoverTT, Action buttonAction)
    {
        var height = 20f; // static height
        var textSize = ImGui.CalcTextSize(displayText);
        var iconSize = CkGui.IconSize(leftIcon);
        var arrowRightSize = CkGui.IconSize(FAI.ChevronRight);
        var textCenterY = ((height - textSize.Y) / 2);
        var iconFontCenterY = (height - iconSize.Y) / 2;
        var arrowRightCenterY = (height - arrowRightSize.Y) / 2;
        // text height == 17, padding on top and bottom == 2f, so 21f
        using (ImRaii.Child($"##DrawSetting{displayText + hoverTT}", new Vector2(CkGui.GetWindowContentRegionWidth(), height)))
        {
            // We love ImGui....
            var childStartYPos = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(childStartYPos + iconFontCenterY);
            CkGui.IconText(leftIcon);

            ImGui.SameLine(iconSize.X + ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(childStartYPos + textCenterY);
            ImGui.TextUnformatted(displayText);

            // Position the button on the same line, aligned to the right
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - arrowRightSize.X - ImGui.GetStyle().ItemSpacing.X);
            ImGui.SetCursorPosY(childStartYPos + arrowRightCenterY);
            // Draw the icon button and perform the action when pressed
            CkGui.IconText(FAI.ChevronRight);
        }

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            buttonAction.Invoke();
        CkGui.AttachToolTip(hoverTT);
    }
}
