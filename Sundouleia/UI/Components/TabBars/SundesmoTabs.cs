using CkCommons.Gui;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.Components;

public class SundesmoTabs : IconTextTabBar<SundesmoTabs.SelectedTab>
{
    public enum SelectedTab
    {
        Interactions,
        Permissions,
    }

    public override SelectedTab TabSelection
    {
        get => base.TabSelection;
        set
        {
            _config.Current.CurInteractionsTab = value;
            _config.Save();
            base.TabSelection = value;
        }
    }

    private readonly MainConfig _config;
    public SundesmoTabs(MainConfig config)
    {
        _config = config;
        TabSelection = _config.Current.CurInteractionsTab;

        AddDrawButton(FontAwesomeIcon.PersonBurst, "Interactions", SelectedTab.Interactions, "Available interactions");
        AddDrawButton(FontAwesomeIcon.Binoculars, "Permissions", SelectedTab.Permissions, "View permissions set by both ends.");

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
