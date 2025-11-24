using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;

namespace Sundouleia.Gui.MainWindow;

// Idk if we even need the tabs anymore lol.
public class RequestsTab
{
    private readonly RequestsDrawer _drawer;
    public RequestsTab(RequestsDrawer drawer)
    {
        _drawer = drawer;
    }
    public void DrawSection()
    {
        var width = ImGui.GetContentRegionAvail().X;
        _drawer.DrawFilterRow(width, 100);
        _drawer.DrawContents(width); // Need to update cache to draw single folders if possible.=
    }
}