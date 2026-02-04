using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Textures;
using Sundouleia.WebAPI;
using SundouleiaAPI.Data;
using System.Globalization;

namespace Sundouleia.Gui.Profiles;

/// <summary>
///     Primary UI Draw helper for profile displays to have optimized 
///     calculations that respect global scaling. <para />
///     Separate from the UI class so that both the ProfileUI and 
///     the Popout ProfileUI can share the same code.
/// </summary>
public class ProfileHelper
{
    private readonly SundouleiaMediator _mediator;

    public ProfileHelper(SundouleiaMediator mediator)
    {
        _mediator = mediator;
    }

    public Vector2 RectMin { get; set; } = Vector2.Zero;
    public Vector2 RectMax { get; set; } = Vector2.Zero;
    private Vector2 PlateSize => RectMax - RectMin;
    public Vector2 CloseButtonPos => RectMin + ImGuiHelpers.ScaledVector2(21.25f);
    public Vector2 CloseButtonSize => ImGuiHelpers.ScaledVector2(27f);
    public Vector2 ReportButtonPos => RectMin + ImGuiHelpers.ScaledVector2(18.0625f, 223.125f);
    private Vector2 AvatarBorderPos => RectMin + new Vector2(PlateSize.X - AvatarBorderSize.X) / 2;
    private Vector2 AvatarBorderSize => ImGuiHelpers.ScaledVector2(254.25f);
    private Vector2 AvatarPos => RectMin + new Vector2(6.75f * ImGuiHelpers.GlobalScale + (PlateSize.X - AvatarBorderSize.X) / 2);
    public Vector2 AvatarSize => ImGuiHelpers.ScaledVector2(240.75f);
    private Vector2 SupporterIconBorderPos => RectMin + ImGuiHelpers.ScaledVector2(211.5f, 18f);
    private Vector2 SupporterIconBorderSize => ImGuiHelpers.ScaledVector2(58.5f);
    private Vector2 SupporterIconPos => RectMin + ImGuiHelpers.ScaledVector2(213.75f, 20.25f);
    private Vector2 SupporterIconSize => ImGuiHelpers.ScaledVector2(54f);
    private Vector2 DescriptionBorderPos => RectMin + ImGuiHelpers.ScaledVector2(13.5f, 385f);
    private Vector2 DescriptionBorderSize => ImGuiHelpers.ScaledVector2(261f, 177f);
    private Vector2 TitleLineStartPos => RectMin + ImGuiHelpers.ScaledVector2(13.5f, 345f);
    private Vector2 TitleLineSize => ImGuiHelpers.ScaledVector2(261f, 5.625f);
    private Vector2 StatsPos => RectMin + ImGuiHelpers.ScaledVector2(0, 358f);
    private Vector2 StatIconSize => ImGuiHelpers.ScaledVector2(22.5f);

    public void DrawProfile(ImDrawListPtr drawList, float rounding, Profile profile, string displayName, UserData userData, bool isPair)
    {
        DrawPlate(drawList, rounding, profile.Info, displayName);
        DrawProfilePic(drawList, profile, displayName, userData, isPair);
        DrawDescription(drawList, profile, userData, isPair);

        // Now let's draw out the chosen achievement Name.
        // (Hold onto this until we add achievements in and stuff)
        //using (UiFontService.SundouleiaLabelFont.Push())
        //{
        //    var titleName = ClientAchievements.GetTitleById(profile.Info.ChosenTitleId);
        //    var chosenTitleSize = ImGui.CalcTextSize(titleName);
        //    ImGui.SetCursorScreenPos(new Vector2(TitleLineStartPos.X + TitleLineSize.X / 2 - chosenTitleSize.X / 2, TitleLineStartPos.Y - chosenTitleSize.Y));
        //    // display it, it should be green if connected and red when not.
        //    ImGui.TextColored(ImGuiColors.ParsedGold, titleName);
        //}

        // Title Line Split, then the achievement states, join date, and report button.
        drawList.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.AchievementLineSplit], TitleLineStartPos, TitleLineSize);
        // Area for Achievement Stats, Joined Date, and Report button.
        DrawStats(drawList, profile.Info, displayName, userData, false);
    }

    public void DrawProfile(ImDrawListPtr drawList, float rounding, Profile profile, string displayName, UserData userData, bool isPair, ref bool hoveringReport)
    {
        DrawPlate(drawList, rounding, profile.Info, displayName);
        DrawProfilePic(drawList, profile, displayName, userData, isPair);
        DrawDescription(drawList, profile, userData, isPair);

        // Now let's draw out the chosen achievement Name.
        // (Hold onto this until we add achievements in and stuff)
        //using (UiFontService.SundouleiaLabelFont.Push())
        //{
        //    var titleName = ClientAchievements.GetTitleById(profile.Info.ChosenTitleId);
        //    var chosenTitleSize = ImGui.CalcTextSize(titleName);
        //    ImGui.SetCursorScreenPos(new Vector2(TitleLineStartPos.X + TitleLineSize.X / 2 - chosenTitleSize.X / 2, TitleLineStartPos.Y - chosenTitleSize.Y));
        //    // display it, it should be green if connected and red when not.
        //    ImGui.TextColored(ImGuiColors.ParsedGold, titleName);
        //}

        // Title Line Split, then the achievement states, join date, and report button.
        drawList.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.AchievementLineSplit], TitleLineStartPos, TitleLineSize);
        // Area for Achievement Stats, Joined Date, and Report button.
        DrawStats(drawList, profile.Info, displayName, userData, hoveringReport);
        hoveringReport = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

    }

    private void DrawPlate(ImDrawListPtr drawList, float rounding, ProfileContent info, string displayName)
    {
        // draw out the background for the window.
        if (CosmeticService.TryGetPlateBg(PlateElement.Plate, info.MainBG, out var plateBG))
            drawList.AddDalamudImageRounded(plateBG, RectMin, PlateSize, rounding);

        // draw out the border on top of that.
        if (CosmeticService.TryGetPlateBorder(PlateElement.Plate, info.MainBorder, out var plateBorder))
            drawList.AddDalamudImageRounded(plateBorder, RectMin, PlateSize, rounding);
    }

    private void DrawProfilePic(ImDrawListPtr drawList, Profile profile, string displayName, UserData userData, bool isPair)
    {
        // Determine what ImageWrap and tooltip we are showing based on some conditions.

        // Show reported profiles as normal if viewing own profile. (Prevent witch hunting)
        if (userData.UID == MainHub.UID)
            drawList.AddDalamudImageRounded(profile.GetAvatarOrDefault(), AvatarPos, AvatarSize, AvatarSize.Y / 2);
        // If a profile is public/non-public, flagged for review, or disabled, this is all handled server-side during the data fetch.
        else
            drawList.AddDalamudImageRounded(profile.GetAvatarOrDefault(), AvatarPos, AvatarSize, AvatarSize.Y / 2);

        // draw out the border for the profile picture
        if (CosmeticService.TryGetPlateBorder(PlateElement.Avatar, profile.Info.AvatarBorder, out var pfpBorder))
            drawList.AddDalamudImageRounded(pfpBorder, AvatarBorderPos, AvatarBorderSize, AvatarSize.Y / 2);

        // Draw out Supporter Icon Black BG base.
        drawList.AddCircleFilled(SupporterIconBorderPos + SupporterIconBorderSize / 2,
            SupporterIconBorderSize.X / 2, ImGui.GetColorU32(new Vector4(0, 0, 0, 1)));

        // Draw out Supporter Icon.
        var supporterInfo = CosmeticService.GetSupporterInfo(userData);
        if (supporterInfo.SupporterWrap is { } wrap)
            drawList.AddDalamudImageRounded(wrap, SupporterIconPos, SupporterIconSize, SupporterIconSize.Y / 2, $"{displayName} is Supporting Sundouleia!");

        // Draw out the border for the icon.
        drawList.AddCircle(SupporterIconBorderPos + SupporterIconBorderSize / 2, SupporterIconBorderSize.X / 2,
            ImGui.GetColorU32(ImGuiColors.ParsedPink), 0, 4f);


        // draw out the UID here. We must make it centered. To do this, we must fist calculate how to center it.
        var widthToCenterOn = AvatarBorderSize.X;
        using (UiFontService.UidFont.Push())
        {
            var aliasOrUidSize = ImGui.CalcTextSize(displayName);
            ImGui.SetCursorScreenPos(new Vector2(AvatarBorderPos.X + widthToCenterOn / 2 - aliasOrUidSize.X / 2, AvatarBorderPos.Y + AvatarBorderSize.Y + 5));
            // display it, it should be green if connected and red when not.
            ImGui.TextColored(ImGuiColors.ParsedPink, displayName);
        }
#if DEBUG
        CkGui.CopyableDisplayText(userData.UID);
#endif
    }

    private void DrawDescription(ImDrawListPtr drawList, Profile profile, UserData userData, bool isPair)
    {
        // draw out the description background.
        if (CosmeticService.TryGetPlateBg(PlateElement.Description, profile.Info.DescriptionBG, out var descBG))
            drawList.AddDalamudImageRounded(descBG, DescriptionBorderPos, DescriptionBorderSize, 2f);

        // description border
        if (CosmeticService.TryGetPlateBorder(PlateElement.Description, profile.Info.DescriptionBorder, out var descBorder))
            drawList.AddDalamudImageRounded(descBorder, DescriptionBorderPos, DescriptionBorderSize, 2f);

        // description overlay.
        if (CosmeticService.TryGetPlateOverlay(PlateElement.Description, profile.Info.DescriptionOverlay, out var descOverlay))
            drawList.AddDalamudImageRounded(descOverlay, DescriptionBorderPos, DescriptionBorderSize, 2f);

        // draw out the description text here.
        ImGui.SetCursorScreenPos(DescriptionBorderPos + ImGuiHelpers.ScaledVector2(12f, 8f));
        // Again, dont let reported people know they were reported.
        if (userData.UID == MainHub.UID)
        {
            // The user is us, and we are under review, show our picture.
            var description = profile.Info.Description.IsNullOrEmpty() ? "No Description Was Set.." : profile.Info.Description;
            var color = profile.Info.Description.IsNullOrEmpty() ? ImGuiColors.DalamudGrey2 : ImGuiColors.DalamudWhite;
            DrawLimitedDescription(description, color, DescriptionBorderSize - new Vector2(15, 0));
        }
        else
        {
            var description = string.IsNullOrEmpty(profile.Info.Description) ? "No Description Set." : profile.Info.Description;
            var color = profile.TempDisabled || (!isPair && !profile.Info.IsPublic) ? ImGuiColors.DalamudRed
                : string.IsNullOrEmpty(profile.Info.Description) ? ImGuiColors.DalamudGrey2 : ImGuiColors.DalamudWhite;
            DrawLimitedDescription(description, color, DescriptionBorderSize - new Vector2(15, 0));
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

    private void DrawStats(ImDrawListPtr drawList, ProfileContent info, string displayName, UserData userData, bool hoveringReport)
    {
        // jump down to where we should draw out the stats, and draw out the achievement icon.
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var statsPos = StatsPos;
        var formattedDate = userData.CreatedOn ?? DateTime.MinValue;
        string createdDate = formattedDate != DateTime.MinValue ? formattedDate.ToString("d", CultureInfo.CurrentCulture) : "MM-DD-YYYY";
        var dateWidth = ImGui.CalcTextSize(createdDate).X;
        // Placeholder until we have achievements.
        var achievementWidth = ImGui.CalcTextSize("0 / 0").X;
        var totalWidth = dateWidth + achievementWidth + StatIconSize.X * 3 + spacing * 3;

        statsPos.X += (PlateSize.X - totalWidth) / 2;
        drawList.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.Clock], statsPos, StatIconSize, ImGuiColors.ParsedGold);

        // set the cursor screen pos to the right of the clock, and draw out the joined date.
        statsPos.X += StatIconSize.X + 2f;
        ImGui.SetCursorScreenPos(statsPos);
        CkGui.ColorText(createdDate, ImGuiColors.ParsedGold);
        CkGui.AttachToolTip($"When {displayName} first joined Sundouleia.");

        statsPos.X += dateWidth + spacing;
        drawList.AddDalamudImage(CosmeticService.CoreTextures.Cache[CoreTexture.Achievement], statsPos, StatIconSize, ImGuiColors.ParsedGold);

        statsPos.X += StatIconSize.X + 2f;
        ImGui.SetCursorScreenPos(statsPos);
        CkGui.ColorText("0 / 0", ImGuiColors.ParsedGold);
        CkGui.AttachToolTip($"The total achievements {displayName} has earned.");

        statsPos.X += achievementWidth + spacing;
        statsPos.Y += 2f;
        ImGui.SetCursorScreenPos(statsPos);
        var color = hoveringReport && (KeyMonitor.CtrlPressed() && KeyMonitor.ShiftPressed())
            ? ImGui.GetColorU32(ImGuiColors.DalamudRed) 
            : hoveringReport ? ImGui.GetColorU32(ImGuiColors.DalamudGrey)
                             : ImGui.GetColorU32(ImGuiColors.DalamudGrey3);
        using (Svc.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
        {
            drawList.AddText(statsPos, color, FAI.Flag.ToIconString());
        }
        ImGui.SetCursorScreenPos(statsPos);
        using (ImRaii.Disabled(!KeyMonitor.CtrlPressed() || !KeyMonitor.ShiftPressed()))
        {
            if (ImGui.InvisibleButton($"ReportProfile##ReportProfile" + userData.UID, CloseButtonSize))
                _mediator.Publish(new OpenReportUIMessage(userData, ReportKind.Profile));

        }
        CkGui.AttachToolTip($"Report {displayName}'s Profile" +
            "--NL--Press CTRL+SHIFT to report." +
            "--SEP--(Opens Report Submission Window)");
    }
}
