using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;

namespace Sundouleia.Gui.Components;

internal class ReportPopupHandler : IPopupHandler
{
    private readonly MainHub _hub;
    private readonly SundesmoManager _sundesmos;
    private readonly ProfileService _profiles;

    private UserData _reportedUser = new("BlankUser");
    private string _reportedDisplayName = "User-XXX";
    private string _reportReason = DefaultReportReason;

    private const string DefaultReportReason = "Describe your report here...";

    public ReportPopupHandler(MainHub hub, SundesmoManager pairs, ProfileService profiles)
    {
        _hub = hub;
        _sundesmos = pairs;
        _profiles = profiles;

    }

    public Vector2 PopupSize => new(800, 450);
    public bool ShowClosed => false;
    public bool CloseHovered { get; set; } = false;
    public Vector2? WindowPadding => Vector2.Zero;
    public float? WindowRounding => 35f;

    public void DrawContent()
    {
        var drawList = ImGui.GetWindowDrawList();
        var rectMin = drawList.GetClipRectMin();
        var rectMax = drawList.GetClipRectMax();
        var PlateSize = rectMax - rectMin;
        var frameH = ImUtf8.FrameHeight;
        var outerPadding = Vector2.One * 12f;
        var borderSize = Vector2.One * 4;
        var pfpBorderPos = rectMin + outerPadding;
        var pfpBorderSize = Vector2.One * 200;
        var pfpPos = rectMin + Vector2.One * 16f;
        var pfpSize = Vector2.One * 192;
        var descPos = pfpBorderPos + new Vector2(0, pfpBorderSize.Y + outerPadding.Y);
        var descSize = pfpBorderSize with { Y = PlateSize.Y - outerPadding.Y * 3 - pfpBorderSize.Y };

        // grab our profile image and draw the baseline.
        var Profile = _profiles.GetProfile(_reportedUser);
        var pfpWrap = Profile.GetAvatarOrDefault();

        // draw out the background for the window.
        if (CosmeticService.CoreTextures.Cache[CoreTexture.ReportBg] is { } reportBG)
            drawList.AddDalamudImageRounded(reportBG, rectMin, PlateSize, 30f);
        // draw out the border on top of that.
        if (CosmeticService.CoreTextures.Cache[CoreTexture.ReportBorder] is { } reportBorder)
            drawList.AddDalamudImageRounded(reportBorder, rectMin, PlateSize, 20f);

        // Draw out the left group.
        using (ImRaii.Group())
        {
            drawList.AddDalamudImageRounded(pfpWrap, pfpPos, pfpSize, 96f, "The Image being Reported");
            // draw out the border for the profile picture
            if (CosmeticService.TryGetPlateBorder(PlateElement.Avatar, PlateBorder.Default, out var pfpBorder))
                drawList.AddDalamudImageRounded(pfpBorder, pfpBorderPos, pfpBorderSize, 96f);

            // Close Button
            var btnPos = rectMin + Vector2.One * 16;
            var btnSize = Vector2.One * 20;
            var closeButtonColor = CloseHovered ? ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)) : ImGui.GetColorU32(ImGuiColors.ParsedPink);
            drawList.AddLine(btnPos, btnPos + btnSize, closeButtonColor, 3);
            drawList.AddLine(new Vector2(btnPos.X + btnSize.X, btnPos.Y), new Vector2(btnPos.X, btnPos.Y + btnSize.Y), closeButtonColor, 3);
            ImGui.SetCursorScreenPos(btnPos);
            if (ImGui.InvisibleButton($"CloseButton##ProfileClose" + _reportedDisplayName, btnSize))
                ImGui.CloseCurrentPopup();
            CloseHovered = ImGui.IsItemHovered();

            // Below draw out the description.
            if (CosmeticService.TryGetPlateBorder(PlateElement.Description, PlateBorder.Default, out var descBorder))
                drawList.AddDalamudImageRounded(descBorder, descPos, descSize, 2f);
            // The text for it.
            ImGui.SetCursorScreenPos(descPos + borderSize);
            var desc = Profile.Info.Description;
            DrawLimitedDescription(desc, ImGuiColors.DalamudWhite, new Vector2(230, 185));
            CkGui.AttachToolTip("The Description being Reported");

            ImGui.SetCursorScreenPos(pfpBorderPos);
            ImGui.Dummy((descPos + descSize) - pfpBorderPos);
        }

        var reportBoxPos = pfpBorderPos with { X = pfpBorderPos.X + pfpBorderSize.X + ImUtf8.ItemSpacing.X };
        ImGui.SetCursorScreenPos(reportBoxPos);
        ImGui.Dummy(new Vector2(1 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y - outerPadding.Y));
        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(ImGuiCol.Border));
        ImGui.SameLine();

        using var rightChild = CkRaii.Child("ReportPlateRight", ImGui.GetContentRegionAvail() - outerPadding);
        using (ImRaii.Group())
        {
            using (var __ = CkRaii.Child("ReportBox", new(ImGui.GetContentRegionAvail().X, pfpBorderSize.Y)))
            {
                ImGui.InputTextMultiline("##reportReason", ref _reportReason, 500, new Vector2(__.InnerRegion.X / 2, __.InnerRegion.Y));

                ImGui.SameLine();
                using (ImRaii.Group())
                {
                    CkGui.ColorText("Profiles are reportable if they:", ImGuiColors.ParsedGold);
                    CkGui.TextWrapped("- Harass another player. Directly or Indirectly.");
                    CkGui.TextWrapped("- Impersonating another player.");
                    CkGui.TextWrapped("- Displays NSFW content without being marked for NSFW.");
                    CkGui.TextWrapped("- Used to share topics that dont belong here.");
                    ImGui.Spacing();
                    CkGui.ColorTextWrapped("Miss-use of reporting will result in your account being timed out.", ImGuiColors.DalamudRed);
                }
            }

            CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
            CkGui.FontTextWrapped("The Plugin Author has been victim of abuse in multiple forms. " +
                "As such, she will ensure her team does not allow predators to exploit this system " +
                "against you, and so you can feel safer using it.", UiFontService.Default150Percent, ImGuiColors.DalamudGrey);

            using var font = UiFontService.UidFont.Push();
            // Get the center of this screen.
            var disableButton = _reportReason.IsNullOrWhitespace() || string.Equals(_reportReason, DefaultReportReason, StringComparison.OrdinalIgnoreCase);
            var buttonSize = ImGuiHelpers.GetButtonSize($"Report {_reportedDisplayName} To Sundouleia");
            var buttonOffset = (ImGui.GetContentRegionAvail() - buttonSize) / 2;

            ImGui.SetCursorPos(ImGui.GetCursorPos() + buttonOffset);
            using (ImRaii.Disabled(disableButton))
            {
                if (ImGui.Button($"Report {_reportedDisplayName} To Sundouleia"))
                {
                    ImGui.CloseCurrentPopup();
                    var reason = _reportReason;
                    _ = _hub.UserReportProfile(new(_reportedUser, reason));
                }
            }
        }
    }

    private void DrawLimitedDescription(string desc, Vector4 color, Vector2 size)
    {
        // Calculate the line height and determine the max lines based on available height
        var lineHeight = ImGui.CalcTextSize("A").Y;
        var maxLines = (int)(size.Y / lineHeight);

        var currentLines = 1;
        var lineWidth = size.X; // Max width for each line
        var words = desc.Split(' '); // Split text by words
        var newDescText = "";
        var currentLine = "";

        foreach (var word in words)
        {
            // Try adding the current word to the line
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            var testLineWidth = ImGui.CalcTextSize(testLine).X;

            if (testLineWidth > lineWidth)
            {
                // Current word exceeds line width; finalize the current line
                newDescText += currentLine + "\n";
                currentLine = word;
                currentLines++;

                // Check if maxLines is reached and break if so
                if (currentLines >= maxLines)
                    break;
            }
            else
            {
                // Word fits in the current line; accumulate it
                currentLine = testLine;
            }
        }

        // Add any remaining text if we haven’t hit max lines
        if (currentLines < maxLines && !string.IsNullOrEmpty(currentLine))
        {
            newDescText += currentLine;
            currentLines++; // Increment the line count for the final line
        }

        CkGui.ColorTextWrapped(newDescText.TrimEnd(), color);
    }

    public void Open(OpenReportUIMessage msg)
    {
        _reportedUser = msg.UserToReport;
        _reportedDisplayName = _sundesmos.DirectPairs.Any(x => x.UserData.UID == _reportedUser.UID)
            ? _reportedUser.AliasOrUID
            : "User-" + _reportedUser.UID.Substring(_reportedUser.UID.Length - 4);
        _reportReason = DefaultReportReason;
    }
}
