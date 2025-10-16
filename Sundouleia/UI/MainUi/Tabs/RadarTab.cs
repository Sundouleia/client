using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Sundouleia.Radar;
using Sundouleia.Services;
using Sundouleia.Services.Tutorial;

namespace Sundouleia.Gui.MainWindow;
public class RadarTab
{
    private readonly RadarManager _manager;
    private readonly RadarService _service;
    private readonly TutorialService _guides;
    public RadarTab(RadarManager manager, RadarService service, TutorialService guides)
    {
        _manager = manager;
        _service = service;
        _guides = guides;
    }

    public void DrawSection()
    {
        CkGui.FontTextCentered($"{RadarService.CurrWorldName} Radar - Zone {RadarService.CurrZone}", UiFontService.Default150Percent);
        CkGui.ColorTextCentered($"{RadarService.CurrZoneName} | {_manager.AllUsers.Count} Others | {_manager.RenderedUsers.Count} Rendered", ImGuiColors.DalamudGrey2);
        ImGui.Separator();

        CkGui.TextWrapped("Below is just a rough outline, but will be a list of users by their anonymous " +
            "names, allowing you to send requests, if they permit it.");

        // Draw out the users here and stuff for requesting. Name them by Anonymous.
        foreach (var radarUser in _manager.AllUsers)
        {
            CkGui.FramedIconText(FAI.UserNinja);
            CkGui.TextFrameAlignedInline(radarUser.AnonymousName);
            CkGui.ColorTextInline($" (Valid: {radarUser.IsValid})", radarUser.IsValid ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
        }
    }
}

