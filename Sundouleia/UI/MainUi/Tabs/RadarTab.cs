using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Services.Tutorial;
using Sundouleia.WebAPI;
using System.Collections.Immutable;

namespace Sundouleia.Gui.MainWindow;
public class RadarTab : DisposableMediatorSubscriberBase
{
    private readonly DrawEntityFactory _factory;
    private readonly SundesmoManager _sundesmos;
    private readonly RadarManager _manager;
    private readonly TutorialService _guides;

    private DynamicRadarFolder _pairedFolder;
    private DynamicRadarFolder _unpairedFolder;

    public RadarTab(ILogger<RadarTab> logger, SundouleiaMediator mediator, DrawEntityFactory factory,
        SundesmoManager sundesmos, RadarManager manager, TutorialService guides)
        : base(logger, mediator)
    {
        _factory = factory;
        _sundesmos = sundesmos;
        _manager = manager;
        _guides = guides;

        _pairedFolder = _factory.CreateRadarFolder(Constants.FolderTagRadarPaired, FolderOptions.Default);
        _unpairedFolder = _factory.CreateRadarFolder(Constants.FolderTagRadarUnpaired, FolderOptions.Default);
    }

    public void DrawSection()
    {
        var unverified = !MainHub.Reputation.IsVerified;
        var usageBlocked = !MainHub.Reputation.RadarUsage;
        // Otherwise, draw the blocked content body.
        var min = ImGui.GetCursorScreenPos();
        var max = min + ImGui.GetContentRegionAvail();

        if (!unverified && !usageBlocked)
        {
            DrawContentBody();
        }
        else
        {
            using (ImRaii.Disabled(usageBlocked || unverified))
                DrawContentBody();

            // Have to make a second child to overcome the conflicting z-ordering on text.
            ImGui.SetCursorScreenPos(min);
            using (ImRaii.Child("Overlays", ImGui.GetContentRegionAvail()))
            {
                // Draw warnings if we should.
                ImGui.GetWindowDrawList().AddRectFilledMultiColor(min, max, 0x77000000, 0x77000000, 0xAA000000, 0xAA000000);
                if (unverified)
                    DrawUnverifiedOverlay();
                else if (usageBlocked)
                    DrawRepBlockedOverlay();
            }
        }
    }

    private void DrawContentBody()
    {
        CkGui.FontTextCentered($"{RadarService.CurrWorldName} - {RadarService.CurrZoneName}", UiFontService.Default150Percent);
        // Draw paired first, then unpaired, (yes, this is done intentionally to help with things not being 'too automated')
        ImGui.Spacing();

        _pairedFolder.DrawContents();
        _unpairedFolder.DrawContents();
    }

    private void DrawUnverifiedOverlay()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y + CkGui.CalcFontTextSize("A", UiFontService.Default150Percent).Y * 2 + ImUtf8.TextHeight * 3 + ImUtf8.ItemSpacing.Y * 5;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Must Claim Account To Use!", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("For Moderation & Safety Reasons", UiFontService.Default150Percent);
        CkGui.FontTextCentered("Only Verified Users Get Social Features.", UiFontService.Default150Percent);
        ImGui.Spacing();
        CkGui.CenterText("You can verify via Sundouleia's Discord Bot.");
        CkGui.CenterText("Verification is easy & doesn't interact with lodestone");
        CkGui.CenterText("or any other SE properties.");
    }

    private void DrawRepBlockedOverlay()
    {
        var errorHeight = CkGui.CalcFontTextSize("A", UiFontService.UidFont).Y * 2 + CkGui.CalcFontTextSize("A", UiFontService.Default150Percent).Y + ImUtf8.ItemSpacing.Y * 2;
        var centerDrawHeight = (ImGui.GetContentRegionAvail().Y - ImUtf8.FrameHeightSpacing - errorHeight) / 2;

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + centerDrawHeight);
        CkGui.FontTextCentered("Blocked Via Bad Reputation!", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered("Cannot Use This Anymore", UiFontService.UidFont, ImGuiColors.DalamudRed);
        CkGui.FontTextCentered($"You have [{MainHub.Reputation.ChatStrikes}] radar chat strikes.", UiFontService.Default150Percent, ImGuiColors.DalamudRed);
    }
}

