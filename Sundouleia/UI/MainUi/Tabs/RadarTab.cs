using CkCommons;
using CkCommons.Gui;
using Sundouleia.Services.Tutorial;

namespace Sundouleia.Gui.MainWindow;
public class RadarTab
{
    private readonly TutorialService _guides;
    public RadarTab(TutorialService guides) 
    {
        _guides = guides;

    }

    // Draws information about the current radar location, and the list of radar users in the area.
    // for each person in the list we can target if they are visible and in range, or just send a request.
    public void DrawRadarSection()
    {
        CkGui.CenterColorTextAligned("WIP", CkColor.CkMistressColor.Uint());
    }
}

