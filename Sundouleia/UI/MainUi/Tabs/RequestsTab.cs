using Dalamud.Bindings.ImGui;
using Sundouleia.DrawSystem;

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
        _drawer.DrawFullCache(width); // Need to update cache to draw single folders if possible.=
    }
}