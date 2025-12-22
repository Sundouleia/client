using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Hosting;
using Sundouleia.ModularActor;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;
using static CkCommons.Textures.MoodleDisplay;

namespace Sundouleia.Interop;

/// <summary>
/// The IPC Provider for Sundouleia to interact with other plugins by sharing information about visible players.
/// </summary>
public class IpcProvider : DisposableMediatorSubscriberBase, IHostedService
{
    private const int SundouleiaApiVersion = 1;

    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;

    // Could update to be playerHandler or Sundesmo, but it does make address lookup a bit more annoying.
    private readonly Dictionary<nint, ProviderMoodleAccessTuple> _handledSundesmos = [];

    // Sundouleia's Personal IPC Events.
    private static ICallGateProvider<int>?    ApiVersion;
    private static ICallGateProvider<object>? Ready;
    private static ICallGateProvider<object>? Disposing;

    // Events to handle knowing when the state of the list changes. (Simplify from sundesmo to pair for common understanding)
    private static ICallGateProvider<nint, object>? PairRendered;   // When a sundesmo becomes rendered.
    private static ICallGateProvider<nint, object>? PairUnrendered; // When a sundesmo is no longer rendered.
    private static ICallGateProvider<nint, object>? AccessUpdated;  // A rendered pair's access permissions changed.

    // IPC Getters (Could change to another thing besides pointers but idk)
    private ICallGateProvider<List<nint>>?                                  GetAllRendered;     // Get rendered sundesmos pointers.
    private ICallGateProvider<Dictionary<nint, ProviderMoodleAccessTuple>>? GetAllRenderedInfo; // Get rendered sundesmos & their access info) (could make list)
    private ICallGateProvider<nint, ProviderMoodleAccessTuple>?             GetAccessInfo;      // Get a sundesmo's access info.
    // Modular Actor Data, Base, Outfit, Item, and ItemPack loaders.
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmadFile;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmabFile;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaoFile;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaoFiles;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaiFile;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaiFiles;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaipFile;

    // SMAD related validators.
    private ICallGateProvider<string, Task<bool>>? IsFileValid;       // Validates if a SMAD, SMAB, SMAO, SMAI, or SMAIP file is valid.
    private ICallGateProvider<string, Task<bool>>? IsUpdateFileValid; // Validates if an update token is valid for a given SMAD file.

    // IPC Event Actions (for Moodles)
    private static ICallGateProvider<MoodlesStatusInfo, object?>?       ApplyStatusInfo;    // Apply a moodle tuple to the client actor.
    private static ICallGateProvider<List<MoodlesStatusInfo>, object?>? ApplyStatusInfoList;// Apply moodle tuples to the client actor.
    
    /// <summary>
    ///     Obtains the request to apply Moodles onto another Pair. <para />
    ///     If valid permissions, invokes a server call to request the action on the other pair.
    /// </summary>
    private ICallGateProvider<nint, List<MoodlesStatusInfo>, bool, object?>? ApplyToPairRequest;

    public IpcProvider(ILogger<IpcProvider> logger, SundouleiaMediator mediator,
        SundesmoManager pairs, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _sundesmos = pairs;
        _watcher = watcher;

        // Should subscribe to characterActorCreated or rendered / unrendered events.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, _ =>
        {
            // Add to handled sundesmos.
            _handledSundesmos.TryAdd(_.Handler.Address, _.Sundesmo.ToAccessTuple().ToCallGate());
            NotifyPairRendered(_.Handler.Address);
        });

        Mediator.Subscribe<SundesmoPlayerUnrendered>(this, _ =>
        {
            // Remove from handled sundesmos.
            _handledSundesmos.Remove(_.Address, out var removed);
            NotifyPairUnrendered(_.Address);
        });

        Mediator.Subscribe<MoodleAccessPermsChanged>(this, _ =>
        {
            // Update the permission if they are rendered.
            if (!_.Sundesmo.IsRendered)
                return;
            // Update the access permissions.
            _handledSundesmos[_.Sundesmo.PlayerAddress] = _.Sundesmo.ToAccessTuple().ToCallGate();
            NotifyAccessUpdated(_.Sundesmo.PlayerAddress);
        });
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
        GetAllRenderedInfo = Svc.PluginInterface.GetIpcProvider<Dictionary<nint, ProviderMoodleAccessTuple>>("Sundouleia.GetAllRenderedInfo");
        GetAccessInfo = Svc.PluginInterface.GetIpcProvider<nint, ProviderMoodleAccessTuple>("Sundouleia.GetAccessInfo");
        IsFileValid = Svc.PluginInterface.GetIpcProvider<string, Task<bool>>("Sundouleia.IsFileValid");
        IsUpdateFileValid = Svc.PluginInterface.GetIpcProvider<string, Task<bool>>("Sundouleia.IsUpdateFileValid");

        // init Callable Actions
        LoadSmadFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmadFile");
        LoadSmabFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmabFile");
        LoadSmaoFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaoFile");
        LoadSmaoFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaoFiles");
        LoadSmaiFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaiFile");
        LoadSmaiFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaiFiles");
        LoadSmaipFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaipFile");

        // For Moodles pair application requests
        ApplyToPairRequest = Svc.PluginInterface.GetIpcProvider<nint, List<MoodlesStatusInfo>, bool, object?>("Sundouleia.ApplyToPairRequest");

        // init appliers (Maybe replace with applied / removed / updated later)
        ApplyStatusInfo = Svc.PluginInterface.GetIpcProvider<MoodlesStatusInfo, object?>("Sundouleia.ApplyStatusInfo");
        ApplyStatusInfoList = Svc.PluginInterface.GetIpcProvider<List<MoodlesStatusInfo>, object?>("Sundouleia.ApplyStatusInfoList");

        // register api
        ApiVersion.RegisterFunc(() => SundouleiaApiVersion);
        // register getters
        GetAllRendered.RegisterFunc(() => _handledSundesmos.Keys.ToList());
        GetAllRenderedInfo.RegisterFunc(() => new Dictionary<nint, ProviderMoodleAccessTuple>(_handledSundesmos));
        GetAccessInfo.RegisterFunc((address) => _handledSundesmos.TryGetValue(address, out var access) ? access : (0, 0, 0, 0));
        // register loaders
        LoadSmadFile.RegisterFunc(LoadSMAD);
        LoadSmabFile.RegisterFunc(LoadSMAB);
        LoadSmaoFile.RegisterFunc(LoadSMAO);
        LoadSmaoFiles.RegisterFunc(LoadSMAO);
        LoadSmaiFile.RegisterFunc(LoadSMAI);
        LoadSmaiFiles.RegisterFunc(LoadSMAI);
        LoadSmaipFile.RegisterFunc(LoadSMAIP);
        ApplyToPairRequest.RegisterAction(ProcessApplyToPairRequest);
        // register validators
        IsFileValid.RegisterFunc(ValidateFile);
        IsUpdateFileValid.RegisterFunc(ValidateUpdateFile);

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
        AccessUpdated?.UnregisterAction();
        ApplyToPairRequest?.UnregisterAction();
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
    private async Task<bool> LoadSMAD(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAB(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAO(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAO(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAI(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAI(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAIP(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    /// <summary>
    ///     Applies a <see cref="MoodlesStatusInfo"/> tuple to the CLIENT ONLY via Moodles. <para />
    ///     This helps account for trying on Moodle Presets, or applying the preset's StatusTuples. <para />
    ///     Method is invoked via GagSpeak's IpcProvider to prevent miss-use of bypassing permissions.
    /// </summary>
    public void ApplyStatusTuple(MoodlesStatusInfo status) => ApplyStatusInfo?.SendMessage(status);

    /// <summary>
    ///     Applies a group of <see cref="MoodlesStatusInfo"/> tuples to the CLIENT ONLY via Moodles. <para />
    ///     This helps account for trying on Moodle Presets, or applying the preset's StatusTuples. <para />
    ///     Method is invoked via GagSpeak's IpcProvider to prevent miss-use of bypassing permissions.
    /// </summary>
    public void ApplyStatusTuples(IEnumerable<MoodlesStatusInfo> statuses) => ApplyStatusInfoList?.SendMessage(statuses.ToList());

    // Used to ensure integrity before pushing update to the server.
    private void ProcessApplyToPairRequest(nint recipientAddr, List<MoodlesStatusInfo> toApply, bool isPreset)
    {
        // Identify the pair.
        if (_sundesmos.DirectPairs.FirstOrDefault(p => p.IsRendered && p.PlayerAddress == recipientAddr) is not { } pair)
            return;
        // Validate.
        foreach (var status in toApply)
            if (!IsStatusValid(status, out var errorMsg))
            {
                Logger.LogWarning(errorMsg);
                return;
            }
        // If valid, publish
        Mediator.Publish(new MoodlesApplyStatusToPair(new(pair.UserData, toApply)));

        bool IsStatusValid(MoodlesStatusInfo status, [NotNullWhen(false)] out string? error)
        {
            if (!pair.PairPerms.MoodleAccess.HasAny(MoodleAccess.AllowOther))
                return (error = "Attempted to apply to a pair without 'AllowOther' active.") is null;
            else if (!pair.PairPerms.MoodleAccess.HasAny(MoodleAccess.Positive))
                return (error = "Pair does not allow application of Moodles with positive status types.") is null;
            else if (!pair.PairPerms.MoodleAccess.HasAny(MoodleAccess.Negative))
                return (error = "Pair does not allow application of Moodles with negative status types.") is null;
            else if (!pair.PairPerms.MoodleAccess.HasAny(MoodleAccess.Special))
                return (error = "Pair does not allow application of Moodles with special status types.") is null;
            else if (!pair.PairPerms.MoodleAccess.HasAny(MoodleAccess.Permanent) && status.ExpireTicks == -1)
                return (error = "Pair does not allow application of permanent Moodles.") is null;
            else if (pair.PairPerms.MaxMoodleTime < TimeSpan.FromMilliseconds(status.ExpireTicks))
                return (error = "Moodle duration of requested Moodle was longer than the pair allows!") is null;

            return (error = null) is null;
        }
    }


    // Validation.
    private async Task<bool> ValidateFile(string path)
        => await Task.FromResult(false);

    private async Task<bool> ValidateUpdateFile(string path)
        => await Task.FromResult(false);
}

