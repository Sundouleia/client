using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Sundouleia.Gui.Components;
using Sundouleia.Loci;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui.Loci;

// Primary Loci UI servicing all interactions with the Loci Module.
public class LociUI : WindowMediatorSubscriberBase
{
    // Note that if you ever change this width you will need to also adjust the display width for the account page display.
    public const float LOCI_UI_WIDTH = 600f;

    private readonly LociTabs _tabMenu;
    private readonly MainConfig _config;
    private readonly StatusesTab _statusTab;
    private readonly PresetsTab _presetTab;
    private readonly LociManagersTab _managersTab;
    private readonly LociSettings _settingsTab;
    private readonly IpcTesterTab _ipcTab;

    public LociUI(ILogger<LociUI> logger, SundouleiaMediator mediator, LociTabs tabs,
        MainConfig config, StatusesTab statusTab, PresetsTab presetTab,
        LociManagersTab managersTab, LociSettings settingsTab, IpcTesterTab ipcTab)
        : base(logger, mediator, "Loci - Custom Status Control###Sundouleia_LociUI")
    {
        _tabMenu = tabs;
        _config = config;
        _statusTab = statusTab;
        _presetTab = presetTab;
        _managersTab = managersTab;
        _settingsTab = settingsTab;
        _ipcTab = ipcTab;


        this.PinningClickthroughFalse();
        this.SetBoundaries(new(800, 450), ImGui.GetIO().DisplaySize);        
        // Update the tab menu selection.
        _tabMenu.TabSelection = _config.Current.CurLociTab;
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        var width = CkGui.GetWindowContentRegionWidth();
        // Draw the tab bar ontop
        _tabMenu.Draw(width);

        using var _ = CkRaii.Child("selected", ImGui.GetContentRegionAvail());
        switch (_tabMenu.TabSelection)
        {
            case LociTabs.SelectedTab.Statuses:
                _statusTab.DrawSection(_.InnerRegion);
                break;
            case LociTabs.SelectedTab.Presets:
                _presetTab.DrawSection(_.InnerRegion);
                break;
            case LociTabs.SelectedTab.Managers:
                _managersTab.DrawSection(_.InnerRegion);
                break;
            case LociTabs.SelectedTab.Settings:
                _settingsTab.DrawSettings();
                break;
            case LociTabs.SelectedTab.IpcTester:
                _ipcTab.DrawSection();
                break;
        }
    }
}
