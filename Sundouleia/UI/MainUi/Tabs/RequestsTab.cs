using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Sundouleia.Gui.Components;

namespace Sundouleia.Gui.MainWindow;

// This is a placeholder UI structure for the sake of getting testing
// functional, and will be restructured later.
public class RequestsTab
{
    private DynamicRequestFolder _incomingFolder;
    private DynamicRequestFolder _pendingFolder;

    public RequestsTab(DrawEntityFactory factory)
    {

        // Create the folders.
        _incomingFolder = factory.CreateIncomingRequestsFolder();
        _pendingFolder = factory.CreateOutgoingRequestsFolder();
    }

    public DynamicRequestFolder Incoming => _incomingFolder;
    public DynamicRequestFolder Pending => _pendingFolder;

    public void DrawRequestsSection()
    {
        using var _ = CkRaii.Child("content", ImGui.GetContentRegionAvail(), wFlags: WFlags.NoScrollbar);

        _incomingFolder.DrawContents();
        _pendingFolder.DrawContents();
    }

    // Obviously there will be more to this, but can expand on it later.
}