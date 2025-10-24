using CkCommons;
using Dalamud.Interface.ImGuiNotification;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.Interop;

// Do not use a key here as we are setting temporary collections not temporary mods.
public class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller
{
    private int API_CurrentMajor = 0;
    private int API_CurrentMinor = 0;
    private const int RequiredBreakingVersion = 5;
    private const int RequiredFeatureVersion = 12;

    private const string SUNDOULEIA_ID = "Sundouleia";
    private const string SUNDOULEIA_MOD_PREFIX = "Sundesmo_ModFiles";
    private const string SUNDOULEIA_META_MANIP_NAME = "Sundouleia_Meta";

    private bool _shownPenumbraUnavailable = false; // safety net to prevent notification spam.

    // Should probably plug this into a monitor and update it as things change.
    public static string? ModDirectory { get; private set; } = null;

    // API Version
    private ApiVersion Version;
    // API Events
    private readonly EventSubscriber                                OnInitialized;
    private readonly EventSubscriber                                OnDisposed;
    public EventSubscriber<nint, int>                               OnObjectRedrawn;
    public EventSubscriber<nint, string, string>                    OnObjectResourcePathResolved;
    public EventSubscriber<ModSettingChange, Guid, string, bool>    OnModSettingsChanged;
    // API Getters
    private GetModDirectory             GetModDirectory;       // Retrieves the root mod directory path.
    private GetPlayerMetaManipulations  GetMetaManipulations;  // Obtains the client's mod metadata manipulations.
    private GetPlayerResourcePaths      GetPlayerResourcePaths;// Obtain all client's player, mount/minion, pet, and companion resource paths.
    private GetGameObjectResourcePaths  GetObjectResourcePaths;

    // API Enactors
    private CreateTemporaryCollection   CreateTempCollection;
    private AssignTemporaryCollection   AssignTempCollection;
    private DeleteTemporaryCollection   DeleteTempCollection;
    private AddTemporaryMod             AddTempMod;
    private RemoveTemporaryMod          RemoveTempMod;

    private RedrawObject                RedrawObject;               // Can force the client to Redraw.
    private ResolvePlayerPathsAsync     ResolveOnScreenActorPaths;

    // Would like to hopefully have a penumbra update that would allow us to get the effective changes
    // on an actor based on its on-screen state, and for us to know when to get the effective changes and applied changes.
    // or just something to tell us when the state changed idk, will have to look into later.

    // Ultimately, the most ideal workflow would be something like:
    // -> Changes are made to something (could be equip slot or mod setting change)
    // -> We get notified by Penumbra that an on-screen actor was affected by a change.
    // -> Grab the effective changes in respect to that game object's on-screen state.
    // -> Compare that against Sundouleia's internal cache of on-screen paths, if any are different, start upload process
    // and reapply the actor.
    // -> This way by the time it is done uploading everything our other items should have had time to build and can upload in unison.

    private readonly CharaObjectWatcher _watcher;

    public IpcCallerPenumbra(ILogger<IpcCallerPenumbra> logger, SundouleiaMediator mediator,
        CharaObjectWatcher watcher) : base(logger, mediator)
    {
        _watcher = watcher;

        OnInitialized = Initialized.Subscriber(Svc.PluginInterface, () =>
        {
            APIAvailable = true;
            CheckModDirectory();
            Mediator.Publish(new PenumbraInitialized());
            // maybe redraw here but i see no reason to lol.
        });
        OnDisposed = Disposed.Subscriber(Svc.PluginInterface, () =>
        {
            APIAvailable = false;
            Mediator.Publish(new PenumbraDisposed());
        });

        // API Version.
        Version = new ApiVersion(Svc.PluginInterface);
        // Getters
        GetMetaManipulations = new GetPlayerMetaManipulations(Svc.PluginInterface);
        GetModDirectory = new GetModDirectory(Svc.PluginInterface);
        GetPlayerResourcePaths = new GetPlayerResourcePaths(Svc.PluginInterface);
        GetObjectResourcePaths = new GetGameObjectResourcePaths(Svc.PluginInterface);
        // Enactors
        CreateTempCollection = new CreateTemporaryCollection(Svc.PluginInterface);
        AssignTempCollection = new AssignTemporaryCollection(Svc.PluginInterface);
        DeleteTempCollection = new DeleteTemporaryCollection(Svc.PluginInterface);
        AddTempMod = new AddTemporaryMod(Svc.PluginInterface);
        RemoveTempMod = new RemoveTemporaryMod(Svc.PluginInterface);

        RedrawObject = new RedrawObject(Svc.PluginInterface);
        ResolveOnScreenActorPaths = new ResolvePlayerPathsAsync(Svc.PluginInterface);

        CheckAPI();
        CheckModDirectory();
        // Maybe add a OnLogin mediator here to re-check api when logging into alts or whatever.
    }

    public static bool APIAvailable { get; private set; } = false;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        OnDisposed.Dispose();
        OnInitialized.Dispose();
    }

    public void CheckAPI()
    {
        try
        {
            (API_CurrentMajor, API_CurrentMinor) = Version.Invoke();
        }
        catch
        {
            API_CurrentMajor = 0; API_CurrentMinor = 0;
        }

        // State in which version is invalid.
        APIAvailable = (API_CurrentMajor != RequiredBreakingVersion || API_CurrentMinor < RequiredFeatureVersion) ? false : true;

        // the penumbra unavailable flag
        _shownPenumbraUnavailable = _shownPenumbraUnavailable && !APIAvailable;

        if (!APIAvailable && !_shownPenumbraUnavailable)
        {
            _shownPenumbraUnavailable = true;
            Logger.LogError($"Invalid Version {API_CurrentMajor}.{API_CurrentMinor:D4}, required major " +
                $"Version {RequiredBreakingVersion} with feature greater or equal to {RequiredFeatureVersion}.");
            Mediator.Publish(new NotificationMessage("Penumbra inactive", "Features using Penumbra will not function properly.", NotificationType.Error));
        }
    }

    public void CheckModDirectory()
    {
        var value = !APIAvailable ? string.Empty : GetModDirectory!.Invoke().ToLowerInvariant();
        if (!string.Equals(ModDirectory, value, StringComparison.Ordinal))
        {
            ModDirectory = value;
            Mediator.Publish(new PenumbraDirectoryChanged(ModDirectory));
        }
    }

    /// <summary> 
    ///     Redraws the actor at <paramref name="objectIdx"/>.
    /// </summary>
    public void RedrawGameObject(ushort objectIdx)
    {
        if (!APIAvailable) return;
        Logger.LogTrace($"Redrawing actor at ObjectIdx [{objectIdx}]", LoggerType.IpcPenumbra);
        RedrawObject.Invoke(objectIdx, RedrawType.Redraw);
    }
    
    /// <summary>
    ///     Any metadata manipulations applied by mods condensed into a nice base64 string.
    /// </summary>
    public string GetMetaManipulationsString()
        => APIAvailable ? GetMetaManipulations.Invoke() : string.Empty;

    /// <summary>
    ///     Similar to <see cref="GetCharacterResourcePathData(ushort)"/>, but for all on-screen actors. <para />
    ///     Assumes you know the object indexes of the currently owned on-screen actors.
    /// </summary>
    public async Task<Dictionary<ushort, Dictionary<string, HashSet<string>>>> GetClientOnScreenResourcePaths()
    {
        if (!APIAvailable) return new Dictionary<ushort, Dictionary<string, HashSet<string>>>();
        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            Logger.LogDebug($"Calling: GetPlayerResourcePaths", LoggerType.IpcPenumbra);
            return GetPlayerResourcePaths.Invoke();
        }).ConfigureAwait(false);

    }

    /// <summary>
    ///     Get the game object resource path dictionary for the <paramref name="objIdx"/>. <para />
    ///     
    ///     Note that anything changed prior to the most recent redraw / glamourer's 
    ///     Auto-ReloadGear update will not be present here. <para />
    ///     
    ///     We use this over GetPlayerResourcePaths as we need to get the other objects sometimes.
    ///     Look into a better way at handling this down the line soon if possible.
    /// </summary>
    /// <returns> The ResourcePath dictionary reflecting the On-Screen state of an actor </returns>
    public async Task<Dictionary<string, HashSet<string>>?> GetCharacterResourcePathData(ushort objIdx)
    {
        if (!APIAvailable) return null;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            Logger.LogDebug($"Calling: GetGameObjectResourcePaths for ObjectIdx [{objIdx}]", LoggerType.IpcPenumbra);
            return GetObjectResourcePaths.Invoke(objIdx)[0];
        }).ConfigureAwait(false);
    }

    // Does the fancy mod resolving voodoo magic we partially want but doesn't quite give us what we want,
    // if that makes any sense lol.
    public async Task<(string[] forward, string[][] reverse)> ResolveModPaths(string[] forward, string[] reverse)
        => await ResolveOnScreenActorPaths.Invoke(forward, reverse).ConfigureAwait(false);

    /// <summary>
    ///     Creates a new temporary collection for one of our Sundesmo using their UID.
    /// </summary>
    /// <returns> 
    ///     The GUID of the created collection to use for later reference in 
    ///     assigning and manipulating the appended mods.
    /// </returns>
    public async Task<Guid> NewSundesmoCollection(string pairUid)
    {
        if (!APIAvailable) return Guid.Empty;
        var name = $"Sundesmo_{pairUid}_Collection";
        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (CreateTempCollection.Invoke(SUNDOULEIA_ID, name, out Guid id) is { } ret && ret is PenumbraApiEc.Success)
            {
                Logger.LogDebug($"New Temp {{{name}}} -> ID: {id}", LoggerType.IpcPenumbra);
                return id;
            }
            return Guid.Empty;
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Assigns a Temporary Collection to a visible Sundesmo that we identify
    ///     with their associated game object index.
    /// </summary>
    public async Task AssignSundesmoCollection(Guid id, int objIdx)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var ret = AssignTempCollection.Invoke(id, objIdx, true);
            Logger.LogTrace($"Assigning User Collection to {Svc.Objects[objIdx]?.Name ?? "UNK"}, Success: [{ret}] ({id})", LoggerType.IpcPenumbra);
            return ret;
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     In an ideal world we could efficiency and and remove changed items 
    ///     from a temporary mod, but penumbra's API does not currently allow this. <para />
    ///     As a result we must reapply everything in bulk. Which sucks, and means we must manage
    ///     the replacements internally with its own cache until they add support for this.
    /// </summary>
    public async Task ReapplySundesmoMods(Guid collection, Dictionary<string, string> modPaths)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            // remove the existing temporary mod
            var retRemove = RemoveTempMod.Invoke(SUNDOULEIA_MOD_PREFIX, collection, 0);
            Logger.LogTrace($"Removed Existing Temp Mod for Collection: {collection}, Success: [{retRemove}]", LoggerType.IpcPenumbra);
            // add the new temporary mod with the new paths.
            var retAdded = AddTempMod.Invoke(SUNDOULEIA_MOD_PREFIX, collection, modPaths, string.Empty, 0);
            Logger.LogTrace($"Added Temp Mod for Collection: {collection}, Success: [{retAdded}]", LoggerType.IpcPenumbra);
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Removes a Sundesmo's Temporary Collection via its GUID. <para />
    ///     It should be a given that they do not need to be visible for this to execute successfully.
    /// </summary>
    public async Task RemoveSundesmoCollection(Guid id)
    {
        if (!APIAvailable) return;
        Logger.LogDebug($"Removing Sundesmo Collection {{{id}}}", LoggerType.IpcPenumbra);
        var ret = await Svc.Framework.RunOnFrameworkThread(() => DeleteTempCollection.Invoke(id)).ConfigureAwait(false);
        Logger.LogDebug($"Sundesmo Collection {{{id}}} deleted. [RetCode: {ret}]", LoggerType.IpcPenumbra);
    }

    /// <summary>
    ///     Sets the Metadata Manipulation data for a Sundesmo's Temporary Collection. <para />
    ///     Adding a mod allows you to store the manipulations for it. But if instead we
    ///     compress all mod manipulations into a single mod with 0 replacements and all
    ///     meta manipulations, we end up with an easy to replace mod without making copies.
    /// </summary>
    public async Task SetSundesmoManipulations(Guid collection, string manipulationDataString)
    {
        if (!APIAvailable) return;

        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var retAdded = AddTempMod.Invoke(SUNDOULEIA_META_MANIP_NAME, collection, [], manipulationDataString, 0);
            Logger.LogTrace($"Manipulation Data updated for Sundesmo Collection {{{collection}}} [RetCode: {retAdded}]", LoggerType.IpcPenumbra);
        }).ConfigureAwait(false);
    }
}
