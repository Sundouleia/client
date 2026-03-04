using CkCommons.Gui;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.Components;

public class LociTabs : IconTextTabBar<LociTabs.SelectedTab>
{
    public enum SelectedTab
    {
        Statuses,
        Presets,
        Managers,
        Settings,
        IpcTester,
    }

    public override SelectedTab TabSelection
    {
        get => base.TabSelection;
        set
        {
            _config.Current.CurLociTab = value;
            _config.Save();
            base.TabSelection = value;
        }
    }

    private readonly MainConfig _config;
    public LociTabs(MainConfig config)
    {
        _config = config;
        TabSelection = _config.Current.CurLociTab;

        AddDrawButton(FAI.TheaterMasks, "Statuses", SelectedTab.Statuses, "Your Loci Statuses");
        AddDrawButton(FAI.LayerGroup, "Presets", SelectedTab.Presets, "Your Loci Presets");
        AddDrawButton(FAI.Wrench, "Managers", SelectedTab.Managers, "Manage statuses on Actors");
        AddDrawButton(FAI.Cog, "Settings", SelectedTab.Settings, "Configurable Options for Loci");
        AddDrawButton(FAI.Flask, "Ipc Tester", SelectedTab.IpcTester, "Test the various IPC Methods for Loci");
    }

    public override void Draw(float availableWidth)
    {
        if (_tabButtons.Count == 0)
            return;

        using var color = ImRaii.PushColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new(0, 0, 0, 0)));
        var spacing = ImGui.GetStyle().ItemSpacing;
        var buttonX = (availableWidth - (spacing.X * (_tabButtons.Count - 1))) / _tabButtons.Count;
        var buttonY = CkGui.IconButtonSize(FontAwesomeIcon.Pause).Y;
        var buttonSize = new Vector2(buttonX, buttonY);
        var drawList = ImGui.GetWindowDrawList();
        var underlineColor = ImGui.GetColorU32(ImGuiCol.Separator);

        foreach (var tab in _tabButtons)
            DrawTabButton(tab, buttonSize, spacing, drawList);

        // advance to the new line and dispose of the button color.
        ImGui.NewLine();
        color.Dispose();

        ImGuiHelpers.ScaledDummy(3f);
        ImGui.Separator();
    }
}
