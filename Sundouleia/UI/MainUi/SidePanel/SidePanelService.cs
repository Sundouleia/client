using CkCommons;
using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Sundouleia.CustomCombos;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Gui.MainWindow;

public enum SidePanelMode
{
    None,
    Interactions,
    GroupEditor,
    IncomingRequests,
}

public interface ISidePanelCache
{
    SidePanelMode Mode { get; }

    // If we can draw this displayMode. (Maybe remove this since it wont be necessary)
    bool IsValid { get; }

    // The width of the draw display.
    public float DisplayWidth { get; }
}

public class InteractionsCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.Interactions;

    // Stored internally for regenerators.
    private readonly ILogger _log;
    private readonly MainHub _hub;

    private OpenedInteraction _curOpened = OpenedInteraction.None;
    public InteractionsCache(ILogger log, MainHub hub, Sundesmo sundesmo)
    {
        _log = log;
        _hub = hub;

        Sundesmo    = sundesmo;
        OwnStatuses = new OwnStatusCombo(log, hub, Sundesmo, 1.3f);
        OwnPresets  = new OwnPresetCombo(log, hub, Sundesmo, 1.3f);
        Statuses    = new SundesmoStatusCombo(log, hub, Sundesmo, 1.3f);
        Presets     = new SundesmoPresetCombo(log, hub, Sundesmo, 1.3f);
        Remover     = new SundesmoStatusCombo(log, hub, Sundesmo, 1.3f, () =>
        {
            if (Sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveAny))
                return [.. Sundesmo.SharedData.DataInfoList.OrderBy(x => x.Title)];
            else if (Sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveApplied))
                return [.. Sundesmo.SharedData.DataInfoList.Where(x => x.Applier == PlayerData.NameWithWorld).OrderBy(x => x.Title)];
            else
                return [];
        });
    }

    public Sundesmo             Sundesmo    { get; private set; }
    public OwnStatusCombo       OwnStatuses { get; private set; }
    public OwnPresetCombo       OwnPresets  { get; private set; }
    public SundesmoStatusCombo  Statuses    { get; private set; }
    public SundesmoPresetCombo  Presets     { get; private set; }
    public SundesmoStatusCombo  Remover     { get; private set; }

    public OpenedInteraction OpenedInteraction => _curOpened;
    public string   DisplayName  => Sundesmo.GetNickAliasOrUid();
    public bool     IsValid      => Sundesmo is not null;
    public float    DisplayWidth => (ImGui.CalcTextSize($"Preventing applying their moodles {DisplayName}.").X + ImGui.GetFrameHeightWithSpacing()).AddWinPadX();

    public void ToggleInteraction(OpenedInteraction interaction)
        => _curOpened = (_curOpened == interaction) ? OpenedInteraction.None : interaction;

    public void UpdateSundesmo(Sundesmo sundesmo)
    {
        Sundesmo    = sundesmo;
        OwnStatuses = new OwnStatusCombo(_log, _hub, Sundesmo, 1.3f);
        OwnPresets  = new OwnPresetCombo(_log, _hub, Sundesmo, 1.3f);
        Statuses    = new SundesmoStatusCombo(_log, _hub, Sundesmo, 1.3f);
        Presets     = new SundesmoPresetCombo(_log, _hub, Sundesmo, 1.3f);
        Remover     = new SundesmoStatusCombo(_log, _hub, Sundesmo, 1.3f, () =>
        {
            if (Sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveAny))
                return [.. Sundesmo.SharedData.DataInfoList.OrderBy(x => x.Title)];
            else if (Sundesmo.PairPerms.MoodleAccess.HasAny(MoodleAccess.RemoveApplied))
                return [.. Sundesmo.SharedData.DataInfoList.Where(x => x.Applier == PlayerData.NameWithWorld).OrderBy(x => x.Title)];
            else
                return [];
        });
    }
}

public class GroupOrganizerCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.GroupEditor;
    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => true;
}

public class ResponseCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.IncomingRequests;
    private RequestCache? _cache;
    private DynamicSelections<RequestEntry>? _selections;
    public ResponseCache(RequestCache cache, DynamicSelections<RequestEntry> selections)
    {
        _cache = cache;
        _selections = selections;
    }

    public IReadOnlyList<DynamicLeaf<RequestEntry>> Selected => _selections?.Leaves ?? [];
    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => _selections is not null && Selected.Count > 0;
} 

public sealed class SidePanelService : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly FolderConfig _config;
    public SidePanelService(ILogger<SidePanelService> logger, SundouleiaMediator mediator,
        MainHub hub, FolderConfig config)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        Mediator.Subscribe<DisconnectedMessage>(this, _ => ClearDisplay());
        Mediator.Subscribe<MainWindowTabChangeMessage>(this, _ => UpdateForNewTab(_.NewTab));
        Mediator.Subscribe<OpenSundesmoSidePanel>(this, _ => ForInteractions(_.Sundesmo, _.ForceOpen));
    }

    private void UpdateForNewTab(MainMenuTabs.SelectedTab newTab)
    {
        switch (DisplayMode)
        {
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

    public ISidePanelCache? DisplayCache { get; private set; }

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
            DisplayCache = new InteractionsCache(Logger, _hub, sundesmo);
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
    public void ForRequests(RequestCache cache, DynamicSelections<RequestEntry> selections)
    {
        if (DisplayMode is SidePanelMode.IncomingRequests)
            return;

        // Update the display cache.
        DisplayCache = new ResponseCache(cache, selections);
    }
}