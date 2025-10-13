using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using Sundouleia.WebAPI.Files;
using Sundouleia.WebAPI.Files.Models;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using System.Reflection;
using System.Windows.Forms;
using TerraFX.Interop.Windows;

namespace Sundouleia.Services;

/// <summary> 
///     Tracks when sundesmos go online/offline, and visible/invisible. <para />
///     Reliably tracks when offline/unrendered sundesmos are fully timed out or
///     experiencing a brief reconnection / timeout, to prevent continuously redrawing data. <para />
///     This additionally handles updates regarding when we send out changes to other sundesmos.
/// </summary>
public sealed class DistributionService : DisposableMediatorSubscriberBase
{
    // likely file sending somewhere in here.
    private readonly MainConfig _config;
    private readonly MainHub _hub;
    private readonly PlzNoCrashFrens _noCrashPlz;
    private readonly IpcManager _ipc;
    private readonly SundesmoManager _sundesmos;
    private readonly FileCacheManager _cacheManager;
    private readonly FileUploader _fileUploader;
    private readonly ModdedStateManager _transients;
    private readonly CharaObjectWatcher _watcher;

    // Task runs the distribution of our data to other sundesmos.
    // should always await the above task, if active, before firing.
    private readonly SemaphoreSlim _distributionLock = new(1, 1);
    private CancellationTokenSource _distributeDataCTS = new();
    private Task? _distributeDataTask;

    // Management for the task involving making an update to our latest data.
    // If this is ever processing, we should await it prior to distributing data.
    // This way we make sure that when we do distribute the data, it has the latest information.
    private readonly SemaphoreSlim _dataUpdateLock = new(1, 1);
    // Should only be modified while the dataUpdateLock is active.
    internal ClientDataCache LastCreatedData { get; private set; } = new();

    // Accessors for the ClientUpdateService
    public bool UpdatingData => _dataUpdateLock.CurrentCount is 0;
    public bool DistributingData => _distributionLock.CurrentCount is 0; // only use if we dont want to cancel distributions.

    public DistributionService(ILogger<DistributionService> logger, SundouleiaMediator mediator,
        MainConfig config, MainHub hub, PlzNoCrashFrens noCrashPlz, IpcManager ipc, SundesmoManager pairs,
        FileCacheManager cacheManager, ModdedStateManager transients, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _config = config;
        _hub = hub;
        _noCrashPlz = noCrashPlz;
        _ipc = ipc;
        _sundesmos = pairs;
        _cacheManager = cacheManager;
        _transients = transients;
        _watcher = watcher;

        // Process sundesmo state changes.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, msg => NewVisibleUsers.Add(msg.Handler.Sundesmo.UserData));
        Mediator.Subscribe<SundesmoEnteredLimbo>(this, msg => InLimbo.Add(msg.Sundesmo.UserData));
        Mediator.Subscribe<SundesmoLeftLimbo>(this, msg => InLimbo.Remove(msg.Sundesmo.UserData));
        // Process connections.
        Mediator.Subscribe<ConnectedMessage>(this, _ => OnHubConnected());
        Mediator.Subscribe<DisconnectedMessage>(this, _ => NewVisibleUsers.Clear());
        // Process Update Checking.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, _ => UpdateCheck());
    }

    public HashSet<UserData> NewVisibleUsers { get; private set; } = new();
    public HashSet<UserData> InLimbo { get; private set; } = new();

    public List<UserData> SundesmosForUpdatePush => _sundesmos.GetVisibleConnected().Except([.. InLimbo, .. NewVisibleUsers]).ToList();

    // Only entry point where we ignore timeout states.
    // If this gets abused through we can very easily add timeout functionality here too.
    private async void OnHubConnected()
    {
        Logger.LogInformation("Hub Reconnected!");
        Logger.LogDebug($"UID's in NewVisible are: {string.Join(", ", NewVisibleUsers.Select(x => x.AliasOrUID))}", LoggerType.DataDistributor);
        Logger.LogDebug($"UID's in Limbo are: {string.Join(", ", InLimbo.Select(x => x.AliasOrUID))}", LoggerType.DataDistributor);

        Logger.LogTrace("Awaiting _dataUpdateLock (OnHubConnected)", LoggerType.DataDistributor);
        await _dataUpdateLock.WaitAsync();
        Logger.LogTrace("Acquired _dataUpdateLock (OnHubConnected)", LoggerType.DataDistributor);
        try
        {
            LastCreatedData.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
            LastCreatedData.GlamourerState[OwnedObject.Player] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.CPlusState[OwnedObject.Player] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.Moodles = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
            LastCreatedData.GlamourerState[OwnedObject.MinionOrMount] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.CPlusState[OwnedObject.MinionOrMount] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.GlamourerState[OwnedObject.Pet] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.CPlusState[OwnedObject.Pet] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.GlamourerState[OwnedObject.Companion] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            LastCreatedData.CPlusState[OwnedObject.Companion] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            // Cache mods.
            LastCreatedData.ApplyNewModState(await _transients.CollectModdedState(CancellationToken.None).ConfigureAwait(false));

        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during OnHubConnected: {ex}");
        }
        finally
        {
            Logger.LogTrace("Releasing _dataUpdateLock (OnHubConnected)", LoggerType.DataDistributor);
            _dataUpdateLock.Release();
        }

        // Send off to all visible users after awaiting for any other distribution task to process.
        Logger.LogInformation("Distributing initial Ipc Cache to visible sundesmos.", LoggerType.DataDistributor);
        _distributeDataCTS = _distributeDataCTS.SafeCancelRecreate();
        _distributeDataTask = Task.Run(async () =>
        {
            var recipients = NewVisibleUsers.ToList();
            NewVisibleUsers.Clear();
            await ResendAll(recipients).ConfigureAwait(false);
        }, _distributeDataCTS.Token);
    }

    // Note that we are going to need some kind of logic for handling the edge cases where user A is receiving a new update and that 
    private void UpdateCheck()
    {
        if (NewVisibleUsers.Count is 0) return;
        // If we are zoning or not available, do not process any updates from us.
        if (PlayerData.IsZoning || !PlayerData.Available || !MainHub.IsConnected) return;
        // Do not process the task if we are currently updating our latest data.
        if (UpdatingData) return;
        // If we are already distributing data, do not start another distribution.
        if (_distributeDataTask is not null && !_distributeDataTask.IsCompleted) return;

        // Process a distribution of full data to the newly visible users and then clear the update.
        _distributeDataTask = Task.Run(async () =>
        {
            var recipients = NewVisibleUsers.ToList();
            NewVisibleUsers.Clear();
            await ResendAll(recipients).ConfigureAwait(false);
        }, _distributeDataCTS.Token);
    }

    /// <summary>
    ///     Upload any missing files not yet present on the hub. (We could optionally send a isUploading here but idk)
    /// </summary>
    private async Task UploadAndPushMissingMods(List<UserData> usersToPushDataTo, List<VerifiedModFile> filesNeedingUpload)
    {
        // Upload any missing files not yet present on the hub. (We could optionally send a isUploading here but idk)
        Logger.LogDebug($"Uploading {filesNeedingUpload.Count} missing files to server...", LoggerType.DataDistributor);

        var uploadedFiles = await _fileUploader.UploadFiles(filesNeedingUpload).ConfigureAwait(false);
        
        Logger.LogDebug($"Uploaded {uploadedFiles.Count}/{filesNeedingUpload.Count} missing files. If any failed, there are integrity issues with your cache!", LoggerType.DataDistributor);
        await _hub.UserPushIpcMods(new PushIpcMods(usersToPushDataTo, new(uploadedFiles, []))).ConfigureAwait(false);
    }

    #region Cache Updates
    private async Task ResendAll(List<UserData> recipients)
    {
        var modData = new ModUpdates([], []);
        var appearance = new VisualUpdate();
        try
        {
            // Apply new changes and store them for sending.
            Logger.LogTrace($"Awaiting _dataUpdateLock (ResendAll)", LoggerType.DataDistributor);
            await _dataUpdateLock.WaitAsync();
            Logger.LogTrace($"Acquired _dataUpdateLock (ResendAll)", LoggerType.DataDistributor);
            try
            {
                modData = LastCreatedData.ToModUpdates();
                appearance = LastCreatedData.ToVisualUpdate();
            }
            finally
            {
                Logger.LogTrace($"Releasing _dataUpdateLock (ResendAll)", LoggerType.DataDistributor);
                _dataUpdateLock.Release();
            }

            // Send off update.
            var res = await _hub.UserPushIpcFull(new(recipients, modData, appearance)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcFull to {recipients.Count} newly visible users. {res.Value?.Count ?? 0} Files needed uploading.", LoggerType.DataDistributor);

            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during ResendAll: {ex}");
        }
    }

    // if it is a full update we do not care about the changes,
    // only that the cache is updated prior to the change.
    public async Task UpdateAndSendAll(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        var recipients = SundesmosForUpdatePush;
        var modChanges = new ModUpdates([], []);
        var visualChanges = new VisualUpdate();
        try
        {
            // Apply new changes and store them for sending.
            Logger.LogTrace($"Awaiting _dataUpdateLock (UpdateAndSendAll)", LoggerType.DataDistributor);
            await _dataUpdateLock.WaitAsync();
            Logger.LogTrace($"Acquired _dataUpdateLock (UpdateAndSendAll)", LoggerType.DataDistributor);
            try
            {
                modChanges = await UpdateModsInternal(CancellationToken.None).ConfigureAwait(false);
                visualChanges = await UpdateVisualsInternal(newChanges).ConfigureAwait(false);
            }
            finally
            {
                Logger.LogTrace($"Releasing _dataUpdateLock (UpdateAndSendAll)", LoggerType.DataDistributor);
                _dataUpdateLock.Release();
            }

            // Void data with no changes.
            if (modChanges.FilesToAdd.Count is 0 && modChanges.HashesToRemove.Count is 0 && !visualChanges.HasData())
                return;

            // Data changed, clear limbo.
            InLimbo.Clear();

            // Send off update.
            var res = await _hub.UserPushIpcFull(new(recipients, modChanges, visualChanges)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcFull to {recipients.Count} users. {res.Value?.Count ?? 0} Files needed uploading.", LoggerType.DataDistributor);

            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateAndSendAll: {ex}");
        }
    }

    public async Task UpdateAndSendMods()
    {
        var recipients = SundesmosForUpdatePush;
        var modChanges = new ModUpdates([], []);
        try
        {
            Logger.LogTrace($"Awaiting _dataUpdateLock (UpdateAndSendMods)", LoggerType.DataDistributor);
            await _dataUpdateLock.WaitAsync();
            Logger.LogTrace($"Acquired _dataUpdateLock (UpdateAndSendMods)", LoggerType.DataDistributor);
            try
            {
                modChanges = await UpdateModsInternal(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Logger.LogTrace($"Releasing _dataUpdateLock (UpdateAndSendMods)", LoggerType.DataDistributor);
                _dataUpdateLock.Release();
            }

            // Void data with no changes.
            if (modChanges.FilesToAdd.Count is 0 && modChanges.HashesToRemove.Count is 0)
                return;

            // Data changed, clear limbo.
            InLimbo.Clear();

            // Send off update.
            var res = await _hub.UserPushIpcMods(new(recipients, modChanges)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcMods to {recipients.Count} users. {res.Value?.Count ?? 0} Files needed uploading.", LoggerType.DataDistributor);

            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateAndSendMods: {ex}");
        }
    }

    public async Task UpdateAndSendVisuals(Dictionary<OwnedObject, IpcKind> newChanges)
    {
        var recipients = SundesmosForUpdatePush;
        var newVisuals = new VisualUpdate();
        try
        {
            // Apply new changes and store them for sending.
            Logger.LogTrace($"Awaiting _dataUpdateLock (UpdateAndSendVisuals)", LoggerType.DataDistributor);
            await _dataUpdateLock.WaitAsync();
            Logger.LogTrace($"Acquired _dataUpdateLock (UpdateAndSendVisuals)", LoggerType.DataDistributor);
            try
            {
                newVisuals = await UpdateVisualsInternal(newChanges).ConfigureAwait(false);
            }
            finally
            {
                Logger.LogTrace($"Releasing _dataUpdateLock (UpdateAndSendVisuals)", LoggerType.DataDistributor);
                _dataUpdateLock.Release();
            }

            // Void data with no changes.
            if (!newVisuals.HasData())
                return;

            // Data changed, clear limbo.
            InLimbo.Clear();

            // Send off update.
            var res = await _hub.UserPushIpcOther(new(recipients, newVisuals)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcOther to {recipients.Count} users.", LoggerType.DataDistributor);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateAndSendVisuals: {ex}");
        }
    }

    public async Task UpdateAndSendSingle(OwnedObject obj, IpcKind type)
    {
        // Send this update off to all our visibly connected sundesmos that are not in limbo or new.
        var recipients = SundesmosForUpdatePush;
        string? newData = null;
        try
        {
            // Apply new changes and store them for sending.
            Logger.LogTrace($"Awaiting _dataUpdateLock (UpdateAndSendSingle)", LoggerType.DataDistributor);
            await _dataUpdateLock.WaitAsync();
            Logger.LogTrace($"Acquired _dataUpdateLock (UpdateAndSendSingle)", LoggerType.DataDistributor);
            try
            {
                newData = await UpdateCacheSingleInternal(obj, type, LastCreatedData).ConfigureAwait(false);
            }
            finally
            {
                Logger.LogTrace($"Releasing _dataUpdateLock (UpdateAndSendSingle)", LoggerType.DataDistributor);
                _dataUpdateLock.Release();
            }

            // return if no changes occurred.
            if (string.IsNullOrEmpty(newData))
                return;

            // Data changed, clear limbo.
            InLimbo.Clear();

            // Send off update.
            await _hub.UserPushIpcSingle(new(recipients, obj, type, newData)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcOther to {recipients.Count} users.", LoggerType.DataDistributor);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during UpdateAndSendSingle: {ex}");
        }
    }

    private async Task<ModUpdates> UpdateModsInternal(CancellationToken ct)
    {
        // Collect the current modded state.
        var currentState = await _transients.CollectModdedState(ct).ConfigureAwait(false);
        // Apply the new state to the latest to get the new changed values from it.
        return LastCreatedData.ApplyNewModState(currentState);
    }

    private async Task<VisualUpdate> UpdateVisualsInternal(Dictionary<OwnedObject, IpcKind> changes)
    {
        var changedData = LastCreatedData.DeepClone();
        // process the tasks for each object in parallel.
        var tasks = new List<Task>();
        foreach (var (obj, kinds) in changes)
        {
            if (kinds == IpcKind.None) continue;
            tasks.Add(UpdateDataCache(obj, kinds, changedData));
        }
        // Execute in parallel.
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return LastCreatedData.ApplyAllIpc(changedData);
    }

    private async Task UpdateDataCache(OwnedObject obj, IpcKind toUpdate, ClientDataCache data)
    {
        if (toUpdate.HasAny(IpcKind.Glamourer)) data.GlamourerState[obj] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.CPlus)) data.CPlusState[obj] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;

        if (obj is not OwnedObject.Player) return;

        if (toUpdate.HasAny(IpcKind.ModManips)) data.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Heels)) data.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Moodles)) data.Moodles = await _ipc.Moodles.GetOwn().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Honorific)) data.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.PetNames)) data.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
    }

    private async Task<string?> UpdateCacheSingleInternal(OwnedObject obj, IpcKind type, ClientDataCache data)
    {
        var dataStr = type switch
        {
            IpcKind.ModManips => _ipc.Penumbra.GetMetaManipulationsString(),
            IpcKind.Glamourer => await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.CPlus => await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.Heels => await _ipc.Heels.GetClientOffset().ConfigureAwait(false),
            IpcKind.Moodles => await _ipc.Moodles.GetOwn().ConfigureAwait(false),
            IpcKind.Honorific => await _ipc.Honorific.GetTitle().ConfigureAwait(false),
            IpcKind.PetNames => _ipc.PetNames.GetPetNicknames(),
            _ => string.Empty,
        };
        return data.ApplySingleIpc(obj, type, dataStr) ? dataStr : null;
    }
    #endregion Cache Updates
}
