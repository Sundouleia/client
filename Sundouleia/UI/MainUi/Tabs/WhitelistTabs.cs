using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTabs
{
    private readonly FolderConfig _config;
    private readonly WhitelistDrawer _defaultDrawer;
    private readonly GroupsDrawer _groupsDrawer;

    public WhitelistTabs(FolderConfig config, WhitelistDrawer main, GroupsDrawer groups)
    {
        _config = config;
        _defaultDrawer = main;
        _groupsDrawer = groups;
    }

    // Obviously will do more here to ensure that we can
    // blend the view of selected folders and unselected.
    // We can make use of this via a custom cache from
    // a seperate drawer that can display the folders only
    // we have marked for display.
    public void DrawBasicView()
    {
        var width = ImGui.GetContentRegionAvail().X;
        _defaultDrawer.DrawFilterRow(width, 64);
        _defaultDrawer.DrawContents(width);
    }

    public void DrawGroupsView()
    {
        var width = ImGui.GetContentRegionAvail().X;
        _groupsDrawer.DrawFilterRow(width, 64);
        _groupsDrawer.DrawContents(width, DrawFlags);
    }

    private DynamicFlags DrawFlags 
        => _groupsDrawer.OrganizerMode ? DynamicFlags.SelectableDragDrop : DynamicFlags.None;
}
