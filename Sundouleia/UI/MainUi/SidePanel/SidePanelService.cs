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
    IncomingRequests,
    Interactions,
    GroupCreator,
    FolderCreator,
    GroupEditor,
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

// Idk build as we go, dont try to solve it all at once.
public class NewGroupCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.GroupCreator;

    private readonly ILogger _log;
    private readonly GroupsDrawSystem _dds;
    public NewGroupCache(ILogger log, GroupsDrawSystem dds)
    {
        _log = log;
        _dds = dds;
    }

    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => true;

    public IDynamicFolderGroup<Sundesmo>? ParentNode = null;
    public SundesmoGroup    NewGroup    { get; } = new SundesmoGroup();

    public bool IsGroupValid()
        => !string.IsNullOrWhiteSpace(NewGroup.Label) 
        && NewGroup.Icon is not FAI.None;

    public bool TryAddCreatedGroup()
    {
        if (!IsGroupValid())
        {
            _log.LogWarning("Tried to add invalid group, aborting.");
            return false;
        }

        return ParentNode is null
            ? _dds.AddNewGroup(NewGroup)
            : _dds.AddNewGroup(NewGroup, (DynamicFolderGroup<Sundesmo>)ParentNode);
    }
}

public class NewFolderGroupCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.FolderCreator;

    private readonly GroupsDrawSystem _dds;
    public NewFolderGroupCache(GroupsDrawSystem dds)
        => _dds = dds;

    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => true;

    public IDynamicFolderGroup<Sundesmo>? ParentNode = null;
    public string NewFolderName = string.Empty;
    public bool TryAddCreatedFolderGroup()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName))
            return false;

        return ParentNode is null
            ? _dds.AddNewFolderGroup(NewFolderName)
            : _dds.AddNewFolderGroup(NewFolderName, (DynamicFolderGroup<Sundesmo>)ParentNode);
    }
}

public class GroupEditorCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.GroupEditor;
    
    private readonly GroupsDrawer _drawer;
    
    private WhitelistCache _cache;
    public GroupEditorCache(GroupsDrawer drawer, WhitelistCache cache)
    {
        _drawer = drawer;
        _cache = cache;
    }

    public float DisplayWidth => 300 * ImGuiHelpers.GlobalScale;
    public bool IsValid => _cache.GroupInEditor is not null;

    public IDynamicFolderGroup<Sundesmo>? ParentNode => _cache.GroupInEditor?.Parent ?? null;
    public SundesmoGroup GroupInEditor => _cache.GroupInEditor!.Group;

    public void ChangeParentNode(IDynamicFolderGroup<Sundesmo>? newNode)
    {
        if (!IsValid) return;
        _cache.ChangeParentNode(newNode);
    }

    public void UpdateStyle()
    {
        if (!IsValid) return;
        _cache.UpdateEditorGroupStyle();
    }

    public bool TryRenameNode(GroupsManager groups, string newName)
        => IsValid && _cache.TryRenameNode(groups, newName);
}

public class ResponseCache : ISidePanelCache
{
    public SidePanelMode Mode => SidePanelMode.IncomingRequests;
    private DynamicSelections<RequestEntry>? _selections;
    public ResponseCache(DynamicSelections<RequestEntry> selections)
    {
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

            case SidePanelMode.Interactions:
                if (newTab is not MainMenuTabs.SelectedTab.BasicWhitelist)
                    ClearDisplay();
                return;
            case SidePanelMode.GroupCreator:
            case SidePanelMode.FolderCreator:
            case SidePanelMode.GroupEditor: // This one can be a bit iffy because 
                if (newTab is not MainMenuTabs.SelectedTab.GroupWhitelist)
                    ClearDisplay();
                return;

        }
    }

    public ISidePanelCache? DisplayCache
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value))
                return;

            if (field is IDisposable disposable)
                disposable.Dispose();

            field = value;
        }
    }

    public SidePanelMode DisplayMode => DisplayCache?.Mode ?? SidePanelMode.None;
    public bool CanDraw => DisplayCache?.IsValid ?? false;
    public float DisplayWidth => DisplayCache?.DisplayWidth ?? 250f;
    public void ClearDisplay()
    {
        // Before setting the display cache to null check if it is disposable, and if so, dispose it.
        if (DisplayCache is IDisposable disp)
            disp.Dispose();
        // Clear it.
        DisplayCache = null;
    }

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

    public void ForNewGroup(GroupsDrawSystem dds)
    {
        if (DisplayMode is SidePanelMode.GroupCreator)
            ClearDisplay();
        else
            DisplayCache = new NewGroupCache(Logger, dds);
    }

    public void ForNewFolderGroup(GroupsDrawSystem dds)
    {
        if (DisplayMode is SidePanelMode.FolderCreator)
            ClearDisplay();
        else
            DisplayCache = new NewFolderGroupCache(dds);
    }

    // Opens, or toggles, or swaps current data for interactions.
    public void ForGroupEditor(GroupFolder group, WhitelistCache cache, GroupsDrawer drawer)
    {
        // If the mode is already GroupEditorCache.
        if (DisplayCache is GroupEditorCache pairCache)
        {
            // If the sundesmo is the same, toggle off.
            if (cache.GroupInEditor == group)
            {
                Logger.LogDebug($"Closing GroupEditor for expanded group.");
                // Close it.
                ClearDisplay();
                cache.GroupInEditor = null;
            }
            // Update the displayed data to show the new sundesmo.
            else
            {
                Logger.LogDebug($"Switching GroupEditor to {group.Name}");
                cache.GroupInEditor = group;
            }
        }
        // Was displaying something else, so make sure we update and open.
        else
        {
            Logger.LogDebug($"Opening GroupEditor for {group.Name}");
            DisplayCache = new GroupEditorCache(drawer, cache);
            cache.GroupInEditor = group;
        }
    }

    /// <summary>
    ///     Set the side panel to display pending request selections.
    /// </summary>
    public void ForRequests(RequestCache cache, DynamicSelections<RequestEntry> selections)
    {
        if (DisplayMode is SidePanelMode.IncomingRequests)
            return;

        // Update the display cache.
        DisplayCache = new ResponseCache(selections);
    }
}