global using IPCMoodleAccessTuple = (// Maybe include ptr/objectIdx or not idk
    Sundouleia.Interop.MoodleAccess OtherAccess, long OtherMaxTime, 
    Sundouleia.Interop.MoodleAccess CallerAccess, long CallerMaxTime
);

global using MoodlesStatusInfo = (
    System.Guid GUID,
    int IconID,
    string Title,
    string Description,
    byte StatusType,        // Moodles StatusType enum, as a byte.
    string CustomVFXPath,   // What VFX to show on application.
    int Stacks,             // Usually 1 when no stacks are used.
    long ExpireTicksUTC,    // Permanent if -1, referred to as 'NoExpire' in MoodleStatus
    string Applier,         // Who applied the moodle. (Only relevent when updating active moodles)
    string Dispeller,       // When set, only this person can dispel your moodle.
    bool Permanent,         // Referred to as 'Sticky' in the Moodles UI
    System.Guid StatusOnDispell, // What status is applied upon the moodle being right-clicked off.
    bool ReapplyIncStacks,  // If stacks increase on reapplication.
    int StackIncCount       // How many stacks get added on each reapplication.
);

global using MoodlePresetInfo = (System.Guid GUID, System.Collections.Generic.List<System.Guid> Statuses, byte ApplyType, string Title);

using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs;
using Sundouleia.Watchers;
using TerraFX.Interop.Windows;
using Sundouleia.Services.Mediator;
using Sundouleia.ModularActorData;

namespace Sundouleia.Interop;

[Flags] // Defines access permissions for moodle application and removal on others.
public enum MoodleAccess : short
{
    None            = 0 << 0, // No Access
    AllowOwn        = 1 << 0, // The Access Owners own moodles can be applied.
    AllowOther      = 1 << 1, // The Access Owners 'other' / 'pair' can apply their moodles.
    Positive        = 1 << 2, // Positive Statuses can be applied.
    Negative        = 1 << 3, // Negative Statuses can be applied.
    Special         = 1 << 4, // Special Statuses can be applied.
    Permanent       = 1 << 5, // Moodles without a duration can be applied.
    RemoveApplied   = 1 << 6, // 'Other' can remove only moodles they have applied.
    RemoveAny       = 1 << 7, // 'Other' can remove any moodles.
    Clearing        = 1 << 8, // 'Other' can clear all moodles.
}



/// <summary>
/// The IPC Provider for Sundouleia to interact with other plugins by sharing information about visible players.
/// </summary>
public class IpcProvider : DisposableMediatorSubscriberBase, IHostedService
{
    private const int SundouleiaApiVersion = 1;

    private readonly ModularActorManager _actorFileManager;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    private readonly Dictionary<nint, IPCMoodleAccessTuple> _handledSundesmos = [];

    // Sundouleia's Personal IPC Events.
    private static ICallGateProvider<int>?    ApiVersion;       // FUNC 
    private static ICallGateProvider<object>? Ready;            // FUNC
    private static ICallGateProvider<object>? Disposing;        // FUNC
    // Events to handle knowing when the state of the list changes. (Simplify from sundesmo to pair for common understanding)
    private static ICallGateProvider<nint, object>? PairRendered;   // When a sundesmo becomes rendered.
    private static ICallGateProvider<nint, object>? PairUnrendered; // When a sundesmo is no longer rendered.
    private static ICallGateProvider<nint, object>? AccessUpdated;  // A rendered pair's access permissions changed.

    // IPC Getters (Could change to another thing besides pointers but idk)
    private ICallGateProvider<List<nint>>?                             GetAllRendered;     // Get rendered sundesmos pointers.
    private ICallGateProvider<Dictionary<nint, IPCMoodleAccessTuple>>? GetAllRenderedInfo; // Get rendered sundesmos & their access info) (could make list)
    private ICallGateProvider<nint, IPCMoodleAccessTuple>?             GetAccessInfo;      // Get a sundesmo's access info.
    // Modular Actor Data, Base, Outfit, Item, and ItemPack loaders.
    private ICallGateProvider<string, int, bool>?       LoadSmadFile;   // Load a ModularActorData File to the gameObjectIdx.
    private ICallGateProvider<string, int, bool>?       LoadSmabFile;   // Load a ModularActorBase File to the gameObjectIdx.
    private ICallGateProvider<string, int, bool>?       LoadSmaoFile;   // Load a ModularActorOutfit File to the gameObjectIdx.
    private ICallGateProvider<List<string>, int, bool>? LoadSmaoFiles;  // Load many ModularActorOutfit Files to the gameObjectIdx.
    private ICallGateProvider<string, int, bool>?       LoadSmaiFile;   // Load a ModularActorItem File to the gameObjectIdx.
    private ICallGateProvider<List<string>, int, bool>? LoadSmaiFiles;  // Load many ModularActorItem Files to the gameObjectIdx.
    private ICallGateProvider<string, int, bool>?       LoadSmaipFile;  // Load an ItemPack holding multiple items to the gameObjectIdx.
    // The above methods, but Async variants.
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmadFileAsync;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmabFileAsync;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaoFileAsync;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaoFilesAsync;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaiFileAsync;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaiFilesAsync;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaipFileAsync;

    // SMAD related validators.
    private ICallGateProvider<string, bool>? IsFileValid;       // Validates if a SMAD, SMAB, SMAO, SMAI, or SMAIP file is valid.
    private ICallGateProvider<string, bool>? IsUpdateFileValid; // Validates if an update token is valid for a given SMAD file.

    // IPC Event Actions (for Moodles)
    private static ICallGateProvider<MoodlesStatusInfo, object?>?               ApplyStatusInfo;        // Apply a moodle tuple to the client actor.
    private static ICallGateProvider<List<MoodlesStatusInfo>, object?>?         ApplyStatusInfoList;    // Apply moodle tuples to the client actor.

    public IpcProvider(ILogger<IpcProvider> logger, SundouleiaMediator mediator,
        ModularActorManager actorFileManager, SundesmoManager pairs, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _actorFileManager = actorFileManager;
        _sundesmos = pairs;
        _watcher = watcher;

        // Should subscribe to characterActorCreated or rendered / unrendered events.
        
        // these events would then trigger the respective plugin IPC.
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting Sundouleia IpcProvider");
        // init API
        ApiVersion = Svc.PluginInterface.GetIpcProvider<int>("Sundouleia.GetApiVersion");
        // init Events
        Ready = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Ready");
        Disposing = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Disposing");
        // init renderedList events.
        PairRendered = Svc.PluginInterface.GetIpcProvider<nint, object>("Sundouleia.PairRendered");
        PairUnrendered = Svc.PluginInterface.GetIpcProvider<nint, object>("Sundouleia.PairUnrendered");
        AccessUpdated = Svc.PluginInterface.GetIpcProvider<nint, object>("Sundouleia.AccessUpdated");

        // init Getters
        GetAllRendered = Svc.PluginInterface.GetIpcProvider<List<nint>>("Sundouleia.GetAllRendered");
        GetAllRenderedInfo = Svc.PluginInterface.GetIpcProvider<Dictionary<nint, IPCMoodleAccessTuple>>("Sundouleia.GetAllRenderedInfo");
        GetAccessInfo = Svc.PluginInterface.GetIpcProvider<nint, IPCMoodleAccessTuple>("Sundouleia.GetAccessInfo");
        IsFileValid = Svc.PluginInterface.GetIpcProvider<string, bool>("Sundouleia.IsFileValid");
        IsUpdateFileValid = Svc.PluginInterface.GetIpcProvider<string, bool>("Sundouleia.IsUpdateFileValid");

        // init loaders
        LoadSmadFile = Svc.PluginInterface.GetIpcProvider<string, int, bool>("Sundouleia.LoadSmadFile");
        LoadSmabFile = Svc.PluginInterface.GetIpcProvider<string, int, bool>("Sundouleia.LoadSmabFile");
        LoadSmaoFile = Svc.PluginInterface.GetIpcProvider<string, int, bool>("Sundouleia.LoadSmaoFile");
        LoadSmaoFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, bool>("Sundouleia.LoadSmaoFiles");
        LoadSmaiFile = Svc.PluginInterface.GetIpcProvider<string, int, bool>("Sundouleia.LoadSmaiFile");
        LoadSmaiFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, bool>("Sundouleia.LoadSmaiFiles");
        LoadSmaipFile = Svc.PluginInterface.GetIpcProvider<string, int, bool>("Sundouleia.LoadSmaipFile");
        
        // init async loaders
        LoadSmadFileAsync = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmadFileAsync");
        LoadSmabFileAsync = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmabFileAsync");
        LoadSmaoFileAsync = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaoFileAsync");
        LoadSmaoFilesAsync = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaoFilesAsync");
        LoadSmaiFileAsync = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaiFileAsync");
        LoadSmaiFilesAsync = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaiFilesAsync");
        LoadSmaipFileAsync = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaipFileAsync");

        // init appliers (Maybe replace with applied / removed / updated later)
        ApplyStatusInfo = Svc.PluginInterface.GetIpcProvider<MoodlesStatusInfo, object?>("Sundouleia.ApplyStatusInfo");
        ApplyStatusInfoList = Svc.PluginInterface.GetIpcProvider<List<MoodlesStatusInfo>, object?>("Sundouleia.ApplyStatusInfoList");

        // register api
        ApiVersion.RegisterFunc(() => SundouleiaApiVersion);
        // register getters
        GetAllRendered.RegisterFunc(() => _handledSundesmos.Keys.ToList());
        GetAllRenderedInfo.RegisterFunc(() => new Dictionary<nint, IPCMoodleAccessTuple>(_handledSundesmos));
        GetAccessInfo.RegisterFunc((address) => _handledSundesmos.TryGetValue(address, out var access) ? access : (MoodleAccess.None, 0, MoodleAccess.None, 0));
        // register loaders
        LoadSmadFile.RegisterFunc(LoadSMAD);

        Logger.LogInformation("Started IpcProviderService");
        NotifyReady();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("Stopping IpcProvider Service");
        NotifyDisposing();

        ApiVersion?.UnregisterFunc();
        Ready?.UnregisterFunc();
        Disposing?.UnregisterFunc();
        // unregister the event actions.
        PairRendered?.UnregisterAction();
        PairUnrendered?.UnregisterAction();
        // unregister the functions for getters.
        GetAllRendered?.UnregisterFunc();
        GetAllRenderedInfo?.UnregisterFunc();
        GetAccessInfo?.UnregisterFunc();
        // unregister loaders
        LoadSmadFile?.UnregisterFunc();
        LoadSmabFile?.UnregisterFunc();
        LoadSmaoFile?.UnregisterFunc();
        LoadSmaoFiles?.UnregisterFunc();
        LoadSmaiFile?.UnregisterFunc();
        LoadSmaiFiles?.UnregisterFunc();
        LoadSmaipFile?.UnregisterFunc();
        // unregister async loaders
        LoadSmadFileAsync?.UnregisterFunc();
        LoadSmabFileAsync?.UnregisterFunc();
        LoadSmaoFileAsync?.UnregisterFunc();
        LoadSmaoFilesAsync?.UnregisterFunc();
        LoadSmaiFileAsync?.UnregisterFunc();
        LoadSmaiFilesAsync?.UnregisterFunc();
        LoadSmaipFileAsync?.UnregisterFunc();
        // unregister validators
        IsFileValid?.UnregisterFunc();
        IsUpdateFileValid?.UnregisterFunc();
        return Task.CompletedTask;
    }

    private static void NotifyReady() => Ready?.SendMessage();
    private static void NotifyDisposing() => Disposing?.SendMessage();
    private static void NotifyPairRendered(nint pairPtr) => PairRendered?.SendMessage(pairPtr);
    private static void NotifyPairUnrendered(nint pairPtr) => PairUnrendered?.SendMessage(pairPtr);
    private static void NotifyAccessUpdated(nint pairPtr) => AccessUpdated?.SendMessage(pairPtr);

    /// <summary> Loads a SundouleiaModularActorData file onto the given object index. </summary>
    /// <returns> True if loaded successfully, false otherwise. </returns>
    /// <remarks> Only works in GPOSE. </remarks>
    private bool LoadSMAD(string path, int objectIdx)
        => false; // Not ready.
    
    private bool LoadSMAB(string path, int objectIdx)
        => false; // Not ready.

    private bool LoadSMAO(string path, int objectIdx)
        => false; // Not ready.

    private bool LoadSMAO(List<string> paths, int objectIdx)
        => false; // Not ready.

    private bool LoadSMAI(string path, int objectIdx)
        => false; // Not ready.

    private bool LoadSMAI(List<string> paths, int objectIdx)
        => false; // Not ready.

    private bool LoadSMAIP(string path, int objectIdx)
        => false; // Not ready.

    private async Task<bool> LoadSMADAsync(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMABAsync(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAOAsync(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAOAsync(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAIAsync(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAIAsync(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAIPAsync(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    // Validation.
    private bool ValidateFile(string path)
        => false; // Not ready.

    private bool ValidateUpdateFile(string path)
        => false; // Not ready.
}

