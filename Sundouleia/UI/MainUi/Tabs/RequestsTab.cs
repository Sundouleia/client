using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.MainWindow;

// Idk if we even need the tabs anymore lol.
public class RequestsTab
{
    private readonly FolderConfig _config;
    private readonly RequestsInDrawer _incoming;
    private readonly RequestsOutDrawer _outgoing;
    public RequestsTab(FolderConfig config, RequestsInDrawer incoming, RequestsOutDrawer outgoing)
    {
        _config = config;
        _incoming = incoming;
        _outgoing = outgoing;
    }

    public void DrawSection()
    {
        var width = ImGui.GetContentRegionAvail().X;
        if (_config.Current.ViewingIncoming)
        {
            _incoming.DrawFilterRow(width, 100);
            _incoming.DrawIncomingRequests(width, DynamicFlags.Selectable);
        }
        else
        {
            _outgoing.DrawFilterRow(width, 100);
            _outgoing.DrawPendingRequests(width, DynamicFlags.Selectable);
        }
    }
}