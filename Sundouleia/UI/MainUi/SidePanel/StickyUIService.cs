using CkCommons;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Sundouleia.DrawSystem;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using TerraFX.Interop.Windows;

namespace Sundouleia.Gui.MainWindow;

public enum SidePanelMode
{
    None,
    Interactions,
    GroupEditor,
    IncomingRequests,
    PendingRequests,
}

public interface IStickyUICache
{
    SidePanelMode Mode { get; }

    // If we can draw this displayMode. (Maybe remove this since it wont be necessary)
    bool IsValid { get; }

    // The width of the draw display.
    public float DisplayWidth { get; }
}

public class InteractionsCache : IStickyUICache
{
    public SidePanelMode Mode => SidePanelMode.Interactions;
    public InteractionsCache(Sundesmo sundesmo)
        => Sundesmo = sundesmo;

    public Sundesmo Sundesmo { get; private set; }
    public string DisplayName => Sundesmo.GetNickAliasOrUid();

    public bool IsValid => Sundesmo is not null;
    public float DisplayWidth
        => (ImGui.CalcTextSize($"Preventing animations from {DisplayName}").X
        + ImGui.GetFrameHeightWithSpacing()).AddWinPadX();

    public void UpdateSundesmo(Sundesmo sundesmo)
        => Sundesmo = sundesmo;
}

public class GroupOrganizerCache : IStickyUICache
{
    public SidePanelMode Mode => SidePanelMode.GroupEditor;
    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => true;
}

public class ResponseCache : IStickyUICache
{
    private RequestCache? _cache;
    private DynamicSelections<RequestEntry>? _selections;
    public ResponseCache(SidePanelMode mode, RequestCache cache, DynamicSelections<RequestEntry> selections)
    {
        Mode = mode;
        _cache = cache;
        _selections = selections;
    }

    public SidePanelMode Mode { get; init; }
    public IReadOnlyList<DynamicLeaf<RequestEntry>> Selected => _selections?.Leaves ?? [];
    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => _selections is not null && Selected.Count > 1;
}

public sealed class StickyUIService : DisposableMediatorSubscriberBase
{
    private readonly FolderConfig _config;
    public StickyUIService(ILogger<StickyUIService> logger, SundouleiaMediator mediator, FolderConfig config)
        : base(logger, mediator)
    {
        _config = config;
        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearDisplay());
        Mediator.Subscribe<MainWindowTabChangeMessage>(this, _ => UpdateForNewTab(_.NewTab));
    }

    private void UpdateForNewTab(MainMenuTabs.SelectedTab newTab)
    {
        switch (DisplayMode)
        {
            case SidePanelMode.PendingRequests:
            case SidePanelMode.IncomingRequests:
                if (newTab is not MainMenuTabs.SelectedTab.Requests)
                    ClearDisplay();
                return;

            case SidePanelMode.GroupEditor:
            case SidePanelMode.Interactions:
                if (newTab is not MainMenuTabs.SelectedTab.Whitelist)
                    ClearDisplay();
                return;
        }
    }

    public IStickyUICache? DisplayCache { get; private set; }

    public SidePanelMode DisplayMode => DisplayCache?.Mode ?? SidePanelMode.None;
    public bool CanDraw => DisplayCache?.IsValid ?? false;
    public float DisplayWidth => DisplayCache?.DisplayWidth ?? 250f;
    public void ClearDisplay()
        => DisplayCache = null;

    // Opens, or toggles, or swaps current data for interactions.
    public void ForInteractions(Sundesmo sundesmo, bool forceOpen = false)
    {
        // If the mode is already interactions.
        if (DisplayCache is InteractionsCache pairCache)
        {
            // If the sundesmo is the same, toggle off.
            if (pairCache.Sundesmo == sundesmo)
            {
                // If we are forcing it open, do nothing.
                if (forceOpen)
                    return;
                // Otherwise clear the data.
                ClearDisplay();
            }
            // Update the displayed data to show the new sundesmo.
            else
            {
                pairCache.UpdateSundesmo(sundesmo);
            }
        }
        // Was displaying something else, so make sure we update and open.
        else
        {
            DisplayCache = new InteractionsCache(sundesmo);
        }
    }

    public void ForOrganizer()
    {
        // If the mode is already group editor, toggle off.
        if (DisplayMode is SidePanelMode.GroupEditor)
            ClearDisplay();
        // Otherwise set to group editor.
        else
            DisplayCache = new GroupOrganizerCache();
    }

    /// <summary>
    ///     Set the side panel to display pending request selections.
    /// </summary>
    public void ForRequests(SidePanelMode requestKind, RequestCache cache, DynamicSelections<RequestEntry> selections)
    {
        if (requestKind == DisplayMode)
            return;

        if (requestKind is not (SidePanelMode.IncomingRequests or SidePanelMode.PendingRequests))
            throw new ArgumentException("Request kind must be IncomingRequests or PendingRequests.", nameof(requestKind));

        // Update the display cache.
        DisplayCache = new ResponseCache(requestKind, cache, selections);
    }
}