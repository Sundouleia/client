using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTab
{
    private readonly FolderConfig _config;
    private readonly WhitelistDrawer _defaultDrawer;
    private readonly GroupsDrawer _groupsDrawer;
    public WhitelistTab(FolderConfig config, WhitelistDrawer main, GroupsDrawer groups)
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
