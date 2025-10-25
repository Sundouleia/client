using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Data;
using System.Xml.Linq;
using TerraFX.Interop.Windows;

namespace Sundouleia.Pairs;

/// <summary>
///     Handles the cached data for a player, and their current rendered status. <para />
///     The Rendered status should be handled differently from the alterations. <para />
///     Every pair has their own instance of this data.
/// </summary>
public class PlayerHandler : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileCache;
    private readonly FileDownloader _downloader;
    private readonly CharaObjectWatcher _watcher;
    private readonly IpcManager _ipc;

    private CancellationTokenSource _runtimeCTS = new();
    private CancellationTokenSource _dlWaiterCTS = new();
    private Task? _dlWaiterTask;

    public Sundesmo Sundesmo { get; init; }
    private unsafe Character* _player = null;
    // cached data for appearance.
    private Guid _tempCollection;
    private Dictionary<string, VerifiedModFile> _replacements = []; // Hash -> VerifiedFile with download link included if necessary.
    private Guid _tempProfile; // CPlus temp profile id.
    private IpcDataPlayerCache? _appearanceData = null;

    public PlayerHandler(Sundesmo sundesmo, ILogger<PlayerHandler> logger, SundouleiaMediator mediator,
        FileCacheManager fileCache, FileDownloader downloads, CharaObjectWatcher watcher, IpcManager ipc)
        : base(logger, mediator)
    {
        Sundesmo = sundesmo;

        _fileCache = fileCache;
        _downloader = downloads;
        _watcher = watcher;
        _ipc = ipc;

        // Initial collection creation if valid.
        TryCreateAssignTempCollection().GetAwaiter().GetResult();

        // Listen to Penumbra init & dispose methods to re-assign collections.
        Mediator.Subscribe<PenumbraInitialized>(this, async _ =>
        {
            // Create / Assign the temp collection.
            await TryCreateAssignTempCollection().ConfigureAwait(false);
            // If there is replacement data, reapply it.
            if (IsRendered && _replacements.Count > 0)
                await ApplyMods().ConfigureAwait(false);
        });

        Mediator.Subscribe<PenumbraDisposed>(this, _ => _tempCollection = Guid.Empty);
        Mediator.Subscribe<HonorificReady>(this, async _ =>
        {
            if (!IsRendered || string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Honorific])) return;
            await ApplyHonorific().ConfigureAwait(false);
        });
        Mediator.Subscribe<PetNamesReady>(this, async _ =>
        {
            if (!IsRendered || string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.PetNames])) return;
            await ApplyPetNames().ConfigureAwait(false);
        });

        Mediator.Subscribe<WatchedObjectCreated>(this, msg => MarkVisibleForAddress(msg.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, msg => UnrenderPlayer(msg.Address));
    }
    // Public Accessors.
    public Character DataState { get { unsafe { return *_player; } } }
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ulong EntityId => _player->EntityId;
    public unsafe ulong GameObjectId => _player->GetGameObjectId().ObjectId;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public unsafe IntPtr DrawObjAddress => (nint)_player->DrawObject;
    public unsafe int RenderFlags => _player->RenderFlags;
    public unsafe bool HasModelInSlotLoaded => ((CharacterBase*)_player->DrawObject)->HasModelInSlotLoaded != 0;
    public unsafe bool HasModelFilesInSlotLoaded => ((CharacterBase*)_player->DrawObject)->HasModelFilesInSlotLoaded != 0;

    public string NameString { get; private set; } = string.Empty; // Manual, to assist timeout tasks.
    public unsafe bool IsRendered => _player != null;
    public bool HasAlterations => _appearanceData != null || _replacements.Count is not 0;

    #region Rendering
    // Initializes Player Rendering for this object if the address matches the OnlineUserIdent.
    // Called by the Watcher's mediator subscriber. Not intended for public access.
    // Assumes the passed in address is a visible Character*
    private void MarkVisibleForAddress(IntPtr address)
    {
        if (!Sundesmo.IsOnline || Address != IntPtr.Zero) return; // Already exists or not online.
        if (string.IsNullOrEmpty(Sundesmo.Ident)) return; // Must have valid CharaIdent.
        if (Sundesmo.Ident != SundouleiaSecurity.GetIdentHashByCharacterPtr(address)) return;

        Logger.LogDebug($"Matched {Sundesmo.GetNickAliasOrUid()} to a created object @ [{address:X}]", LoggerType.PairHandler);
        MarkRenderedInternal(address);
    }

    // Publicly accessible method to try and identify the address of an online user to mark them as visible.
    internal async Task SetVisibleIfRendered()
    {
        if (!Sundesmo.IsOnline) return; // Must be online.
        if (string.IsNullOrEmpty(Sundesmo.Ident)) return; // Must have valid CharaIdent.
        // If already rendered, reapply alterations and return.
        if (IsRendered)
        {
            Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()}) is already rendered, reapplying alterations.", LoggerType.PairHandler);
            Mediator.Publish(new SundesmoPlayerRendered(this));
            Mediator.Publish(new RefreshWhitelistMessage());
            await ReInitializeInternal().ConfigureAwait(false);
        }
        else if (_watcher.TryGetExisting(this, out IntPtr playerAddr))
        {
            Logger.LogDebug($"Matched {Sundesmo.GetNickAliasOrUid()} to an existing object @ [{playerAddr:X}]", LoggerType.PairHandler);
            MarkRenderedInternal(playerAddr);
        }
    }

    private unsafe void MarkRenderedInternal(IntPtr address)
    {
        // End Timeouts if running as one of our states became valid again.
        Sundesmo.EndTimeout();
        // Set the game data.
        _player = (Character*)address;
        NameString = _player->NameString;
        // Notify other services.
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] rendered!", LoggerType.PairHandler);
        Mediator.Publish(new SundesmoPlayerRendered(this));
        Mediator.Publish(new RefreshWhitelistMessage());
        ReInitializeInternal().ConfigureAwait(false);
    }

    private async Task ReInitializeInternal()
    {
        // Create/Assign this ObjectIdx to the Penumbra Temp Collection if necessary.
        await TryCreateAssignTempCollection().ConfigureAwait(false);

        // If they are online and have alterations, reapply them. Otherwise, exit.
        if (!Sundesmo.IsOnline || !HasAlterations) 
            return;

        // Await until we know the player has absolutely finished loading in.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        Logger.LogDebug($"[{Sundesmo.GetNickAliasOrUid()}] is fully loaded, reapplying alterations.", LoggerType.PairHandler);
        await ReapplyAlterations().ConfigureAwait(false);
    }

    /// <summary>
    ///     Fired whenever the player is unrendered from the game world. <para />
    ///     Not linked to appearance alterations, but does begin its timeout if not set.
    /// </summary>
    private unsafe void UnrenderPlayer(IntPtr address)
    {
        if (Address == IntPtr.Zero || address != Address)
            return;
        // Clear the GameData.
        _player = null;
        // Temp timeout inform.
        Sundesmo.TriggerTimeoutTask();
        // Refresh the list to reflect visible state.
        Logger.LogDebug($"Marking {Sundesmo.GetNickAliasOrUid()} as unrendered @ [{address:X}]", LoggerType.PairHandler);
        Mediator.Publish(new RefreshWhitelistMessage());
    }

    // Faze out as we move to watcher.
    private async Task WaitUntilValidDrawObject(CancellationToken timeoutToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, _runtimeCTS.Token);
        while (!cts.IsCancellationRequested)
        {
            if (!PlayerData.IsZoning && IsObjectLoaded())
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     There are conditions where an object can be rendered / created, but not drawable, or currently bring drawn. <para />
    ///     This mainly occurs on login or when transferring between zones, but can also occur during redraws and such.
    ///     We can get around this by checking for various draw conditions.
    /// </summary>
    public unsafe bool IsObjectLoaded()
    {
        if (!IsRendered) return false; // Not even rendered, does not exist.
        if (DrawObjAddress == IntPtr.Zero) return false; // Character object does not exist yet.
        if (RenderFlags == 2048) return false; // Render Flag for "IsLoading" (2048 == 0b100000000000)
        if (HasModelInSlotLoaded) return false; // There are models that need to still be loaded into the DrawObject slots.
        if (HasModelFilesInSlotLoaded) return false; // There are model files that need to still be loaded into the DrawObject slots.
        return true;
    }
    #endregion Rendering

    #region Altaration Control
    // Adds a temporary collection for this handler.
    private async Task TryCreateAssignTempCollection()
    {
        if (!IpcCallerPenumbra.APIAvailable)
            return;
        // Create new Collection if we need one.
        if (_tempCollection == Guid.Empty)
            _tempCollection = await _ipc.Penumbra.NewSundesmoCollection(Sundesmo.UserData.UID).ConfigureAwait(false);

        // If we are rendered, assign the collection
        if (IsRendered)
            await _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reverts the rendered alterations on a player. <b> This does not delete the alteration data. </b>
    /// </summary>
    public async Task RevertRenderedAlterations(CancellationToken ct = default)
    {
        // Revert any C+ Data if we have any.
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        // Revert based on rendered state.
        if (IsRendered)
            await RevertAlterations(Sundesmo.GetNickAliasOrUid(), NameString, Address, ObjIndex, ct).ConfigureAwait(false);
        else if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
            await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);
    }

    public async Task RevertAlterations(string aliasOrUid, string name, IntPtr address, ushort objIdx, CancellationToken token)
    {
        if (address == IntPtr.Zero)
            return;
        // We can care about parallel execution here if we really want to but i dont care atm.
        await _ipc.PetNames.ClearPetNamesByIdx(objIdx).ConfigureAwait(false);
        await _ipc.Glamourer.ReleaseActor(objIdx).ConfigureAwait(false);
        await _ipc.Heels.RestoreUserOffset(objIdx).ConfigureAwait(false);
        await _ipc.Honorific.ClearTitleAsync(objIdx).ConfigureAwait(false);
        await _ipc.Moodles.ClearByPtr(address).ConfigureAwait(false);
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
    }

    // Don't entirely need to await this, but its an option we want it i guess.
    public async Task UpdateAndApplyAlterations(NewModUpdates modChanges, IpcDataPlayerUpdate ipcChanges, bool isInitialData)
    {
        // If initial data, clear replacements and appearance data.
        if (isInitialData)
        {
            _replacements.Clear();
            _appearanceData = null;
        }

        // Update Data.
        var visualDiff = UpdateDataIpc(ipcChanges);
        var modDiff = UpdateDataMods(modChanges);
        // Apply Changes if necessary.
        await ApplyAlterations(visualDiff, modDiff).ConfigureAwait(false);
    }

    public async Task UpdateAndApplyMods(NewModUpdates modChanges, string manipString)
    {
        bool needsRedraw = false;
        // Update the manipString, and apply if changes occurred.
        if (!string.IsNullOrEmpty(manipString))
            if (UpdateDataIpc(IpcKind.ModManips, manipString))
                needsRedraw |= await ApplyVisualsSingle(IpcKind.ModManips).ConfigureAwait(false);

        // Update the mod data, and apply if changes occurred.
        if (UpdateDataMods(modChanges))
            needsRedraw |= await ApplyMods().ConfigureAwait(false);

        ConditionalRedraw(needsRedraw);
    }

    public async Task UpdateAndApplyIpc(IpcDataPlayerUpdate ipcChanges)
    {
        var visualDiff = UpdateDataIpc(ipcChanges);
        if (visualDiff is IpcKind.None)
            return;
        // Apply changes.
        ConditionalRedraw(await ApplyVisuals(visualDiff).ConfigureAwait(false));
    }

    public async Task UpdateAndApplyIpc(IpcKind kind, string newData)
    {
        if (!UpdateDataIpc(kind, newData))
            return;
        // Apply changes.
        ConditionalRedraw(await ApplyVisualsSingle(kind).ConfigureAwait(false));
    }
    public async Task ReapplyAlterations()
    {
        // Apply changes, then redraw if necessary.
        bool visualRedrawNeeded = await ReapplyVisuals().ConfigureAwait(false);
        bool modRedrawNeeded = await ApplyMods().ConfigureAwait(false);
        ConditionalRedraw(visualRedrawNeeded || modRedrawNeeded);
    }
    #endregion Altaration Control

    #region Alteration Updates
    private void UpdateDataFull(NewModUpdates modData, IpcDataPlayerUpdate ipcData, out IpcKind visualDiff, out bool modDiff)
    {
        visualDiff = UpdateDataIpc(ipcData);
        modDiff = UpdateDataMods(modData);
    }

    private bool UpdateDataMods(NewModUpdates modData)
    {
        // Remove all keys to remove.
        foreach (var hash in modData.HashesToRemove)
            _replacements.Remove(hash);
        // Add all new files to add.
        foreach (var file in modData.FilesToAdd)
            _replacements[file.Hash] = file;

        Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data")));
        Logger.LogTrace($"Mod Data received from {NameString}({Sundesmo.GetNickAliasOrUid()}) [{modData.FilesToAdd.Count} New | {modData.HashesToRemove.Count} ToRemove | {modData.FilesUploading} Uploading]", LoggerType.PairHandler);

        // If we do not have any penumbraAPI or file cache, do not attempt downloads.
        if (!IpcCallerPenumbra.APIAvailable || !_fileCache.CacheFolderIsValid())
        {
            Logger.LogDebug("Either Penumbra IPC or File Cache is not available, cannot process mod data.", LoggerType.PairHandler);
            return modData.HasChanges;
        }
        
        // Set the uploading text based on if we have new files to upload or not.
        if (modData.FilesUploading > 0)
            Mediator.Publish(new FilesUploading(this));
        else
            Mediator.Publish(new FilesUploaded(this));

        // We should take any new mods to add, and enqueue them to the file downloader.
        _downloader.BeginDownloads(this, modData.FilesToAdd);
        return modData.HasChanges;
    }

    // Returns the changes applied.
    private IpcKind UpdateDataIpc(IpcDataPlayerUpdate ipcData)
    {
        _appearanceData ??= new();
        return _appearanceData.UpdateCache(ipcData);
    }

    private bool UpdateDataIpc(IpcKind kind, string newData)
    {
        _appearanceData ??= new();
        return _appearanceData.UpdateCacheSingle(kind, newData);
    }
    #endregion Alteration Updates

    #region Alteration Application
    private async Task ApplyAlterations(IpcKind visualChanges, bool modsChanged)
    {
        var refresh = false;
        if (visualChanges is not IpcKind.None)
            refresh |= await ApplyVisuals(visualChanges).ConfigureAwait(false);
        if (modsChanged)
            refresh |= await ApplyMods().ConfigureAwait(false);

        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had their alterations applied. (Visual Changes: {visualChanges}, Mod Changes: {modsChanged})", LoggerType.PairHandler);
        ConditionalRedraw(refresh);
    }

    // True if data was applied, false otherwise. (useful for redraw)
    private async Task<bool> ApplyMods()
    {
        // Sanity checks.
        if (!IsRendered)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] is not rendered, skipping mod application.", LoggerType.PairMods);
            return false;
        }
        if (_tempCollection == Guid.Empty)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] does not have a temporary collection, skipping mod application.", LoggerType.PairMods);
            return false;
        }
        if (_replacements.Count == 0)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] has no mod replacements, skipping mod application.", LoggerType.PairMods);
            return false;
        }

        // Wait for the mods to finish downloading (this can be interrupted by new mod data)
        var moddedPaths = await WaitForModDownloads().ConfigureAwait(false);

        // Await for true render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return false;

        Logger.LogDebug($"Applying mod data for {NameString}({Sundesmo.GetNickAliasOrUid()}) with [{moddedPaths.Count}] modded paths.", LoggerType.PairMods);
        await _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).ConfigureAwait(false);
        await _ipc.Penumbra.ReapplySundesmoMods(_tempCollection, moddedPaths).ConfigureAwait(false);
        
        return moddedPaths.Count > 0;
    }

    /// <summary>
    ///     Awaits for all mod downloads to complete. <para />
    ///     If this function is called again while one is still running, 
    ///     halts it and returns partial progress.
    /// </summary>
    /// <returns> The Modded Dictionary to apply. </returns>
    private async Task<Dictionary<string, string>> WaitForModDownloads()
    {
        // Interrupt any previous download waiters to enforce partial application.
        _dlWaiterCTS = _dlWaiterCTS.SafeCancelRecreate();

        // Grab the current files from the fileCacheCSV.
        Logger.LogDebug($"Checking cache for existing files for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairMods);
        var missingFiles = _downloader.GetExistingFromCache(_replacements, out var moddedDict, _runtimeCTS.Token);

        // Track attempts. Begin downloading the missing files, if any, until all are gone.
        // If at any point this process is interrupted, leave the white loop.
        try
        {
            int attempts = 0;
            while (missingFiles.Count > 0 && attempts++ <= 10 && !_dlWaiterCTS.Token.IsCancellationRequested)
            {
                // Await for the current task to finish, if any is assigned.
                if (_dlWaiterTask != null && !_dlWaiterTask.IsCompleted)
                {
                    Logger.LogDebug($"Finishing prior download task for {NameString} ({Sundesmo.GetNickAliasOrUid()}", LoggerType.PairMods);
                    await _dlWaiterTask.ConfigureAwait(false);
                }

                // Begin the download task.
                Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data.")));
                Logger.LogDebug($"Downloading {missingFiles.Count} files for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairMods);

                _dlWaiterTask = Task.Run(async () => await _downloader.WaitForDownloadsToComplete(this), _dlWaiterCTS.Token);

                // Await for the task to finish. (This will occur immediately if cancelled halfway through)
                await _dlWaiterTask.ConfigureAwait(false);

                // If the cancel token was requested, break out.
                if (_dlWaiterCTS.Token.IsCancellationRequested)
                {
                    Logger.LogWarning($"Downloader interrupted with new mod update for {NameString}({Sundesmo.GetNickAliasOrUid()}), setting PARTIAL application.", LoggerType.PairMods);
                    break;
                }

                // Recheck missing files.
                missingFiles = _downloader.GetExistingFromCache(_replacements, out moddedDict, _dlWaiterCTS.Token);

                // Delay ~2s with ±15–20% random jitter to avoid synchronized polling
                var jitter = 0.15 + Random.Shared.NextDouble() * 0.05; // 0.15..0.20
                if (Random.Shared.Next(2) == 0) jitter = -jitter;
                var delay = TimeSpan.FromSeconds(2 * (1 + jitter));
                await Task.Delay(delay, _dlWaiterCTS.Token).ConfigureAwait(false);
            }

            _dlWaiterCTS.Token.ThrowIfCancellationRequested();

            Logger.LogDebug($"Missing files downloaded, applying mod data for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairMods);
        }
        catch (TaskCanceledException)
        {
            // Grab final partial progress. This could happen during unloading, so catch ObjectDisposedException as well.
            missingFiles = _downloader.GetExistingFromCache(_replacements, out moddedDict, _runtimeCTS.Token);
        }

        // Return the modded dictionary to apply.
        return moddedDict;
    }

    // True if data was applied, false otherwise. (useful for redraw)
    private async Task<bool> ApplyVisuals(IpcKind changes)
    {
        if (!IsRendered || _appearanceData is null)
            return false;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return false;

        Logger.LogDebug($"Reapplying visual data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        var toApply = new List<Task>();

        if (changes.HasAny(IpcKind.Glamourer))  toApply.Add(ApplyGlamourer());
        if (changes.HasAny(IpcKind.Heels))      toApply.Add(ApplyHeels());
        if (changes.HasAny(IpcKind.CPlus))      toApply.Add(ApplyCPlus());
        if (changes.HasAny(IpcKind.Honorific))  toApply.Add(ApplyHonorific());
        if (changes.HasAny(IpcKind.Moodles))    toApply.Add(ApplyMoodles());
        if (changes.HasAny(IpcKind.ModManips))  toApply.Add(ApplyModManips());
        if (changes.HasAny(IpcKind.PetNames))   toApply.Add(ApplyPetNames());

        await Task.WhenAll(toApply).ConfigureAwait(false);

        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had their visual data reapplied.", LoggerType.PairHandler);
        return changes.HasAny(IpcKind.Glamourer | IpcKind.CPlus | IpcKind.ModManips);
    }

    // True if we should redraw, false otherwise.
    private async Task<bool> ApplyVisualsSingle(IpcKind kind)
    {
        if (!IsRendered || _appearanceData is null)
            return false;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return false;

        // Apply change.
        var task = kind switch
        {
            IpcKind.Glamourer => ApplyGlamourer(),
            IpcKind.Heels => ApplyHeels(),
            IpcKind.CPlus => ApplyCPlus(),
            IpcKind.Honorific => ApplyHonorific(),
            IpcKind.Moodles => ApplyMoodles(),
            IpcKind.ModManips => ApplyModManips(),
            IpcKind.PetNames => ApplyPetNames(),
            _ => Task.CompletedTask
        };
        await task.ConfigureAwait(false);

        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had a single IPC change ({kind})", LoggerType.PairHandler);
        return kind.HasAny(IpcKind.Glamourer | IpcKind.CPlus | IpcKind.ModManips);
    }

    private async Task<bool> ReapplyVisuals()
    {
        if (!IsRendered || _appearanceData is null)
            return false;

        await WaitUntilValidDrawObject().ConfigureAwait(false);
        
        Logger.LogDebug($"Reapplying visual data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        var toApply = new List<Task>();
        
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Glamourer])) toApply.Add(ApplyGlamourer());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Heels]))     toApply.Add(ApplyHeels());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))     toApply.Add(ApplyCPlus());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Honorific])) toApply.Add(ApplyHonorific());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Moodles]))   toApply.Add(ApplyMoodles());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.ModManips])) toApply.Add(ApplyModManips());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.PetNames]))  toApply.Add(ApplyPetNames());
        
        // Run in parallel.
        await Task.WhenAll(toApply).ConfigureAwait(false);
        
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had their visual data reapplied.", LoggerType.PairHandler);
        return toApply.Count > 0;
    }
    #endregion Alteration Application

    #region Ipc Helpers
    private async Task ApplyGlamourer()
    {
        Logger.LogDebug($"Applying glamourer state for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);
    }
    private async Task ApplyHeels()
    {
        Logger.LogDebug($"Setting heels offset for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        await _ipc.Heels.SetUserOffset(ObjIndex, _appearanceData!.Data[IpcKind.Heels]).ConfigureAwait(false);
    }
    private async Task ApplyHonorific()
    {
        Logger.LogDebug($"Setting honorific title for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        await _ipc.Honorific.SetTitleAsync(ObjIndex, _appearanceData!.Data[IpcKind.Honorific]).ConfigureAwait(false);
    }
    private async Task ApplyMoodles()
    {
        Logger.LogDebug($"Setting moodles status for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        await _ipc.Moodles.SetByPtr(Address, _appearanceData!.Data[IpcKind.Moodles]).ConfigureAwait(false);
    }
    private async Task ApplyModManips()
    {
        Logger.LogDebug($"Setting mod manipulations for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        await _ipc.Penumbra.SetSundesmoManipulations(_tempCollection, _appearanceData!.Data[IpcKind.ModManips]).ConfigureAwait(false);
    }
    private Task ApplyPetNames()
    {
        var nickData = _appearanceData!.Data[IpcKind.PetNames];
        Logger.LogDebug($"{(string.IsNullOrEmpty(nickData) ? "Clearing" : "Setting")} pet nicknames for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
        _ipc.PetNames.SetNamesByIdx(ObjIndex, nickData);
        return Task.CompletedTask;
    }
    private async Task ApplyCPlus()
    {
        if (string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]) && _tempProfile != Guid.Empty)
        {
            Logger.LogDebug($"Reverting CPlus profile {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            Logger.LogDebug($"Applying CPlus profile {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairAppearance);
            _tempProfile = await _ipc.CustomizePlus.ApplyTempProfile(this, _appearanceData.Data[IpcKind.CPlus]).ConfigureAwait(false);
        }
    }

    public void ConditionalRedraw(bool condition)
    {
        if (condition && IsRendered)
        {
            Logger.LogDebug($"Redrawing [{Sundesmo.GetNickAliasOrUid()}] due to alteration changes.", LoggerType.PairHandler);
            _ipc.Penumbra.RedrawGameObject(ObjIndex);
        }
    }
    #endregion Ipc Helpers

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IntPtr addr = IsRendered ? Address : IntPtr.Zero;
        ushort objIdx = IsRendered ? ObjIndex : (ushort)0;

        // Stop any actively running tasks.
        _dlWaiterCTS.SafeCancelDispose();
        // Cancel any tasks depending on runtime. (Do not dispose)
        _runtimeCTS.SafeCancel();
        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrEmpty(NameString))
        {
            Logger.LogDebug($"Disposing {NameString}({Sundesmo.GetNickAliasOrUid()}) @ [{Address:X}]", LoggerType.PairHandler);
            Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.Disposed, "Disposed")));
        }

        // Do not dispose if the framework is unloading!
        // (means we are shutting down the game and cannot transmit calls to other ipcs without causing fatal errors!)
        if (Svc.Framework.IsFrameworkUnloading)
        {
            Logger.LogWarning($"Framework is unloading, skipping disposal for {NameString}({Sundesmo.GetNickAliasOrUid()})");
            return;
        }

        // Process off the disposal thread. (Avoids deadlocking on plugin shutdown)
        _ = SafeRevertOnDisposal(Sundesmo.GetNickAliasOrUid(), NameString, addr, objIdx).ConfigureAwait(false);
    }

    /// <summary>
    ///     What to fire whenever called on application shutdown instead of the normal disposal method.
    /// </summary>
    private async Task SafeRevertOnDisposal(string nickAliasOrUid, string name, IntPtr address, ushort objIdx)
    {
        try
        {
            if (_tempCollection != Guid.Empty)
            {
                Logger.LogTrace($"Removing {name}(({nickAliasOrUid})'s temporary collection.", LoggerType.PairHandler);
                await _ipc.Penumbra.RemoveSundesmoCollection(_tempCollection).ConfigureAwait(false);
            }
            // revert glamourer by name.
            if (!IsRendered && !string.IsNullOrEmpty(name))
            {
                Logger.LogTrace($"Reverting {name}(({nickAliasOrUid})'s actor state", LoggerType.PairHandler);
                await _ipc.Glamourer.ReleaseByName(name).ConfigureAwait(false);
            }
            // Check for zone relative data.
            if (!PlayerData.IsZoning && !PlayerData.InCutscene && IsRendered)
            {
                Logger.LogDebug($"{name}(({nickAliasOrUid}) is rendered, reverting by address/index.", LoggerType.PairHandler);
                using var timeoutCTS = new CancellationTokenSource();
                timeoutCTS.CancelAfter(TimeSpan.FromSeconds(30));
                await RevertAlterations(nickAliasOrUid, name, address, objIdx, timeoutCTS.Token);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error reverting {name}({nickAliasOrUid} on shutdown: {ex}");
        }
        finally
        {
            // Clear internal data.
            _tempCollection = Guid.Empty;
            _replacements.Clear();
            _tempProfile = Guid.Empty;
            _appearanceData = null;
            NameString = string.Empty;
            unsafe { _player = null; }
        }
    }

    public void DrawDebugInfo()
    {
        using var node = ImRaii.TreeNode($"Player Alterations##{Sundesmo.UserData.UID}-alterations-player");
        if (!node) return;

        DebugAppliedMods();
        if (_appearanceData is not null)
            DebugAppearance();
    }

    private void DebugAppliedMods()
    {
        using var node = ImRaii.TreeNode($"Mods##{Sundesmo.UserData.UID}-mod-replacements");
        if (!node) return;

        using var table = ImRaii.Table("sundesmos-mods-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Hash");
        ImGui.TableSetupColumn("Game Paths");
        ImGui.TableSetupColumn("Resolved Path");
        ImGui.TableHeadersRow();
        foreach (var (hash, mod) in _replacements)
        {
            ImGui.TableNextColumn();
            CkGui.ColorText(hash, ImGuiColors.DalamudViolet);
            ImGui.TableNextColumn();
            ImGui.Text(string.Join("\n", mod.GamePaths));
            ImGui.TableNextColumn();
            ImGui.Text(mod.SwappedPath);
        }
    }

    private void DebugAppearance()
    {
        using var node = ImRaii.TreeNode($"Appearance##{Sundesmo.UserData.UID}-appearance-player");
        if (!node) return;

        using var table = ImRaii.Table("sundesmo-appearance", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Data Type");
        ImGui.TableSetupColumn("Reapply Test");
        ImGui.TableSetupColumn("Data Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        ImGui.Text("Glamourer");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]), id: $"{Sundesmo.UserData.UID}-glamourer-reapply"))
            UiService.SetUITask(ApplyGlamourer);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.Glamourer]);

        ImGui.TableNextColumn();
        ImGui.Text("CPlus");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.CPlus]), id: $"{Sundesmo.UserData.UID}-cplus-reapply"))
            UiService.SetUITask(ApplyCPlus);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.CPlus]);

        ImGui.TableNextColumn();
        ImGui.Text("ModManips");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.ModManips]), id: $"{Sundesmo.UserData.UID}-manips-reapply"))
            UiService.SetUITask(ApplyModManips);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.ModManips]);

        ImGui.TableNextColumn();
        ImGui.Text("HeelsOffset");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Heels]), id: $"{Sundesmo.UserData.UID}-heels-reapply"))
            UiService.SetUITask(ApplyHeels);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.Heels]);

        ImGui.TableNextColumn();
        ImGui.Text("TitleData");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Honorific]), id: $"{Sundesmo.UserData.UID}-honorific-reapply"))
            UiService.SetUITask(ApplyHonorific);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.Honorific]);

        ImGui.TableNextColumn();
        ImGui.Text("Moodles");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Moodles]), id: $"{Sundesmo.UserData.UID}-moodles-reapply"))
            UiService.SetUITask(ApplyMoodles);
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.Moodles]);

        ImGui.TableNextColumn();
        ImGui.Text("PetNames");
        ImGui.TableNextColumn();
        if (CkGui.IconTextButton(FAI.Sync, "Reapply", disabled: string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.PetNames]), id: $"{Sundesmo.UserData.UID}-pet-reapply"))
            UiService.SetUITask(_ipc.PetNames.ClearPetNamesByIdx(ObjIndex));
        ImGui.TableNextColumn();
        ImGui.Text(_appearanceData!.Data[IpcKind.PetNames]);
    }
}
