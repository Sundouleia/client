using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;

namespace Sundouleia.Gui.Components;

public class GroupEditorTabs : IconTextTabBar<GroupEditorTabs.SelectedTab>
{
    public enum SelectedTab
    {
        Attributes,
        Users,
    }

    public GroupEditorTabs()
    {
        AddDrawButton(FontAwesomeIcon.Tag, "Attributes", SelectedTab.Attributes, "Group Customizations");
        AddDrawButton(FontAwesomeIcon.Users, "Users", SelectedTab.Users, "Group User Management");

    }

    public override void Draw(float availableWidth)
    {
        if (_tabButtons.Count == 0)
            return;

        using var color = ImRaii.PushColor(ImGuiCol.Button, 0xFF000000);
        var spacing = ImUtf8.ItemSpacing;
        var buttonW = (availableWidth - (spacing.X * (_tabButtons.Count - 1))) / _tabButtons.Count;
        var buttonSize = new Vector2(buttonW, ImUtf8.FrameHeight);
        var wdl = ImGui.GetWindowDrawList();

        // Draw out the buttons, then newline after.
        foreach (var tab in _tabButtons)
            DrawTabButton(tab, buttonSize, spacing, wdl);

        // advance to the new line and dispose of the button color.
        ImGui.NewLine();
        ImGuiHelpers.ScaledDummy(3f);
        ImGui.Separator();
    }
}
