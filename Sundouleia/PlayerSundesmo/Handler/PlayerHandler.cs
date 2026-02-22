using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs.Enums;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Data;

namespace Sundouleia.Pairs;

/// <summary>
///     Handles the cached data for a player, and their current rendered status. <para />
///     The Rendered status should be handled differently from the alterations. <para />
///     Every pair has their own instance of this data.
/// </summary>
public class PlayerHandler : DisposableMediatorSubscriberBase
{
    private readonly AccountConfig _config;
    private readonly FileCacheManager _fileCache;
    private readonly FileDownloader _downloader;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    // Parent References
    private RedrawManager _redrawer { get; init; }
    public Sundesmo Sundesmo { get; init; }

    // Task Control
    private CancellationTokenSource _runtimeCTS = new();
    private CancellationTokenSource _dlWaiterCTS = new();
    private Task? _dlWaiterTask;
    
    private unsafe Character* _player = null;
    // cached data for appearance.
    private Guid _tempCollection;
    // Could maybe include a second copy if this for the filtered output, but for now filter them on accept.
    private Dictionary<string, ValidFileHash> _moddedFiles = [];
    private Dictionary<string, FileSwapData>  _swappedFiles = [];
    private Guid _tempProfile; // CPlus temp profile id.
    private IpcDataPlayerCache? _appearanceData = null;

    private readonly SemaphoreSlim _dataLock = new(1, 1);
    private bool _hasReplacements => _moddedFiles.Count > 0 || _swappedFiles.Count > 0;
    private bool _hasAlterations => _hasReplacements || _appearanceData is not null;
    public bool BlockVisualApplication => _appearanceData is null || !IsRendered || _config.ConnectionKind is ConnectionKind.StreamerMode;
    public bool BlockModApplication => _config.ConnectionKind is ConnectionKind.StreamerMode || !_hasReplacements;

    public PlayerHandler(Sundesmo sundesmo, RedrawManager redrawer, ILogger<PlayerHandler> logger, 
        SundouleiaMediator mediator, AccountConfig config, FileCacheManager fileCache, 
        FileDownloader downloads, IpcManager ipc, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _redrawer = redrawer;
        Sundesmo = sundesmo;

        _config = config;
        _fileCache = fileCache;
        _downloader = downloads;
        _ipc = ipc;
        _watcher = watcher;

        // Listen to Penumbra init & dispose methods to re-assign collections.
        Mediator.Subscribe<PenumbraInitialized>(this, async _ =>
        {
            // Do nothing if not yet rendered.
            if (!IsRendered) 
                return;
            // If rendered, create and assign to the temp collection if not already done.
            await TryCreateAssignTempCollection().ConfigureAwait(false);
            // If there is replacement data, reapply it.
            await ApplyMods().ConfigureAwait(false);
        });

        Mediator.Subscribe<PenumbraDisposed>(this, _ => _tempCollection = Guid.Empty);
        Mediator.Subscribe<HonorificReady>(this, async _ =>
        {
            if (_config.ConnectionKind is ConnectionKind.StreamerMode) return;
            if (!IsRendered || string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Honorific])) return;
            await ApplyHonorific().ConfigureAwait(false);
        });
        Mediator.Subscribe<PetNamesReady>(this, async _ =>
        {
            if (_config.ConnectionKind is ConnectionKind.StreamerMode) return;
            if (!IsRendered || string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.PetNames])) return;
            await ApplyPetNames().ConfigureAwait(false);
        });

        Mediator.Subscribe<WatchedObjectCreated>(this, msg => MarkVisibleForAddress(msg.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, msg => UnrenderPlayer(msg.Address));
    }
    // Public Accessors.
    public Character DataState { get { unsafe { return *_player; } } }
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ushort ObjIndex => IsRendered ? _player->ObjectIndex : ushort.MaxValue;
    public unsafe ulong EntityId => IsRendered ? _player->EntityId : 0;
    public unsafe ulong GameObjectId => IsRendered ? _player->GetGameObjectId().ObjectId : ulong.MaxValue;
    public unsafe IntPtr DrawObjAddress => (nint)_player->DrawObject;
    public unsafe ulong RenderFlags => (ulong)_player->RenderFlags;
    public unsafe bool HasModelInSlotLoaded => ((CharacterBase*)_player->DrawObject)->HasModelInSlotLoaded != 0;
    public unsafe bool HasModelFilesInSlotLoaded => ((CharacterBase*)_player->DrawObject)->HasModelFilesInSlotLoaded != 0;

    public string NameString { get; private set; } = string.Empty; // Manual, to assist timeout tasks.
    public string NameWithWorld { get; private set; } = string.Empty; // Manual, to assist timeout tasks.
    public unsafe bool IsRendered => _player != null;

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
            Mediator.Publish(new SundesmoPlayerRendered(this, Sundesmo));
            Mediator.Publish(new FolderUpdateSundesmos());
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
        // If we were in any timeout, end the timeout.
        Sundesmo.ExitLimboState();

        // Set the game data.
        _player = (Character*)address;
        NameString = _player->NameString;
        NameWithWorld = _player->GetNameWithWorld();
        
        // Notify other services.
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] rendered!", LoggerType.PairHandler);
        Mediator.Publish(new SundesmoPlayerRendered(this, Sundesmo));
        
        // ReInitialize our alterations for becoming visible again.
        ReInitializeInternal().ConfigureAwait(false);
    }

    private async Task ReInitializeInternal()
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Create/Assign this ObjectIdx to the Penumbra Temp Collection if necessary.
            await TryCreateAssignTempCollection().ConfigureAwait(false);

            // If they are online and have alterations, reapply them. Otherwise, exit.
            if (!Sundesmo.IsOnline || (!_hasAlterations))
            {
                Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()}) skipped ReInit: [IsOnline: {Sundesmo.IsOnline}, HasAlterations: {_hasAlterations}.", LoggerType.PairHandler);
                return;
            }

            // Skip if in streamer mode.
            if (_config.ConnectionKind is ConnectionKind.StreamerMode)
                return;

            // Await until we know the player has absolutely finished loading in.
            await WaitUntilValidDrawObject().ConfigureAwait(false);

            Logger.LogDebug($"[{Sundesmo.GetNickAliasOrUid()}] is fully loaded, reapplying alterations.", LoggerType.PairHandler);
            await ReapplyAlterationsInternal().ConfigureAwait(false);
        }
        finally
        {
            _dataLock.Release();
        }
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

        // Unrendering should begin the Sundesmo's timeout process.
        Logger.LogDebug($"Marking {Sundesmo.GetNickAliasOrUid()} as unrendered @ [{address:X}]", LoggerType.PairHandler);
        Sundesmo.EnterLimboState();

        Mediator.Publish(new SundesmoPlayerUnrendered(address));
        Mediator.Publish(new FolderUpdateSundesmos());
    }

    private async Task TryCreateAssignTempCollection()
    {
        if (!IpcCallerPenumbra.APIAvailable)
            return;
        // Create new Collection if we need one.
        if (_tempCollection == Guid.Empty)
            _tempCollection = await _ipc.Penumbra.NewSundesmoCollection(Sundesmo.UserData.UID).ConfigureAwait(false);

        // If we are rendered, assign the collection
        if (IsRendered && _config.ConnectionKind is not ConnectionKind.StreamerMode)
            await _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).ConfigureAwait(false);
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
        if (RenderFlags == 2048) return false; // Render Flag "IsLoading" (2048 == 0b100000000000)
        if (HasModelInSlotLoaded) return false; // There are models that need to still be loaded into the DrawObject slots.
        if (HasModelFilesInSlotLoaded) return false; // There are model files that need to still be loaded into the DrawObject slots.
        return true;
    }
    #endregion Rendering

    #region Altaration Control

    /// <inheritdoc cref="RevertAlterations(string, nint, ushort, CancellationToken)"/>
    public async Task RevertAlterations(CancellationToken ct = default)
    {
        try
        {
            await _dataLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        try
        {
            // Revert penumbra collection and customize+ data if set.
            await RevertAssignedAlterations();

            // If not visible, skip all but GlamourerByName, since they won't be valid.
            if (!IsRendered)
            {
                // Revert glamourer by name if possible.
                if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
                    await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);
                return;
            }

            // Ensure we have a valid address before we process a revert.
            // (Helps safegaurd cases where a Sundesmo is disposed of before its handlers are finished)
            // (Can touch this up later)
            if (!CharaObjectWatcher.RenderedCharas.Contains(Address))
                return;

            // We can care about parallel execution here if we really want to but i dont care atm.
            await _ipc.PetNames.ClearPetNamesByIdx(ObjIndex).ConfigureAwait(false);
            await _ipc.Glamourer.ReleaseActor(ObjIndex).ConfigureAwait(false);
            await _ipc.Heels.RestoreUserOffset(ObjIndex).ConfigureAwait(false);
            await _ipc.Honorific.ClearTitleAsync(ObjIndex).ConfigureAwait(false);
            await _ipc.Moodles.ClearByPtr(Address).ConfigureAwait(false);
        }
        catch (AccessViolationException)
        {
            Logger.LogWarning($"RevertAlterations for {NameString}({Sundesmo.GetNickAliasOrUid()}) was cancelled.", LoggerType.PairHandler);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"RevertAlterations for {NameString}({Sundesmo.GetNickAliasOrUid()}) failed unexpectedly.", LoggerType.PairHandler);
        }
        finally
        {
            _dataLock.Release();
        }
    }
    /// <summary>
    ///     Reverts the rendered alterations on a player.<br/>
    ///     <b>This does not delete the alteration data. </b>
    /// </summary>
    private async Task RevertAlterations(IntPtr address, ushort objIdx, CancellationToken token)
    {
        try
        {
            await _dataLock.WaitAsync(token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        try
        {
            // Revert penumbra collection and customize+ data if set.
            await RevertAssignedAlterations();
             
            // If not visible, skip all but GlamourerByName, since they won't be valid.
            // Additionally, outside of the framework unloading, one of the conditions where an address
            // is not valid at time of unloading is when we logout, so we should also check if logged in.
            if (address == IntPtr.Zero)
            {
                // Revert glamourer by name if possible.
                if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
                    await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);
                return;
            }

            // We can care about parallel execution here if we really want to but i dont care atm.
            await _ipc.PetNames.ClearPetNamesByIdx(objIdx).ConfigureAwait(false);
            await _ipc.Glamourer.ReleaseActor(objIdx).ConfigureAwait(false);
            await _ipc.Heels.RestoreUserOffset(objIdx).ConfigureAwait(false);
            await _ipc.Honorific.ClearTitleAsync(objIdx).ConfigureAwait(false);
            await _ipc.Moodles.ClearByPtr(address).ConfigureAwait(false);

            _ipc.Penumbra.RedrawGameObject(objIdx);
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning($"RevertAlterations for {NameString}({Sundesmo.GetNickAliasOrUid()}) was cancelled.", LoggerType.PairHandler);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"RevertAlterations for {NameString}({Sundesmo.GetNickAliasOrUid()}) failed unexpectedly.", LoggerType.PairHandler);
        }
        finally
        {
            _dataLock.Release();
        }
    }

    /// <summary>
    ///     Revert alterations that entrusted us with an ID, that dont require other actor info. <para/>
    ///     <b>Currently Penumbra and Customize+</b>
    /// </summary>
    private async Task RevertAssignedAlterations()
    {
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        if (_tempCollection != Guid.Empty)
        {
            Logger.LogTrace($"Removing {NameString}(({Sundesmo.GetNickAliasOrUid()})'s temporary collection.", LoggerType.PairHandler);
            await _ipc.Penumbra.RemoveSundesmoCollection(_tempCollection).ConfigureAwait(false);
            _tempCollection = Guid.Empty;
        }
    }

    public async Task ClearAlterations(CancellationToken ct = default)
    {
        await _dataLock.WaitAsync(ct).ConfigureAwait(false);
        // Clear alterations.
        if (_tempCollection != Guid.Empty || _tempProfile != Guid.Empty)
        {
            Logger.LogError("You are clearing alterations prior to reverting them!\n" +
                "This will have consequences on the stability of your data sync!");
            Logger.LogError("If you are getting this, find out why it is happening!");
        }

        _moddedFiles.Clear();
        _swappedFiles.Clear();
        _appearanceData = null;
        _tempCollection = Guid.Empty;
        _tempProfile = Guid.Empty;

        _dataLock.Release();
    }

    // Ok to wrap this for pending redraws as we only ever call it from the sundesmo.
    public async Task UpdateAndApplyAlterations(NewModUpdates modChanges, IpcDataPlayerUpdate ipcChanges, bool isInitialData)
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Encapsulate this in a pending redraw as we will do so after this is completed.
            await _redrawer.RunOnPendingRedrawSlim(this, async () =>
            {
                // This currently doesnt handle reapplication entirely as the difference between removed and added should not redraw if the same
                var redrawType = RedrawKind.None;
                // Wrap this call as a pending redraw
                // If initial data, clear replacements and appearance data.
                if (isInitialData)
                {
                    // Check if existing modded files require redraw
                    if (_moddedFiles.Count > 0 || _swappedFiles.Count > 0)
                    {
                        var allPaths = _moddedFiles.Values.SelectMany(f => f.GamePaths).Concat(_swappedFiles.Values.SelectMany(f => f.GamePaths)).ToArray();
                        redrawType = NeedsRedraw(allPaths) ? RedrawKind.Full : RedrawKind.Reapply;
                    }

                    // Clear the dictionaries
                    _moddedFiles.Clear();
                    _swappedFiles.Clear();
                    // Check appearance data if present and we still don't need full redraw
                    if (redrawType < RedrawKind.Full && _appearanceData?.Data.Keys is { } keys)
                        if (keys.Contains(IpcKind.Glamourer) || keys.Contains(IpcKind.CPlus) || keys.Contains(IpcKind.ModManips))
                            redrawType |= RedrawKind.Reapply;
                    // Clear the appearance.
                    _appearanceData = null;
                }

                // Get the redraw data off the data updates.
                var visualDiff = UpdateDataIpc(ipcChanges);
                var modDiff = UpdateDataMods(modChanges);

                // Determine based on updates.
                if (redrawType < RedrawKind.Full)
                {
                    redrawType |= NeedsReapply(visualDiff) ? RedrawKind.Reapply : RedrawKind.None;
                    redrawType |= modDiff;
                }

                // Appply them
                await ApplyAlterations(visualDiff, modDiff is not RedrawKind.None).ConfigureAwait(false);
                return redrawType;
            });
        }
        finally
        {
            _dataLock.Release();
        }
    }

    // Ok to wrap this for pending redraws as we only ever call it from the sundesmo.
    public async Task UpdateAndApplyMods(NewModUpdates modChanges, string manipString)
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Encapsulate this in a pending redraw as we will do so after this is completed.
            await _redrawer.RunOnPendingRedrawSlim(this, async () =>
            {
                var redrawType = RedrawKind.None;
                // Update the manipString, and apply if changes occurred.
                if (!string.IsNullOrEmpty(manipString))
                {
                    // Update the Manips, if they change mark us for a redraw.
                    if (UpdateDataIpc(IpcKind.ModManips, manipString))
                    {
                        redrawType |= RedrawKind.Reapply;
                        await ApplyVisualsSingle(IpcKind.ModManips).ConfigureAwait(false);
                    }
                }

                // Update the mod data, and apply if changes occurred.
                redrawType |= UpdateDataMods(modChanges);
                await ApplyMods().ConfigureAwait(false);

                // Return the final redraw type.
                return redrawType;
            });
        }
        finally
        {
            _dataLock.Release();
        }
    }

    // Ok to wrap this for pending redraws as we only ever call it from the sundesmo.\
    public async Task UpdateAndApplyIpc(IpcDataPlayerUpdate ipcChanges)
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Encapsulate this in a pending redraw as we will do so after this is completed.
            await _redrawer.RunOnPendingRedrawSlim(this, async () =>
            {
                if (UpdateDataIpc(ipcChanges) is { } diff && diff is not IpcKind.None)
                {
                    await ApplyVisuals(diff).ConfigureAwait(false);
                    return NeedsReapply(diff) ? RedrawKind.Reapply : RedrawKind.None;
                }
                // Otherwise return none.
                return RedrawKind.None;
            });
        }
        finally
        {
            _dataLock.Release();
        }
    }

    // Ok to wrap this for pending redraws as we only ever call it from the sundesmo.
    public async Task UpdateAndApplyIpc(IpcKind kind, string newData)
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Encapsulate this in a pending redraw as we will do so after this is completed.
            await _redrawer.RunOnPendingRedrawSlim(this, async () =>
            {
                // If the updated IPC was changed, we should apply it.
                if (UpdateDataIpc(kind, newData))
                {
                    await ApplyVisualsSingle(kind).ConfigureAwait(false);
                    return NeedsReapply(kind) ? RedrawKind.Reapply : RedrawKind.None;
                }
                // Otherwise return none.
                return RedrawKind.None;
            });
        }
        finally
        {
            _dataLock.Release();
        }
    }

    public async Task ReapplyAlterations()
    {
        await _dataLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReapplyAlterationsInternal();
        }
        finally
        {
            _dataLock.Release();
        }
    }

    // Can add a force-redraw if needed.
    private async Task ReapplyAlterationsInternal()
    {
        await _redrawer.RunOnPendingRedrawSlim(this, async () =>
        {
            // Reapply the visuals and the mods.
            var visualsReapplied = await ReapplyVisuals().ConfigureAwait(false);
            var affectedPaths = await ApplyMods().ConfigureAwait(false);
            // The initial redraw type is defined by visuals.
            return NeedsRedraw(affectedPaths)
                ? RedrawKind.Full : NeedsReapply(visualsReapplied)
                    ? RedrawKind.Reapply : RedrawKind.None;
        });
    }
    #endregion Altaration Control

    #region Alteration Updates
    private RedrawKind UpdateDataMods(NewModUpdates modData)
    {
        var redrawType = modData.HasAnyChanges ? RedrawKind.Reapply : RedrawKind.None;
        // Remove all keys to remove.
        foreach (var hash in modData.HashesToRemove)
        {
            if (!_moddedFiles.Remove(hash, out var fileHash))
                continue;
            // Be sure to skip this check once we identify a file that needs a full redraw.
            if (redrawType is not RedrawKind.Full && NeedsRedraw(fileHash.GamePaths))
                redrawType = RedrawKind.Full;
        }

        foreach (var swap in modData.SwapsToRemove)
        {
            if (!_swappedFiles.Remove(swap, out var swappedFile))
                continue;

            // Be sure to skip this check once we identify a file that needs a full redraw.
            if (redrawType is not RedrawKind.Full && NeedsRedraw(swappedFile.GamePaths))
                redrawType = RedrawKind.Full;
        }

        // Filter out the replacements and file swaps that we have opted to filter out.
        modData = RemoveFilteredMods(modData);

        // Add / Update all new files and swaps.
        foreach (var file in modData.NewReplacements)
        {
            // Could run into an issue where the replaced path already present may require a redraw, but tackle that when the time comes.
            _moddedFiles[file.Hash] = file;

            // Be sure to skip this check once we identify a file that needs a full redraw.
            if (redrawType is not RedrawKind.Full && NeedsRedraw(file.GamePaths))
                redrawType = RedrawKind.Full;
        }
        foreach (var swap in modData.NewSwaps)
        {
            // Could run into an issue where the replaced path already present may require a redraw, but tackle that when the time comes.
            _swappedFiles[swap.SwappedPath] = swap;

            // Be sure to skip this check once we identify a file that needs a full redraw.
            if (redrawType is not RedrawKind.Full && NeedsRedraw(swap.GamePaths))
                redrawType = RedrawKind.Full;
        }

        Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data")));
        Logger.LogTrace($"New ModData from {NameString}({Sundesmo.GetNickAliasOrUid()}) " +
            $"[{modData.NewReplacements.Count} New ModFiles | {modData.NewSwaps.Count} New Swaps |" +
            $" {modData.HashesToRemove.Count} Removed ModFiles | {modData.SwapsToRemove.Count} Removed Swaps]." +
            $"(Still uploading ({modData.FilesUploading}) files to you.", LoggerType.PairHandler);

        // If we do not have any penumbraAPI or file cache, do not attempt downloads.
        if (!IpcCallerPenumbra.APIAvailable || !_fileCache.CacheFolderIsValid())
        {
            Logger.LogDebug("Either Penumbra IPC or File Cache is not available, cannot process mod data.", LoggerType.PairHandler);
            return RedrawKind.None;
        }
        
        // Set the uploading text based on if we have new files to upload or not.
        if (modData.FilesUploading > 0)
            Mediator.Publish(new FilesUploading(this));
        else
            Mediator.Publish(new FilesUploaded(this));

        // We should take any new mods to add, and enqueue them to the file downloader.
        _downloader.BeginDownloads(this, modData.NewReplacements);
        return redrawType;
    }

    public void ApplyModFiltersToCachedData()
    {
        _moddedFiles = _moddedFiles
            .Where(kvp =>
            {
                var f = kvp.Value;
                if (!Sundesmo.OwnPerms.AllowSounds && f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (!Sundesmo.OwnPerms.AllowAnimations && f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (!Sundesmo.OwnPerms.AllowVfx && f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                    return false;
                return true;
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        _swappedFiles = _swappedFiles
            .Where(kvp =>
            {
                var f = kvp.Value;
                if (!Sundesmo.OwnPerms.AllowSounds && f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (!Sundesmo.OwnPerms.AllowAnimations && f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (!Sundesmo.OwnPerms.AllowVfx && f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                    return false;
                return true;
            })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    private static readonly string[] RedrawPathTypes = [ "/face/", "/hair/", "/tail/", "/animation/", "/skeleton/" ];
    private bool NeedsRedraw(string[] gamePaths) => gamePaths.Any(p => RedrawPathTypes.Any(s => p.Contains(s, StringComparison.OrdinalIgnoreCase)));
    private bool NeedsReapply(IpcKind kinds) => kinds.HasAny(IpcKind.Glamourer | IpcKind.CPlus | IpcKind.ModManips);

    // Placeholder until we get a better system in place.
    private NewModUpdates RemoveFilteredMods(NewModUpdates modData)
    {
        if (Sundesmo.OwnPerms.AllowVfx && Sundesmo.OwnPerms.AllowSounds && Sundesmo.OwnPerms.AllowAnimations)
            return modData;

        Logger.LogTrace($"{NameString}({Sundesmo.GetNickAliasOrUid()}) Removing Filtered Mods", LoggerType.PairHandler);
        modData = modData with
        {
            NewReplacements = modData.NewReplacements
                .Where(f =>
                {
                    if (!Sundesmo.OwnPerms.AllowSounds && f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    if (!Sundesmo.OwnPerms.AllowAnimations && f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    if (!Sundesmo.OwnPerms.AllowVfx && f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return true;
                })
                .ToList(),

            NewSwaps = modData.NewSwaps
                .Where(f =>
                {
                    if (!Sundesmo.OwnPerms.AllowSounds && f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    if (!Sundesmo.OwnPerms.AllowAnimations && f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    if (!Sundesmo.OwnPerms.AllowVfx && f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return true;
                })
                .ToList()
        };
        return modData;
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
    // Maybe move up to the update and apply since nothing else uses it but idk.
    private async Task ApplyAlterations(IpcKind visualChanges, bool modsChanged)
    {
        // May want to revise how this redraw occurs.
        if (visualChanges is not IpcKind.None)
            await ApplyVisuals(visualChanges).ConfigureAwait(false);
        if (modsChanged)
            await ApplyMods().ConfigureAwait(false);
        Logger.LogInformation($"{NameString}({Sundesmo.GetNickAliasOrUid()}) applied alterations. (Visual: {visualChanges}, Mods: {modsChanged})", LoggerType.PairHandler);
    }

    // True if data was applied, false otherwise.
    private async Task<string[]> ApplyMods()
    {
        if (_config.ConnectionKind is ConnectionKind.StreamerMode)
            return [];

        // Sanity checks.
        if (!IsRendered)
        {
            Logger.LogWarning($"{NameString}({Sundesmo.GetNickAliasOrUid()}) is not rendered, skipping ApplyMods()", LoggerType.PairMods);
            return [];
        }
        if (_tempCollection == Guid.Empty)
        {
            Logger.LogWarning($"{NameString}({Sundesmo.GetNickAliasOrUid()}) has no Temp. Collection, skipping ApplyMods()", LoggerType.PairMods);
            return [];
        }
        if (!_hasReplacements)
        {
            Logger.LogWarning($"{NameString}({Sundesmo.GetNickAliasOrUid()}) has no replacements, skipping ApplyMods()", LoggerType.PairMods);
            return [];
        }

        // Wait for the mods to finish downloading (this can be interrupted by new mod data)
        var moddedPaths = await WaitForModDownloads().ConfigureAwait(false);

        // Ensure that file swaps take precedence over modded paths.
        foreach (var item in _swappedFiles.Values.ToList())
        {
            foreach (var gamePath in item.GamePaths)
            {
                Logger.LogTrace($"Adding file swap for {gamePath}: {item.SwappedPath}", LoggerType.PairMods);
                moddedPaths[gamePath] = item.SwappedPath;
            }
        }

        // Await for true render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return [];

        Logger.LogDebug($"Applying mod data for {NameString}({Sundesmo.GetNickAliasOrUid()}) with [{moddedPaths.Count}] modded paths.", LoggerType.PairMods);
        await _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).ConfigureAwait(false);
        await _ipc.Penumbra.ReapplySundesmoMods(_tempCollection, moddedPaths).ConfigureAwait(false);

        return moddedPaths.Keys.ToArray();
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
        var missingFiles = _downloader.GetExistingFromCache(_moddedFiles.Values, out var moddedDict, _runtimeCTS.Token);
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
                missingFiles = _downloader.GetExistingFromCache(_moddedFiles.Values, out moddedDict, _dlWaiterCTS.Token);

                // Delay ~2s with �15�20% random jitter to avoid synchronized polling
                var jitter = 0.15 + Random.Shared.NextDouble() * 0.05; // 0.15..0.20
                if (Random.Shared.Next(2) == 0) jitter = -jitter;
                var delay = TimeSpan.FromSeconds(2 * (1 + jitter));
                await Task.Delay(delay, _dlWaiterCTS.Token).ConfigureAwait(false);
            }

            _dlWaiterCTS.Token.ThrowIfCancellationRequested();

            Logger.LogDebug($"Missing files downloaded, applying mod data for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairMods);
        }
        catch (ObjectDisposedException)
        {
            // If this token was disposed the pair is disposing, so just return an empty dictionary.
            Logger.LogWarning($"Downloader interrupted during disposal for {NameString}({Sundesmo.GetNickAliasOrUid()}), returning empty application.", LoggerType.PairMods);
            return new Dictionary<string, string>();
        }
        catch (TaskCanceledException)
        {
            // Grab final partial progress. This could happen during unloading, so catch ObjectDisposedException as well.
            try
            {
                missingFiles = _downloader.GetExistingFromCache(_moddedFiles.Values, out moddedDict, _runtimeCTS.Token);
            }
            catch (ObjectDisposedException)
            {
                Logger.LogWarning($"Downloader interrupted during disposal for {NameString}({Sundesmo.GetNickAliasOrUid()}), returning empty application.", LoggerType.PairMods);
                return new Dictionary<string, string>();
            }
        }

        // Return the modded dictionary to apply.
        return moddedDict;
    }

    // True if data was applied, false otherwise. (useful for redraw)
    private async Task ApplyVisuals(IpcKind changes)
    {
        if (BlockVisualApplication)
            return;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);
        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return;

        Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()}) Reapplying Visuals", LoggerType.PairHandler);
        var toApply = new List<Task>();

        if (changes.HasAny(IpcKind.Glamourer))  toApply.Add(ApplyGlamourer());
        if (changes.HasAny(IpcKind.Heels))      toApply.Add(ApplyHeels());
        if (changes.HasAny(IpcKind.CPlus))      toApply.Add(ApplyCPlus());
        if (changes.HasAny(IpcKind.Honorific))  toApply.Add(ApplyHonorific());
        if (changes.HasAny(IpcKind.Moodles))    toApply.Add(ApplyMoodles());
        if (changes.HasAny(IpcKind.ModManips))  toApply.Add(ApplyModManips());
        if (changes.HasAny(IpcKind.PetNames))   toApply.Add(ApplyPetNames());

        await Task.WhenAll(toApply).ConfigureAwait(false);
        Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()}) Applied Visuals: {string.Join("|", changes)}", LoggerType.PairHandler);
    }

    // True if we should redraw, false otherwise. (Should move this to the updater?)
    private async Task ApplyVisualsSingle(IpcKind kind)
    {
        if (BlockVisualApplication)
            return;

        // Await for final render.
        await WaitUntilValidDrawObject().ConfigureAwait(false);

        // Sanity Check.
        if (_runtimeCTS.Token.IsCancellationRequested)
            return;

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
        Logger.LogInformation($"{NameString}({Sundesmo.GetNickAliasOrUid()}) had a IPC change: {kind}", LoggerType.PairHandler);
    }

    /// <summary>
    ///     Returns the IpcKind's reapplied.
    /// </summary>
    private async Task<IpcKind> ReapplyVisuals()
    {
        if (BlockVisualApplication)
            return IpcKind.None;

        await WaitUntilValidDrawObject().ConfigureAwait(false);
        
        var toApply = new List<Task>();
        
        if (!string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.Glamourer])) toApply.Add(ApplyGlamourer());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Heels]))     toApply.Add(ApplyHeels());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))     toApply.Add(ApplyCPlus());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Honorific])) toApply.Add(ApplyHonorific());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Moodles]))   toApply.Add(ApplyMoodles());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.ModManips])) toApply.Add(ApplyModManips());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.PetNames]))  toApply.Add(ApplyPetNames());
        
        // Run in parallel.
        await Task.WhenAll(toApply).ConfigureAwait(false);
        Logger.LogDebug($"{NameString}({Sundesmo.GetNickAliasOrUid()}) Reapplied Visuals: {string.Join("|", _appearanceData.Data.Keys)}", LoggerType.PairHandler);
        return _appearanceData.Data.Keys.Aggregate(IpcKind.None, (current, next) => current | next);
    }
    #endregion Alteration Application

    #region Helpers
    private async Task ApplyGlamourer() => await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);
    private async Task ApplyHeels() => await _ipc.Heels.SetUserOffset(ObjIndex, _appearanceData!.Data[IpcKind.Heels]).ConfigureAwait(false);
    private async Task ApplyHonorific() => await _ipc.Honorific.SetTitleAsync(ObjIndex, _appearanceData!.Data[IpcKind.Honorific]).ConfigureAwait(false);
    private async Task ApplyMoodles() => await _ipc.Moodles.SetByPtr(Address, _appearanceData!.Data[IpcKind.Moodles]).ConfigureAwait(false);
    private async Task ApplyModManips() => await _ipc.Penumbra.SetSundesmoManipulations(_tempCollection, _appearanceData!.Data[IpcKind.ModManips]).ConfigureAwait(false);
    private Task ApplyPetNames()
    {
        var nickData = _appearanceData!.Data[IpcKind.PetNames];
        _ipc.PetNames.SetNamesByIdx(ObjIndex, nickData);
        return Task.CompletedTask;
    }
    private async Task ApplyCPlus()
    {
        if (string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]) && _tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            _tempProfile = await _ipc.CustomizePlus.ApplyTempProfile(this, _appearanceData.Data[IpcKind.CPlus]).ConfigureAwait(false);
        }
    }

    #endregion Ipc Helpers

    /// <summary>
    ///     Perform a true disposal on the handler, reverting rendered alterations,
    ///     clearing alteration data, and disposing of download and runtime CTS. <para/>
    ///     <b>Do not call this unless you are disposing the Sundesmo.</b>
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        IntPtr addr = IsRendered ? Address : IntPtr.Zero;
        ushort objIdx = IsRendered ? ObjIndex : (ushort)0;

        // Stop any actively running tasks.
        _dlWaiterCTS.SafeCancelDispose();
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

        var nickAliasOrUid = Sundesmo.GetNickAliasOrUid();
        var name = NameString;

        // Process off the disposal thread. (Avoids deadlocking on plugin shutdown)
        // Everything in here, if it errors, should not crash the game as it is fire and forget.
        _ = Task.Run(async () =>
        {
            try
            {
                // Revert assigned alterations regardless of the conditional state.
                await RevertAssignedAlterations();

                // If we are not zoning and not in a cutscene, run the revert with a 30s timeout.
                if (!PlayerData.IsZoning && !PlayerData.InCutscene)
                {
                    Logger.LogDebug($"{name}(({nickAliasOrUid}) is rendered, reverting by address/index.", LoggerType.PairHandler);
                    using var timeoutCTS = new CancellationTokenSource();
                    timeoutCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    await RevertAlterations(addr, objIdx, timeoutCTS.Token);
                }

                // Make sure we aren't leaving the semaphore hanging
                _dataLock.Wait();
                _dataLock.Dispose();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error reverting {name}({nickAliasOrUid} on shutdown: {ex}");
            }
            finally
            {
                // Clear internal data.
                _tempCollection = Guid.Empty;
                _moddedFiles.Clear();
                _swappedFiles.Clear();
                _tempProfile = Guid.Empty;
                _appearanceData = null;
                NameString = string.Empty;
                NameWithWorld = string.Empty;
                unsafe { _player = null; }
            }
        });
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

        if (CkGui.IconTextButton(FAI.Sync, "Glamourer Reapply Actor"))
        {
            if (IsRendered) _ipc.Glamourer.ReapplyActor(ObjIndex);
        }

        ImGui.SameLine();
        if (CkGui.IconTextButton(FAI.Sync, "Penumbra Redraw"))
        {
            if (IsRendered) _ipc.Penumbra.RedrawGameObject(ObjIndex);
        }

        using (var modReps = ImRaii.Table("sundesmo-mod-replacements", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter))
        {
            if (!modReps) return;
            ImGui.TableSetupColumn("Hash / Swapped Path");
            ImGui.TableSetupColumn("Affected Game Paths");
            ImGui.TableHeadersRow();

            foreach (var (hash, mod) in _moddedFiles)
            {
                ImGui.TableNextColumn();
                CkGui.HoverIconText(FAI.Hashtag, ImGuiColors.DalamudViolet.ToUint());
                CkGui.AttachToolTip(hash);
                ImGui.TableNextColumn();
                ImGui.Text(string.Join("\n", mod.GamePaths));
            }

            foreach (var (swappedPath, swap) in _swappedFiles)
            {
                ImGui.TableNextColumn();
                CkGui.ColorText(swappedPath, ImGuiColors.DalamudViolet);
                ImGui.TableNextColumn();
                ImGui.Text(string.Join("\n", swap.GamePaths));
            }
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
