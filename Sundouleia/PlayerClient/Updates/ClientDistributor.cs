using CkCommons;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Data;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.Services;

/// <summary> 
///     Does the actual server calls to share client data or requests with other sundesmos.
/// </summary>
public sealed class ClientDistributor : DisposableMediatorSubscriberBase
{
    private readonly MainHub _hub;
    private readonly MainConfig _config;
    private readonly AccountConfig _account;
    private readonly IpcManager _ipc;
    private readonly FileUploader _fileUploader;
    private readonly LimboStateManager _limbo;
    private readonly ModdedStateManager _moddedState;
    private readonly SundesmoManager _sundesmos;
    private readonly CharaObjectWatcher _watcher;
    private readonly ClientUpdateService _updater;

    /// <summary>
    ///     If OnHubConnected was sent yet. Ensures a full data update is always sent first.
    /// </summary>
    private bool _hubConnectionSent = false;

    public ClientDistributor(ILogger<ClientDistributor> logger, SundouleiaMediator mediator,
        MainHub hub, MainConfig config, AccountConfig account, IpcManager ipc, 
        FileUploader fileUploader, LimboStateManager limbo, ModdedStateManager moddedState, 
        SundesmoManager sundesmos, CharaObjectWatcher watcher, ClientUpdateService updater)
        : base(logger, mediator)
    {
        _hub = hub;
        _config = config;
        _account = account;
        _ipc = ipc;
        _fileUploader = fileUploader;
        _limbo = limbo;
        _moddedState = moddedState;
        _sundesmos = sundesmos;
        _watcher = watcher;
        _updater = updater;

        // Process applyToPair change.
        Mediator.Subscribe<MoodlesApplyStatusToPair>(this, _ => ApplyStatusTuplesToSundesmo(_.ApplyStatusTupleDto).ConfigureAwait(false));
        // Process connections.
        Mediator.Subscribe<ConnectedMessage>(this, _ =>
        {
            Logger.LogInformation("HubConnected Triggered, reloading and sending full cache.");
            ReloadAndSendCache();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, _ =>
        {
            _updater.NewVisibleUsers.Clear();
            _hubConnectionSent = false;
        });
        Mediator.Subscribe<ConnectionKindChanged>(this, _ =>
        {
            // If previous kind was full pause, dont worry about doing anything.
            if (_.PrevState is ConnectionKind.FullPause)
                return;
            // Otherwise, if the previous type was TryOn, and the new type is not FullPause, perform a reload and send.
            if (_.PrevState is ConnectionKind.TryOnMode && _.NewState is not ConnectionKind.FullPause)
            {
                Logger.LogInformation($"Switched off TryOnMode to another online state, reloading and sending full Cache!");
                ReloadAndSendCache();
            }
        });

        // Process Update Checking.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, _ => UpdateCheck());
    }

    #region Moodles
    private async Task ApplyStatusTuplesToSundesmo(ApplyMoodleStatus dto)
    {
        Logger.LogDebug($"Pushing ApplyMoodlesByStatus to: {dto.User.AliasOrUID}", LoggerType.DataDistributor);
        if (await _hub.UserApplyMoodleTuples(dto).ConfigureAwait(false) is { } res && res.ErrorCode is not SundouleiaApiEc.Success)
            Logger.LogError($"Failed to push ApplyMoodlesByStatus to server. [{res.ErrorCode}]");
        else
            Logger.LogDebug($"Successfully pushed ApplyMoodlesByStatus to the server", LoggerType.DataDistributor);
    }

    public async Task PushMoodlesData(List<UserData> trustedUsers)
    {
        if (!MainHub.IsConnectionDataSynced)
            return;
        Logger.LogDebug($"Pushing MoodlesData to trustedUsers: ({string.Join(", ", trustedUsers.Select(v => v.AliasOrUID))})", LoggerType.DataDistributor);
        await _hub.UserPushMoodlesData(new(trustedUsers, ClientMoodles.Data));
    }

    public async Task PushMoodleStatusUpdate(List<UserData> trustedUsers, MoodlesStatusInfo status, bool wasDeleted)
    {
        if (!MainHub.IsConnectionDataSynced)
            return;
        Logger.LogTrace($"Pushing StatusUpdate to trustedUsers: ({string.Join(", ", trustedUsers.Select(v => v.AliasOrUID))})", LoggerType.DataDistributor);
        await _hub.UserPushStatusModified(new(trustedUsers, status, wasDeleted));
    }

    public async Task PushMoodlePresetUpdate(List<UserData> trustedUsers, MoodlePresetInfo preset, bool wasDeleted)
    {
        if (!MainHub.IsConnectionDataSynced)
            return;
        Logger.LogTrace($"Pushing PresetUpdate to trustedUsers: ({string.Join(", ", trustedUsers.Select(v => v.AliasOrUID))})", LoggerType.DataDistributor);
        await _hub.UserPushPresetModified(new(trustedUsers, preset, wasDeleted));
    }
    #endregion Moodles

    // Only entry point where we ignore timeout states.
    // If this gets abused through we can very easily add timeout functionality here too.
    private async void ReloadAndSendCache()
    {
        // Update the latest data.
        await _updater.RunOnDataUpdateSlim(async () =>
        {
            _updater.LatestData.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;
            _updater.LatestData.GlamourerState[OwnedObject.Player] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.CPlusState[OwnedObject.Player] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPlayerAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.Moodles = await _ipc.Moodles.GetOwnDataStr().ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
            _updater.LatestData.GlamourerState[OwnedObject.MinionOrMount] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.CPlusState[OwnedObject.MinionOrMount] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedMinionMountAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.GlamourerState[OwnedObject.Pet] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.CPlusState[OwnedObject.Pet] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedPetAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.GlamourerState[OwnedObject.Companion] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            _updater.LatestData.CPlusState[OwnedObject.Companion] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.WatchedCompanionAddr).ConfigureAwait(false) ?? string.Empty;
            // Cache mods.
            var moddedState = await _moddedState.CollectModdedState(CancellationToken.None).ConfigureAwait(false);
            Logger.LogDebug($"(ReloadAndSendCache) Collected modded state. [{moddedState.AllFiles.Count} Mod Files]", LoggerType.DataDistributor);
            _updater.LatestData.ApplyNewModState(moddedState);

        }).ConfigureAwait(false);

        // Then send off to all visible users.
        Logger.LogDebug($"UID's in NewVisible are: {string.Join(", ", _updater.NewVisibleUsers.Select(x => x.AliasOrUID))}", LoggerType.DataDistributor);
        Logger.LogDebug($"UID's in Limbo are: {string.Join(", ", _limbo.InLimbo.Select(x => x.AliasOrUID))}", LoggerType.DataDistributor);
        Logger.LogInformation($"(OnHubConnected) Distributing to visible.", LoggerType.DataDistributor);
        _updater.RefreshDistributionCTS();
        _updater.SetDistributionTask(async () =>
        {
            await ResendAll().ConfigureAwait(false);
            _hubConnectionSent = true;
        });
    }

    // Note that we are going to need some kind of logic for handling the edge cases where user A is receiving a new update and that 
    private void UpdateCheck()
    {
        // Do not run update checks if the hub connection update was never sent.
        if (!_hubConnectionSent)
            return;
        if (_updater.NewVisibleUsers.Count is 0)
            return;
        if (!MainHub.IsConnectionDataSynced || PlayerData.IsZoning || !PlayerData.Available)
            return;
        if (_updater.Distributing)
            return;

        // Process a distribution of full data to the newly visible users and then clear the update.
        Logger.LogInformation("(UpdateCheck) Distributing to new visible.", LoggerType.DataDistributor);
        _updater.SetDistributionTask(ResendAll);
    }

    /// <summary>
    ///     Upload any missing files not yet present on the hub. (We could optionally send a isUploading here but idk)
    /// </summary>
    private async Task UploadAndPushMissingMods(List<UserData> usersToPushDataTo, List<ValidFileHash> filesNeedingUpload)
    {
        // Don't bother uploading if we do not have a properly configured cache.
        if (!_config.HasValidCacheFolderSetup())
            return;

        // Upload any missing files not yet present on the hub. (We could optionally send a isUploading here but idk)
        Logger.LogDebug($"Uploading {filesNeedingUpload.Count} missing files to server...", LoggerType.DataDistributor);
        var uploadedFiles = await _fileUploader.UploadFiles(filesNeedingUpload).ConfigureAwait(false);

        Logger.LogDebug($"Uploaded {uploadedFiles.Count}/{filesNeedingUpload.Count} missing files.", LoggerType.DataDistributor);
        // Empty manip string for now, change later if this has problems!
        await _hub.UserPushIpcMods(new PushIpcMods(usersToPushDataTo, new(uploadedFiles, []), string.Empty)).ConfigureAwait(false);
        Logger.LogDebug($"Sent PushIpcMods out to {usersToPushDataTo.Count} users after uploading missing files.", LoggerType.DataDistributor);
    }

    #region Cache Updates
    private async Task ResendAll()
    {
        await _updater.RunOnDataUpdateSlim(async () =>
        {
            // grab the mod and appearnace data from the latest data to send off to the new visible users.
            var modData = _updater.LatestData.ToModUpdates();
            var appearance = _updater.LatestData.ToVisualUpdate();
            Logger.LogDebug($"(ResendAll) Collected:" +
                $"[Hashes: ({modData.NewReplacements.Count} Added, {modData.HashesToRemove.Count} Removed)| Swaps: ({modData.NewSwaps.Count} Added, {modData.SwapsToRemove.Count} Removed)] " +
                $"| {(appearance.HasData() ? "With" : "Without")} Visual Changes", LoggerType.DataDistributor);
            // Collect the new users to send to and then clear the list.
            var recipients = _updater.NewVisibleUsers.ToList();
            _updater.NewVisibleUsers.Clear();

            // Perform the initial send, then if any remaining missing files are present, upload them.
            var res = await _hub.UserPushIpcFull(new(recipients, modData, appearance, true)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcFull to {recipients.Count} newly visible users. {res.Value?.Count ?? 0} Files needed uploading.", LoggerType.DataDistributor);
            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);

            // If the IPC had moodles in it, send that too.
            if (_sundesmos.GetMoodleTrusted(recipients) is { } moodleUsers && moodleUsers.Count > 0)
            {
                Logger.LogDebug($"(ResendAll) Pushing MoodlesData to {moodleUsers.Count} trusted users.", LoggerType.DataDistributor);
                await PushMoodlesData(moodleUsers).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    ///     Called with the assumption that mods have not changed.
    /// </summary>
    public async Task UpdateAndSendSingle(OwnedObject obj, IpcKind type)
    {
        await _updater.RunOnDataUpdateSlim(async () =>
        {
            // Collect the latest data to send off.
            var (newData, changed) = await UpdateCacheSingleInternal(obj, type, _updater.LatestData).ConfigureAwait(false);

            // If no change occurred, do not push to others (our cache is still updated with the latest data)
            if (!changed)
                return;

            var recipients = _updater.UsersForUpdatePush;
            // Send off the update.
            await _hub.UserPushIpcSingle(new(recipients, obj, type, newData)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcSingle to {recipients.Count} users.", LoggerType.DataDistributor);
        });
    }

    /// <summary>
    ///     Called with the assumption that the modded state could have changed.
    /// </summary>
    public async Task CheckStateAndUpdate(Dictionary<OwnedObject, IpcKind> newChanges, IpcKind flattenedChanges)
    {
        // Create initial assumptions.
        VisualUpdate changedVisuals = VisualUpdate.Empty;
        ModUpdates changedMods = ModUpdates.Empty;
        bool manipStrDiff = false;

        await _updater.RunOnDataUpdateSlim(async () =>
        {
            // ------------------------------------------
            // =========== CACHE UPDATE LOGIC ===========
            // ------------------------------------------

            // If flattened changes include Glamourer, or Mods, check manipulations & modded state.
            if (flattenedChanges.HasAny(IpcKind.Glamourer | IpcKind.Mods))
            {
                // If manipulations changed, add it to the total changes changes.
                if (_updater.LatestData.ApplySingleIpc(OwnedObject.Player, IpcKind.ModManips, _ipc.Penumbra.GetMetaManipulationsString()))
                {
                    manipStrDiff = true;
                    // Update newChanges to help with visual updates.
                    newChanges[OwnedObject.Player] |= IpcKind.ModManips;
                }

                // It is also possible that the mods could have changed if a glamourer of mod change occurred.
                // We need to update the mod state first.
                changedMods = await UpdateModsInternal(CancellationToken.None).ConfigureAwait(false);
                // If the mods had changed, we need to ensure we send off the mods.
                if (changedMods.HasAnyChanges)
                {
                    Logger.LogDebug($"Mods had changes: " +
                        $"[FileHashes: ({changedMods.NewReplacements.Count} Added, {changedMods.HashesToRemove.Count} Removed) " +
                        $"|FileSwaps: ({changedMods.NewSwaps.Count} Added, {changedMods.SwapsToRemove.Count} Removed)]", LoggerType.DataDistributor);
                    newChanges[OwnedObject.Player] |= IpcKind.Mods;
                    flattenedChanges |= IpcKind.Mods;
                }
            }

            // With these outliers taken into account, print the new flattened changes.
            Logger.LogDebug($"Flattened Changes: {flattenedChanges}", LoggerType.DataDistributor);

            // If the flattened changes requires we check the visual state, then do so.
            if (flattenedChanges is not IpcKind.Mods)
                changedVisuals = await UpdateVisualsInternal(newChanges, manipStrDiff).ConfigureAwait(false);


            // -----------------------------------------------
            // =========== DATA DISTRIBUTION LOGIC ===========
            // -----------------------------------------------

            // Grab recipients. If there is nobody to send an update to, exit early.
            // We update the cache regardless so that NewVisibleUsers are given the correct latest data.
            var recipients = _updater.UsersForUpdatePush;
            if (recipients.Count is 0)
                return;

            // MODS CONDITION - Flattened changes could be just Mods, or both but with a failed visual check.
            if (flattenedChanges is IpcKind.Mods || (!changedVisuals.HasData() && changedMods.HasAnyChanges))
            {
                await SendModsUpdate(recipients, changedMods, manipStrDiff).ConfigureAwait(false);
                return;
            }

            // FULL UPDATE CONDITION: Both results were valid.
            if (changedMods.HasAnyChanges)
            {
                Logger.LogWarning($"CheckStateAndUpdate found both mod and visual changes to send to {recipients.Count} users.", LoggerType.DataDistributor);
                await SendFullUpdate(recipients, changedMods, changedVisuals).ConfigureAwait(false);
                return;
            }
            // VISUALS CONDITION: Only visual changes.
            else
            {
                Logger.LogWarning($"CheckStateAndUpdate found only visual changes to send to {recipients.Count} users.", LoggerType.DataDistributor);
                await SendVisualsUpdate(recipients, changedVisuals).ConfigureAwait(false);
                return;
            }
        });
    }

    private async Task SendFullUpdate(List<UserData> recipients, ModUpdates modChanges, VisualUpdate visualChanges)
    {
        try
        {
            var res = await _hub.UserPushIpcFull(new(recipients, modChanges, visualChanges, false)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcFull to {recipients.Count} users. {res.Value?.Count ?? 0} Files needed uploading.", LoggerType.DataDistributor);

            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during SendFullUpdate: {ex}");
        }
    }

    private async Task SendModsUpdate(List<UserData> recipients, ModUpdates modChanges, bool newManipulations)
    {
        try
        {
            string manipStr = newManipulations ? _updater.LatestData.ModManips : string.Empty;
            var res = await _hub.UserPushIpcMods(new(recipients, modChanges, manipStr)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcMods to {recipients.Count} users. {res.Value?.Count ?? 0} Files needed uploading. [HadManipChange?: {newManipulations}]", LoggerType.DataDistributor);

            // Handle any missing mods after.
            if (res.ErrorCode is 0 && res.Value is { } toUpload && toUpload.Count > 0)
                _ = UploadAndPushMissingMods(recipients, toUpload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during SendModsUpdate: {ex}");
        }
    }

    private async Task SendVisualsUpdate(List<UserData> recipients, VisualUpdate visualChanges)
    {
        try
        {
            await _hub.UserPushIpcOther(new(recipients, visualChanges)).ConfigureAwait(false);
            Logger.LogDebug($"Sent PushIpcOther to {recipients.Count} users.", LoggerType.DataDistributor);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error during SendVisualsUpdate: {ex}");
        }
    }

    private async Task<ModUpdates> UpdateModsInternal(CancellationToken ct)
    {
        var currentState = await _moddedState.CollectModdedState(ct).ConfigureAwait(false);
        // Apply the new state to the latest to get the new changed values from it.
        return _updater.LatestData.ApplyNewModState(currentState);
    }

    private async Task<VisualUpdate> UpdateVisualsInternal(Dictionary<OwnedObject, IpcKind> changes, bool forceManips)
    {
        var changedData = new ClientDataCache(_updater.LatestData);
        // process the tasks for each object in parallel.
        var tasks = new List<Task>();
        foreach (var (obj, kinds) in changes)
        {
            if (kinds == IpcKind.None) continue;
            tasks.Add(UpdateDataCache(obj, kinds, changedData));
        }
        // Execute in parallel.
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return _updater.LatestData.ApplyAllIpc(changedData, forceManips);
    }

    private async Task UpdateDataCache(OwnedObject obj, IpcKind toUpdate, ClientDataCache data)
    {
        if (toUpdate.HasAny(IpcKind.Glamourer)) data.GlamourerState[obj] = await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.CPlus)) data.CPlusState[obj] = await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty;

        if (obj is not OwnedObject.Player) return;

        // Special case, update regardless if the manips is an included change.
        if (toUpdate.HasAny(IpcKind.ModManips)) data.ModManips = _ipc.Penumbra.GetMetaManipulationsString() ?? string.Empty;

        if (toUpdate.HasAny(IpcKind.Heels)) data.HeelsOffset = await _ipc.Heels.GetClientOffset().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Moodles)) data.Moodles = await _ipc.Moodles.GetOwnDataStr().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.Honorific)) data.TitleData = await _ipc.Honorific.GetTitle().ConfigureAwait(false) ?? string.Empty;
        if (toUpdate.HasAny(IpcKind.PetNames)) data.PetNames = _ipc.PetNames.GetPetNicknames() ?? string.Empty;
    }

    private async Task<(string, bool)> UpdateCacheSingleInternal(OwnedObject obj, IpcKind type, ClientDataCache data)
    {
        var dataStr = type switch
        {
            IpcKind.ModManips => _ipc.Penumbra.GetMetaManipulationsString(),
            IpcKind.Glamourer => await _ipc.Glamourer.GetBase64StateByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.CPlus => await _ipc.CustomizePlus.GetActiveProfileByPtr(_watcher.FromOwned(obj)).ConfigureAwait(false) ?? string.Empty,
            IpcKind.Heels => await _ipc.Heels.GetClientOffset().ConfigureAwait(false),
            IpcKind.Moodles => await _ipc.Moodles.GetOwnDataStr().ConfigureAwait(false),
            IpcKind.Honorific => await _ipc.Honorific.GetTitle().ConfigureAwait(false),
            IpcKind.PetNames => _ipc.PetNames.GetPetNicknames(),
            _ => string.Empty,
        };
        var changed = data.ApplySingleIpc(obj, type, dataStr);
        var result = changed ? dataStr : string.Empty;
        return (result, changed);
    }
    #endregion Cache Updates
}
