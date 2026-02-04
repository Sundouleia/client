using CkCommons.DrawSystem;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.MainWindow;

public class WhitelistTabs
{
    private readonly FolderConfig _config;
    private readonly WhitelistDrawer _defaults;
    private readonly BasicGroupsDrawer _basicGroups;
    private readonly GroupsDrawer _groups;

    public WhitelistTabs(FolderConfig config, WhitelistDrawer main,
        BasicGroupsDrawer basicGroups, GroupsDrawer groups)
    {
        _config = config;
        _defaults = main;
        _basicGroups = basicGroups;
        _groups = groups;
    }

    // Obviously will do more here to ensure that we can
    // blend the view of selected folders and unselected.
    // We can make use of this via a custom cache from
    // a seperate drawer that can display the folders only
    // we have marked for display.
    public void DrawBasicView()
    {
        var width = ImGui.GetContentRegionAvail().X;
        if(_defaults.DrawFilterRow(width, 64))
            _basicGroups.UpdateFilter(_defaults.SearchFilter);

        // Prefer to not need to do this if there is some better way but this does work for now.
        DrawBasicViewContents(width);
    }

    public void DrawGroupsView()
    {
        var width = ImGui.GetContentRegionAvail().X;
        _groups.DrawFilterRow(width, 64);
        _groups.DrawContents(width, DrawFlags);
    }

    // Prefer to not need to do this if there is some better way but this does work for now.
    private void DrawBasicViewContents(float width)
    {
        using var _ = ImRaii.Child("WhitelistContents", new Vector2(width, -1), false, WFlags.NoScrollbar);
        if (!_) return;

        ImGui.SetScrollX(0);
        using var s = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, Vector2.One);
        
        _basicGroups.DrawFoldersOnly(width);
        _defaults.DrawFoldersOnly(width);
    }

    private DynamicFlags DrawFlags 
        => _groups.OrganizerMode ? DynamicFlags.SelectableDragDrop : DynamicFlags.None;
}
