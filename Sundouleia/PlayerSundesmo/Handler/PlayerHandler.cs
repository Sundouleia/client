using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Microsoft.Extensions.Hosting;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files;
using Sundouleia.WebAPI.Utils;
using SundouleiaAPI.Data;
using System.Xml.Linq;

namespace Sundouleia.Pairs;

/// <summary>
///     Stores information about a pairing (Sundesmo) between 2 users. <para />
///     Handled a lot differently than what people may be used to seeing from past solutions.
/// </summary>
public class PlayerHandler : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileCache;
    private readonly FileDownloader _downloader;
    private readonly IpcManager _ipc;

    private CancellationTokenSource _downloadCTS = new();
    private CancellationTokenSource _newModsCTS = new(); // (could maybe conjoin these two?)
    private Task? _downloadTask;
    private Task? _newModsTask;

    public PlayerHandler(Sundesmo sundesmo, ILogger<PlayerHandler> logger, SundouleiaMediator mediator,
        FileCacheManager fileCache, FileDownloader downloads, IpcManager ipc) 
        : base(logger, mediator)
    {
        Sundesmo = sundesmo;

        _fileCache = fileCache;
        _downloader = downloads;
        _ipc = ipc;

        // If penumbra is already initialized create the temp collection here.
        if (IpcCallerPenumbra.APIAvailable && _tempCollection == Guid.Empty)
            _tempCollection = _ipc.Penumbra.CreateTempSundesmoCollection(Sundesmo.UserData.UID).GetAwaiter().GetResult();

        // Maybe do something on zone switch? but not really sure lol. I would personally continue
        // any existing downloads we have.

        // this just creates 'a collection' it does not assign anyone to it yet.
        Mediator.Subscribe<PenumbraInitialized>(this, async _ =>
        {
            if (_tempCollection == Guid.Empty)
                _tempCollection = await _ipc.Penumbra.CreateTempSundesmoCollection(Sundesmo.UserData.UID);
        });

        // Old subscriber here for class/job change?
        // Old subscribers here for combat/performance start/stop. Reapplies should fix this but yeah can probably just do redraw.

        unsafe
        {
            Mediator.Subscribe<WatchedObjectCreated>(this, msg =>
            {
                // do not create if they are not online or already created.
                if (Address != IntPtr.Zero || !Sundesmo.IsOnline) return;
                // If the Ident is not valid do not create.
                if (string.IsNullOrEmpty(Sundesmo.Ident)) return;
                // If a match can be found, set the player object.
                if (Sundesmo.Ident == SundouleiaSecurity.GetIdentHashByCharacterPtr(msg.Address))
                {
                    Logger.LogDebug($"Matched {Sundesmo.GetNickAliasOrUid()} to a created object @ [{msg.Address:X}]", LoggerType.PairHandler);
                    ObjectRendered((Character*)msg.Address);
                }
            });

            Mediator.Subscribe<WatchedObjectDestroyed>(this, msg =>
            {
                if (Address == IntPtr.Zero || msg.Address != Address) return;
                // Mark the player as unrendered, triggering the timeout.
                ObjectUnrendered();
            });
        }
    }

    public Sundesmo Sundesmo { get; init; }
    private unsafe Character* _player = null;

    // cached data for appearance.
    private Guid _tempCollection;
    private Dictionary<string, VerifiedModFile> _replacements = []; // Hash -> VerifiedFile with download link included if necessary.
    private Guid _tempProfile; // CPlus temp profile id.
    private IpcDataPlayerCache? _appearanceData = null;

    // Public Accessors.
    public Character DataState { get { unsafe { return *_player; } } }
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ulong EntityId => _player->EntityId;
    public unsafe ulong GameObjectId => _player->GetGameObjectId().ObjectId;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public string NameString { get; private set; } = string.Empty; // Manual, to assist timeout tasks.
    public unsafe bool IsRendered => _player != null;
    public bool HasAlterations => _appearanceData != null;

    /// <summary>
    ///     Fired whenever the character object is rendered in the game world. <para />
    ///     This is not to be linked to the appearance alterations in any shape or form.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    public unsafe void ObjectRendered(Character* chara)
    {
        if (chara is null) throw new ArgumentNullException(nameof(chara));
        // Set/Update the GameData.
        _player = chara;
        // If the Player NameString was empty, it needs to be initialized.
        if (string.IsNullOrEmpty(NameString))
            FirstTimeInitialize();

        // Set the NameString and log as rendered.
        NameString = chara->NameString;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] rendered!", LoggerType.PairHandler);
        Mediator.Publish(new SundesmoPlayerRendered(this));
        Mediator.Publish(new RefreshWhitelistMessage());
    }

    /// <summary>
    ///     Fired whenever the player is unrendered from the game world. <para />
    ///     Not linked to appearance alterations, but does begin its timeout if not set.
    /// </summary>
    private void ObjectUnrendered()
    {
        if (!IsRendered) return;
        // Clear the GameData.
        unsafe { _player = null; }
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] unrendered!", LoggerType.PairHandler);
        // Inform the sundesmo to begin its timeout for alterations.
        Sundesmo.TriggerTimeoutTask();
        Mediator.Publish(new RefreshWhitelistMessage());
    }

    /// <summary>
    ///     Removes all cached alterations from the player and reverts their state. <para />
    ///     Different reverts will occur based on the rendered state.
    /// </summary>
    public async Task ClearAlterations(CancellationToken ct)
    {
        // Regardless of rendered state, we should revert any C+ Data if we have any.
        if (_tempProfile != Guid.Empty)
        {
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }

        // If the player is rendered, await disposal with a set timeout.
        if (IsRendered)
        {
            await RevertAlterations(Sundesmo.GetNickAliasOrUid(), NameString, Address, ObjIndex, ct).ConfigureAwait(false);
        }
        else
        {
            // Revert any glamourer data by name if not rendered.
            if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
                await _ipc.Glamourer.ReleaseByName(NameString).ConfigureAwait(false);
        }

        // Reverting mods may happen based on state regardless? Not sure.


        // Clear out the alterations data (keep NameString alive so One-Time-Init does not re-fire.)
        _appearanceData = null;
        _replacements.Clear();
    }

    private void FirstTimeInitialize()
    {
        Logger.LogTrace($"First-Time-Initialize [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        
        // If we have title data we should apply them when the the client downloads / enables it.
        Mediator.Subscribe<HonorificReady>(this, async _ =>
        {
            if (string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Honorific])) return;
            Logger.LogTrace($"Applying [{Sundesmo.GetNickAliasOrUid()}]'s cached Honorific title.");
            await _ipc.Honorific.SetTitleAsync(ObjIndex, _appearanceData.Data[IpcKind.Honorific]).ConfigureAwait(false);
        });
        
        // If we have cached petnames data we should apply them when the the client downloads / enables it.
        Mediator.Subscribe<PetNamesReady>(this, async _ =>
        {
            if (string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.PetNames])) return;
            Logger.LogTrace($"Applying [{Sundesmo.GetNickAliasOrUid()}]'s cached Pet Nicknames.");
            await _ipc.PetNames.SetPetNamesByPtr(Address, _appearanceData.Data[IpcKind.PetNames]).ConfigureAwait(false);
        });
        
        // Assign the temporary penumbra collection if not already set.
        _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).GetAwaiter().GetResult();
    }

    public void ReapplyAlterations()
    {
        if (!IsRendered)
            return;

        // Reapply the alterations.
        if (_appearanceData is not null)
            ReapplyVisuals().ConfigureAwait(false);
        if (_tempCollection != Guid.Empty && _replacements.Count > 0)
        {
            _downloader.GetExistingFromCache(_replacements, out var moddedDict, CancellationToken.None);
            ApplyModData(moddedDict.ToDictionary(k => k.Key.GamePath, k => k.Value, StringComparer.Ordinal)).ConfigureAwait(false);
        }
        // redraw for now, reapply later after glamourer implements methods for reapply.
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
    }

    // Placeholder stuff for now.
    private void UpdateReplacements(NewModUpdates newData)
    {
        // Remove all keys to remove.
        foreach (var hash in newData.HashesToRemove)
            _replacements.Remove(hash);
        // Add all new files to add.
        foreach (var file in newData.FilesToAdd)
            _replacements[file.Hash] = file;
    }

    public void UpdateAndApplyFullData(NewModUpdates modData, IpcDataPlayerUpdate ipcData)
    {
        // 1) Maybe handle something with combat and performance, idk, deal with later.

        // 2) Need to store all data but not apply if not rendered yet.
        if (!IsRendered)
        {
            Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.FullDataReceive, "Downloading but not applying data [NOT-RENDERED]")));
            Logger.LogWarning("Data received from an object not currently present! If this happens, report / determine why immediately.");
            // Update the Mod Replacements with the new changes. (Does not apply them!)
            UpdateReplacements(modData);
            // Update the appearance data with the new ipc data.
            _appearanceData ??= new();
            _appearanceData.UpdateCache(ipcData);
            return;
        }

        // The old mare transfer bars would technically not work here as we would be both uploading files while downloading current ones at the same time.

        // 3) Player is rendered, so process the ipc & mods.
        ApplyIpcData(ipcData).ConfigureAwait(false);
        UpdateAndApplyModData(modData).ConfigureAwait(false);
        Logger.LogInformation($"Applied full data for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
    }

    public async Task UpdateAndApplyModData(NewModUpdates modData)
    {
        if (!IpcCallerPenumbra.APIAvailable)
        {
            Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.ReceivedDataDeclined, "Penumbra IPC Unavailable.")));
            Logger.LogDebug("Penumbra IPC is not available, cannot apply mod data.", LoggerType.PairHandler);
            return;
        }

        // Check for any differenced in the modified paths to know what new data we have.
        Logger.LogDebug($"NewModUpdate is removing {modData.HashesToRemove.Count} files, and adding {modData.FilesToAdd.Count} new files added | WaitingOnMore: {modData.NotAllSent}", LoggerType.PairHandler);

        UpdateReplacements(modData);
        Logger.LogTrace("Removing outdated and requested removal files from collection.", LoggerType.PairHandler);

        // Determine here via checking the FileCache which files we already have the replacement paths for the paths to add.
        // process the download manager here and await its completion or whatever.
        _downloadCTS = _downloadCTS.SafeCancelRecreate();
        var missingFiles = _downloader.GetExistingFromCache(_replacements, out var moddedDict, _downloadCTS.Token);

        // Make this fire-and-forget if we run into more problems than solutions.
        Logger.LogDebug($"Of the {moddedDict.Count} new paths to add, {missingFiles.Count} were not cached. Downloading remainder uncached if any.", LoggerType.PairHandler);
        // This should only be intended to download new files. Any files existing in the hashes should already be downloaded? Not sure.
        // Would need to look into this later, im too exhausted at the moment.
        // In essence, upon receiving newModData, those should be checked, but afterwards, these hashes and their respective file paths
        // in the cache should all be valid. If they are not, they should be cleared and reloaded.
        if (missingFiles.Count > 0)
        {
            int attempts = 0;

            while (missingFiles.Count > 0 && attempts++ <= 10 && !_downloadCTS.Token.IsCancellationRequested)
            {
                // await for the previous download task to complete.
                if (_downloadTask is not null && !_downloadTask.IsCompleted)
                {
                    Logger.LogDebug($"Finishing prior download task for {NameString} ({Sundesmo.GetNickAliasOrUid()}", LoggerType.PairFileCache);
                    await _downloadTask.ConfigureAwait(false);
                }

                // Begin the download task.
                Mediator.Publish(new EventMessage(new(NameString, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data.")));
                Logger.LogDebug($"Downloading {missingFiles.Count} files for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
                
                _downloadTask = Task.Run(async () => await _downloader.DownloadFiles(this, missingFiles, _downloadCTS.Token).ConfigureAwait(false), _downloadCTS.Token);

                await _downloadTask.ConfigureAwait(false);

                // If we wanted to back out, then back out.
                if (_downloadCTS.Token.IsCancellationRequested)
                {
                    Logger.LogWarning($"Download task for {NameString}({Sundesmo.GetNickAliasOrUid()}) was cancelled, aborting further downloads.", LoggerType.PairHandler);
                    return;
                }

                // Now that we have downloaded the files, check again to see which files we still need.
                missingFiles = _downloader.GetExistingFromCache(_replacements, out moddedDict, _downloadCTS.Token);

                // Delay 2s between download monitors?
                await Task.Delay(TimeSpan.FromSeconds(2), _downloadCTS.Token).ConfigureAwait(false);
            }
        }

        // 4) Append the new data.
        _downloadCTS.Token.ThrowIfCancellationRequested();
        Logger.LogDebug("Missing files downloaded, setting updated IPC and reapplying.", LoggerType.PairHandler);
        // 5) Apply the new mod data if rendered.
        Logger.LogInformation($"Updated mod data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        await ApplyModData(moddedDict.ToDictionary(k => k.Key.GamePath, k => k.Value, StringComparer.Ordinal)).ConfigureAwait(false);   
    }
     
    // Would need a way to retrieve the existing modded dictionary from the file cache or something here, not sure. We shouldnt need to download anything on a reapplication.
    private async Task ApplyModData(Dictionary<string, string> moddedPaths)
    {
        if (!IsRendered)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] is not rendered, skipping mod application.", LoggerType.PairHandler);
            return;
        }
        if (_tempCollection == Guid.Empty)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] does not have a temporary collection, skipping mod application.", LoggerType.PairHandler);
            return;
        }
        if (_replacements.Count == 0)
        {
            Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] is not rendered or has no mod data to apply, skipping mod application.", LoggerType.PairHandler);
            return;
        }
        
        Logger.LogDebug($"Applying mod data for {NameString}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
        // Maybe we need to do this for our other objects too? Im not sure, guess we'll find out and stuff.
        await _ipc.Penumbra.AssignSundesmoCollection(_tempCollection, ObjIndex).ConfigureAwait(false);
        await _ipc.Penumbra.ReapplySundesmoMods(_tempCollection, moddedPaths).ConfigureAwait(false);        
        // do a reapply here (but need to do a redraw for now)
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
    }

    public async Task ReapplyVisuals()
    {
        if (!IsRendered || _appearanceData is null)
            return;

        Logger.LogDebug($"Reapplying visual data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        var toApply = new List<Task>();
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Glamourer]))
            toApply.Add(ApplyGlamourer());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Heels]))
            toApply.Add(ApplyHeels());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.CPlus]))
            toApply.Add(ApplyCPlus());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Honorific]))
            toApply.Add(ApplyHonorific());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.Moodles]))
            toApply.Add(ApplyMoodles());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.ModManips]))
            toApply.Add(ApplyModManips());
        if (!string.IsNullOrEmpty(_appearanceData.Data[IpcKind.PetNames]))
            toApply.Add(ApplyPetNames());
        // Run in parallel.
        await Task.WhenAll(toApply).ConfigureAwait(false);
        // Redraw if necessary (Glamourer or CPlus)
        if (_appearanceData.Data.ContainsKey(IpcKind.Glamourer) || _appearanceData.Data.ContainsKey(IpcKind.CPlus))
            _ipc.Penumbra.RedrawGameObject(ObjIndex);
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had their visual data reapplied.", LoggerType.PairHandler);
    }

    public async Task ApplyIpcData(IpcDataPlayerUpdate newData)
    {
        // 0) Init appearance data if not yet made.
        _appearanceData ??= new();

        // 1) Apply changes and get what changed.
        var changes = _appearanceData.UpdateCache(newData);

        // 2) If nothing changed, or not present, return.
        if (changes is 0 || Address == IntPtr.Zero)
            return;

        Logger.LogDebug($"[{Sundesmo.GetNickAliasOrUid()}] has IPC changes to apply ({changes})", LoggerType.PairHandler);

        // 3) Initialize task list to perform all updates in parallel.
        var tasks = new List<Task>();
        if (changes.HasAny(IpcKind.Glamourer)) tasks.Add(ApplyGlamourer());
        if (changes.HasAny(IpcKind.Heels)) tasks.Add(ApplyHeels());
        if (changes.HasAny(IpcKind.CPlus)) tasks.Add(ApplyCPlus());
        if (changes.HasAny(IpcKind.Honorific)) tasks.Add(ApplyHonorific());
        if (changes.HasAny(IpcKind.Moodles)) tasks.Add(ApplyMoodles());
        if (changes.HasAny(IpcKind.ModManips)) tasks.Add(ApplyModManips());
        if (changes.HasAny(IpcKind.PetNames)) tasks.Add(ApplyPetNames());

        // 4) Process all updates.
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Redraw if necessary (Glamourer or CPlus)
        if (changes.HasAny(IpcKind.Glamourer | IpcKind.CPlus))
            _ipc.Penumbra.RedrawGameObject(ObjIndex);

        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had IPC changes applied ({changes})", LoggerType.PairHandler);
    }

    public async Task ApplyIpcSingle(IpcKind kind, string newData)
    {
        // 0) Init appearance data if not yet made.
        _appearanceData ??= new();

        // 1) Attempt to apply the single change.
        if (!_appearanceData.UpdateCacheSingle(kind, newData))
            return;
        
        // 2) Validate render state before applying.
        if (Address == IntPtr.Zero)
            return;
        
        // 3) Apply change.
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

        // Redraw if necessary (Glamourer or CPlus)
        if (kind is IpcKind.Glamourer or IpcKind.CPlus)
            _ipc.Penumbra.RedrawGameObject(ObjIndex);

        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had a single IPC change ({kind})", LoggerType.PairHandler);
    }

    private async Task ApplyGlamourer()
    {
        Logger.LogDebug($"Applying glamourer state for {NameString}", LoggerType.PairAppearance);
        await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);
    }
    private async Task ApplyHeels()
    {
        Logger.LogDebug($"Setting heels offset for {NameString}", LoggerType.PairAppearance);
        await _ipc.Heels.SetUserOffset(ObjIndex, _appearanceData!.Data[IpcKind.Heels]).ConfigureAwait(false);
    }
    private async Task ApplyHonorific()
    {
        Logger.LogDebug($"Setting honorific title for {NameString}", LoggerType.PairAppearance);
        await _ipc.Honorific.SetTitleAsync(ObjIndex, _appearanceData!.Data[IpcKind.Honorific]).ConfigureAwait(false);
    }
    private async Task ApplyMoodles()
    {
        Logger.LogDebug($"Setting moodles status for {NameString}", LoggerType.PairAppearance);
        await _ipc.Moodles.SetByPtr(Address, _appearanceData!.Data[IpcKind.Moodles]).ConfigureAwait(false);
    }
    private async Task ApplyModManips()
    {
        Logger.LogDebug($"Setting mod manipulations for {NameString}", LoggerType.PairAppearance);
        await _ipc.Penumbra.SetSundesmoManipulations(_tempCollection, _appearanceData!.Data[IpcKind.ModManips]).ConfigureAwait(false);
    }
    private async Task ApplyPetNames()
    {
        var nickData = _appearanceData!.Data[IpcKind.PetNames];
        Logger.LogDebug($"{(string.IsNullOrEmpty(nickData) ? "Clearing" : "Setting")} pet nicknames for {NameString}", LoggerType.PairAppearance);
        await _ipc.PetNames.SetNamesByIdx(ObjIndex, _appearanceData!.Data[IpcKind.PetNames]).ConfigureAwait(false);
    }
    private async Task ApplyCPlus()
    {
        if (string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]) && _tempProfile != Guid.Empty)
        {
            Logger.LogDebug($"Reverting CPlus profile for {NameString}", LoggerType.PairAppearance);
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            Logger.LogDebug($"Applying CPlus profile for {NameString}", LoggerType.PairAppearance);
            _tempProfile = await _ipc.CustomizePlus.ApplyTempProfile(this, _appearanceData.Data[IpcKind.CPlus]).ConfigureAwait(false);
        }
    }

    public async Task RevertAlterations(string aliasOrUid, string name, IntPtr address, ushort objIdx, CancellationToken token)
    {
        if (address == IntPtr.Zero)
            return;
        // We can care about parallel execution here if we really want to but i dont care atm.
        if (IpcCallerGlamourer.APIAvailable)
        {
            Logger.LogTrace($"Reverting {name}({aliasOrUid})'s Actor state", LoggerType.PairHandler);
            await _ipc.Glamourer.ReleaseActor(objIdx).ConfigureAwait(false);
        }
        if (IpcCallerHeels.APIAvailable)
        {
            Logger.LogTrace($"Restoring {name}({aliasOrUid})'s heels offset.", LoggerType.PairHandler);
            await _ipc.Heels.RestoreUserOffset(objIdx).ConfigureAwait(false);
        }
        if (IpcCallerCustomize.APIAvailable && _tempProfile != Guid.Empty)
        {
            Logger.LogTrace($"Reverting {name}({aliasOrUid})'s CPlus profile.", LoggerType.PairHandler);
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        if (IpcCallerHonorific.APIAvailable)
        {
            Logger.LogTrace($"Clearing {name}({aliasOrUid})'s honorific title.", LoggerType.PairHandler);
            await _ipc.Honorific.ClearTitleAsync(objIdx).ConfigureAwait(false);
        }
        if (IpcCallerMoodles.APIAvailable)
        {
            Logger.LogTrace($"Clearing {name}({aliasOrUid})'s moodles status.", LoggerType.PairHandler);
            await _ipc.Moodles.ClearByPtr(address).ConfigureAwait(false);
        }
        if (IpcCallerPetNames.APIAvailable)
        {
            Logger.LogTrace($"Clearing {name}({aliasOrUid})'s pet nicknames.", LoggerType.PairHandler);
            await _ipc.PetNames.ClearPetNamesByPtr(address).ConfigureAwait(false);
        }
    }

    // NOTE: This can be very prone to crashing or inconsistent states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // store the name and address to reference removal properly.
        var name = NameString;
        // Stop any actively running tasks.
        _newModsCTS.SafeCancelDispose();
        _downloadCTS.SafeCancelDispose();
        // dispose the downloader.
        _downloader.Dispose();
        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrEmpty(name))
        {
            Logger.LogDebug($"Disposing [{name}] @ [{Address:X}]", LoggerType.PairHandler);
            Mediator.Publish(new EventMessage(new(name, Sundesmo.UserData.UID, DataEventType.Disposed, "Disposed")));
        }

        // Do not dispose if the framework is unloading!
        // (means we are shutting down the game and cannot transmit calls to other ipcs without causing fatal errors!)
        if (Svc.Framework.IsFrameworkUnloading)
        {
            Logger.LogWarning($"Framework is unloading, skipping disposal for {name}({Sundesmo.GetNickAliasOrUid()})");
            return;
        }

        // Process off the disposal thread. (Avoids deadlocking on plugin shutdown)
        _ = SafeRevertOnDisposal(Sundesmo.GetNickAliasOrUid(), name).ConfigureAwait(false);
    }

    /// <summary>
    ///     What to fire whenever called on application shutdown instead of the normal disposal method.
    /// </summary>
    private async Task SafeRevertOnDisposal(string nickAliasOrUid, string name)
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
                await RevertAlterations(nickAliasOrUid, name, Address, ObjIndex, timeoutCTS.Token).ConfigureAwait(false);
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
}
