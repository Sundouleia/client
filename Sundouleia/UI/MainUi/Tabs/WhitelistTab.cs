using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab
{
    private readonly FolderConfig _config;
    private readonly WhitelistDrawer _defaultDrawer;
    private readonly WhitelistGroupsDrawer _groupsDrawer;
    public WhitelistTab(FolderConfig config, WhitelistDrawer main, WhitelistGroupsDrawer groups)
    {
        _config = config;
        _defaultDrawer = main;
        _groupsDrawer = groups;
    }

    public void DrawSection()
    {
        var width = ImGui.GetContentRegionAvail().X;
        // The GroupsDrawer.
        if (_config.Current.ViewingGroups)
        {
            _groupsDrawer.DrawFilterRow(width, 64);
            _groupsDrawer.DrawContents(width);
        }
        // The BaseFoldersDrawer
        else
        {
            _defaultDrawer.DrawFilterRow(width, 64);
            _defaultDrawer.DrawContents(width);
        }
    }
}
