using CkCommons;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Gui.MainWindow;

public enum SidePanelMode
{
    None,
    Interactions,
    GroupEditor,
    BulkResponding, // maybe add later idk.
}

public interface IStickyUICache
{
    SidePanelMode Mode { get; }

    // If we can draw this displayMode.
    bool IsValid { get; }

    // The width of the draw display.
    public float DisplayWidth { get; }

    // Invalidate all data.
    public void ClearData();

}

public class InteractionsCache : IStickyUICache
{
    public SidePanelMode Mode => SidePanelMode.Interactions;

    private Sundesmo? _sundesmo;

    public InteractionsCache(Sundesmo sundesmo)
        => UpdateSundesmo(sundesmo);

    public float DisplayWidth
        => (ImGui.CalcTextSize($"Preventing animations from {DisplayName}").X 
          + ImGui.GetFrameHeightWithSpacing()).AddWinPadX();

    public bool IsValid => _sundesmo is not null;
    public string DisplayName { get; private set; } = string.Empty;
    public Sundesmo? Sundesmo => _sundesmo;

    public void UpdateSundesmo(Sundesmo? sundesmo)
    {
        _sundesmo = sundesmo;
        DisplayName = _sundesmo?.GetNickAliasOrUid() ?? "Anon. User";
    }

    public void ClearData() 
        => UpdateSundesmo(null);
}

public class GroupOrganizerCache : IStickyUICache
{
    public SidePanelMode Mode => SidePanelMode.GroupEditor;
    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => true;
    public void ClearData() 
     { }
}

public sealed class StickyUIService : DisposableMediatorSubscriberBase
{
    private readonly MainMenuTabs _tabs;

    public StickyUIService(ILogger<StickyUIService> logger, SundouleiaMediator mediator, MainMenuTabs tabs)
        : base(logger, mediator)
    {
        _tabs = tabs;
        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearAllData());
        Mediator.Subscribe<MainWindowTabChangeMessage>(this, _ =>
        {
            switch (DisplayMode)
            {
                case SidePanelMode.BulkResponding:
                    if (_.NewTab is not MainMenuTabs.SelectedTab.Requests)
                        ClearAllData();
                    return;

                case SidePanelMode.GroupEditor:
                case SidePanelMode.Interactions:
                    if (_.NewTab is not MainMenuTabs.SelectedTab.Whitelist)
                        ClearAllData();
                    return;
            }
        });
    }

    public SidePanelMode DisplayMode { get; private set; } = SidePanelMode.None;
    public GroupOrganizerCache Organizer { get; private set; } = new();
    public InteractionsCache Interactions { get; private set; } = new(null!);


    public bool IsModeValid()
        => DisplayMode switch
        {
            SidePanelMode.GroupEditor => Organizer.IsValid,
            SidePanelMode.Interactions => Interactions.IsValid,
            _ => false,
        };

    public float DisplayWidth
        => DisplayMode switch
        {
            SidePanelMode.GroupEditor => Organizer.DisplayWidth,
            SidePanelMode.Interactions => Interactions.DisplayWidth,
            _ => 250f,
        };

    /// <summary>
    ///     Performs a full reset on all display data within the stickyUI.
    /// </summary>
    public void ClearAllData()
    {
        Organizer.ClearData();
        Interactions.ClearData();
        DisplayMode = SidePanelMode.None;
    }

    // Opens, or toggles, or swaps current data for interactions.
    public void ForInteractions(Sundesmo sundesmo, bool forceOpen = false)
    {
        // If the mode is already interactions.
        if (DisplayMode is SidePanelMode.Interactions)
        {
            // If the sundesmo is the same, toggle off.
            if (Interactions.Sundesmo == sundesmo)
            {
                // If we are forcing it open, do nothing.
                if (forceOpen)
                    return;
                // Otherwise clear the data.
                DisplayMode = SidePanelMode.None;
                Interactions.ClearData();
                return;
            }
            // Update the displayed data to show the new sundesmo.
            else
            {
                // Otherwise update the sundesmo.
                Interactions.UpdateSundesmo(sundesmo);
                return;
            }
        }
        // Was displaying something else, so make sure we update and open.
        else
        {
            DisplayMode = SidePanelMode.Interactions;
            Interactions.UpdateSundesmo(sundesmo);
        }
    }

    public void ForOrganizer()
    {
        // If the mode is already group editor, toggle off.
        if (DisplayMode is SidePanelMode.GroupEditor)
        {
            Organizer.ClearData();
            DisplayMode = SidePanelMode.None;
        }
        // Otherwise set to group editor.
        else
        {
            DisplayMode = SidePanelMode.GroupEditor;
        }
    }
}