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
///     Main difference with sundesmo and past Moon solutions is that sundesmo 
///     does not do hash comparisons between 2 full data updates. <para />
///     Instead, the <see cref="VisualDataUpdate"/> and <see cref="ModDataUpdate"/> 
///     recieved holds all pending changes to be applied to your stored Data 
///     of the same types.
/// </summary>
public class PlayerHandler : DisposableMediatorSubscriberBase
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly FileCacheManager _fileCache;
    private readonly FileDownloader _downloader;
    private readonly IpcManager _ipc;

    // Internal data to be handled. (still looking into the dowload / mod stuff)
    private CancellationTokenSource _downloadCTS = new();
    private CancellationTokenSource _newModsCTS = new(); // (could maybe conjoin these two?)
    private CancellationTokenSource _timeoutCTS = new();
    private Task? _downloadTask;
    private Task? _newModsTask;
    private Task? _timeoutTask;

    public PlayerHandler(Sundesmo sundesmo, ILogger<PlayerHandler> logger, SundouleiaMediator mediator,
        IHostApplicationLifetime lifetime, FileCacheManager fileCache, FileDownloader downloads,
        IpcManager ipc) : base(logger, mediator)
    {
        Sundesmo = sundesmo;

        _lifetime = lifetime;
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
            _tempCollection = await _ipc.Penumbra.CreateTempSundesmoCollection(Sundesmo.UserData.UID);
        });

        // Old subscriber here for class/job change?
        // Old subscribers here for combat/performance start/stop. Reapplies should fix this but yeah can probably just do redraw.

        unsafe
        {
            Mediator.Subscribe<WatchedObjectCreated>(this, msg =>
            {
                if (msg.Kind != OwnedObject.Player || Address != nint.Zero) return;
                // Check hash for match, if found, create!
                if (Sundesmo.Ident == SundouleiaSecurity.GetIdentHashByCharacterPtr(msg.Address))
                   ObjectRendered((Character*)msg.Address);
            });

            Mediator.Subscribe<WatchedObjectDestroyed>(this, msg =>
            {
                if (msg.Kind != OwnedObject.Player || Address == nint.Zero || msg.Address != Address)
                    return;
                ClearRenderedPlayer(msg.Kind);
            });
        }
    }

    public Sundesmo Sundesmo { get; init; }
    private unsafe Character* _player = null;

    // cached data for appearance.
    private Guid _tempCollection;
    private Dictionary<string, string> _replacements = []; // GamePath -> FilePath (maybe have internal manager idk)
    private Guid _tempProfile; // CPlus temp profile id.
    private IpcDataPlayerCache? _appearanceData = null;

    // Public Accessors.
    public Character DataState { get { unsafe { return *_player; } } }
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ulong EntityId => _player->EntityId;
    public unsafe ulong GameObjectId => _player->GetGameObjectId().Id;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public string PlayerName { get; private set; } = string.Empty; // Manual, to assist timeout tasks.
    public unsafe bool IsRendered => _player != null;

    public unsafe void ObjectRendered(Character* chara)
    {
        if (chara is null)
            throw new ArgumentNullException(nameof(chara));
        // Set the state, if this is a first time initialization, handle it.
        _player = chara;
        if (string.IsNullOrEmpty(PlayerName))
            FirstTimeInitialize();
        // Set name and log render.
        PlayerName = chara->NameString;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] rendered!", LoggerType.PairHandler);
        Mediator.Publish(new SundesmoPlayerRendered(this));
    }

    public unsafe void ClearRenderedPlayer(OwnedObject type)
    {
        _player = null;
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {type} unrendered!", LoggerType.PairHandler);
        Mediator.Publish(new SundesmoPlayerUnrendered(this));
        StartTimeoutTask();
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

    public void StartTimeoutTask()
    {
        // Avoid going offline and becoming invisible from colliding.
        if (_timeoutTask is not null && !_timeoutTask.IsCompleted)
            return;

        _timeoutCTS = _timeoutCTS.SafeCancelRecreate();
        _timeoutTask = Task.Run(async () =>
        {
            // Await the proper delay for data removal.
            await Task.Delay(TimeSpan.FromSeconds(10), _timeoutCTS.Token).ConfigureAwait(false);
            Logger.LogInformation($"{PlayerName}({Sundesmo.GetNickAliasOrUid()})'s data reverted due to timeout after 10s.", LoggerType.PairHandler);
            // Revert any applied data.
            if (_tempProfile != Guid.Empty)
            {
                await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
                _tempProfile = Guid.Empty;
            }
            if (!string.IsNullOrEmpty(_appearanceData?.Data[IpcKind.Glamourer]))
                await _ipc.Glamourer.ReleaseByName(PlayerName).ConfigureAwait(false);
             
            _replacements.Clear();
            _appearanceData = null;
            PlayerName = string.Empty;
            unsafe { _player = null; }
            // Notify the owner that we went poofy or whatever if we need to here.
            Mediator.Publish(new SundesmoTimedOut(this));
        });
    }

    public void StopTimeoutTask() => _timeoutCTS.SafeCancel();

    public void ReapplyAlterations()
    {
        // To Reapply, must be rendered and have non-null appearance data.
        if (!IsRendered)
            return;
        // Reapply the alterations.
        if (_appearanceData is not null)
            ReapplyVisuals().ConfigureAwait(false);
        if (_tempCollection != Guid.Empty && _replacements.Count > 0)
            ApplyModData().ConfigureAwait(false);
        // redraw for now, reapply later after glamourer implements methods for reapply.
        _ipc.Penumbra.RedrawGameObject(ObjIndex);
    }

    public void UpdateAndApplyFullData(RecievedModUpdate modData, IpcDataPlayerUpdate ipcData)
    {
        // 1) Maybe handle something with combat and performance, idk, deal with later.

        // 2) Need to store all data but not apply if not rendered yet.
        if (!IsRendered)
        {
            Mediator.Publish(new EventMessage(new(PlayerName, Sundesmo.UserData.UID, DataEventType.FullDataReceive,
                "Downloading but not applying data [NOT-RENDERED]")));
            Logger.LogWarning("Received data for this sundesmo but their object is not yet present!\n" +
                "With this new ObjectWatcher system this should never happen. If it does, determine why immidiately.");
            // Determine the pending changes to apply for both.
            // TODO: (Having multiple of these errors might lead to inconsistant states to maybe have a 'pendingData' object?
            // (spesifcally an issue for mods)
            _appearanceData ??= new();
            _appearanceData.UpdateCache(ipcData);
            return;
        }

        // 3) Player is rendered, so process the ipc & mods.
        ApplyIpcData(ipcData).ConfigureAwait(false);
        UpdateAndApplyModData(modData).ConfigureAwait(false);
        Logger.LogInformation($"Applied full data for {PlayerName}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
    }

    public async Task UpdateAndApplyModData(RecievedModUpdate modData)
    {
        if (!IpcCallerPenumbra.APIAvailable)
        {
            Mediator.Publish(new EventMessage(new(PlayerName, Sundesmo.UserData.UID, DataEventType.ReceivedDataDeclined, "Penumbra IPC Unavailable.")));
            Logger.LogDebug("Penumbra IPC is not available, cannot apply mod data.", LoggerType.PairHandler);
            return;
        }

        // Logger.LogDebug("Mod Data changes detected, processing comparisons.", LoggerType.PairHandler);



        // Check for any differenced in the modified paths to know what new data we have.
        // Logger.LogDebug("ModData update requests removing [X] files from temp collection. [Y] new files are to be added. [if any are awaiting a second send mention it here]", LoggerType.PairHandler);

        // By now the above method should have returned:
        // - What files we in the previous that are not in the current (backup safety net, but should never need)
        // - What hashes were in the ModData's REMOVEFROM group.
        // - What hashes were in the ModData's ADDTO group.
        // Logger.LogTrace("Removing outdated and requested removal files from collection.", LoggerType.PairHandler);
        
        // TODO: Replace this with an IPC call whenever we get the ability to add/remove changed items from a temporary mod.
        // - Remove Gamepaths from it that should be removed.
        // Log what is left to be done.
        // Logger.LogDebug("Removed [X] paths, Replaced [Y] paths, Adding [Z] paths.", LoggerType.PairHandler);
        
        // Determine here via checking the filecache which files we already have the replacement paths for the paths to add.

        // Log the fetched results. [NOTE: 'X of Y paths didnt have download links' will be removed later as they will not be provided in the callback.
        // Logger.LogDebug("Of the [X] new paths to add, [Y] were cached. Downloading remainder uncached if any.", LoggerType.PairHandler);
        var downloadLinks = 2;
        if (downloadLinks > 4)
        {
            // Cancel any current 'uploading' display.
            Mediator.Publish(new FileUploaded(this));
            // Push to event.
            Mediator.Publish(new EventMessage(new(PlayerName, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data.")));
            Logger.LogDebug($"Downloading {downloadLinks} files for {PlayerName}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
            // process the download manager here and await its completion or whatever.
            _downloadCTS = _downloadCTS.SafeCancelRecreate();
            var downloadToken = _downloadCTS.Token;

            // Make this fire-and-forget if we run into more problems than solutions.
            // (can pull this into a seperate call or something idk lol. We have a personalized download manager so why not use that XD)

        }

        // 4) Append the new data.
        // Logger.LogDebug("Missing files downloaded, setting updated IPC and reapplying.", LoggerType.PairHandler);
        // - Replace any existing gamepaths that have new paths.
        // - Add any new gamepaths that are not already present.

        // 5) Apply the new mod data if rendered.
        // Logger.LogInformation($"Updated mod data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        await ApplyModData().ConfigureAwait(false);   
    }

    private async Task ApplyModData()
    {
        if (!IsRendered || _tempCollection == Guid.Empty || _replacements.Count == 0)
        {
            // Logger.LogWarning($"[{Sundesmo.GetNickAliasOrUid()}] is not rendered or has no mod data to apply, skipping mod application.", LoggerType.PairHandler);
            return;
        }
        
        Logger.LogDebug($"Applying mod data for {PlayerName}({Sundesmo.GetNickAliasOrUid()})", LoggerType.PairHandler);
        await _ipc.Penumbra.ReapplySundesmoMods(_tempCollection, _replacements).ConfigureAwait(false);
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
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}] had a single IPC change ({kind})", LoggerType.PairHandler);
    }

    private async Task ApplyGlamourer()
    {
        Logger.LogDebug($"Applying glamourer state for {PlayerName}");
        await _ipc.Glamourer.ApplyBase64StateByIdx(ObjIndex, _appearanceData!.Data[IpcKind.Glamourer]).ConfigureAwait(false);
    }
    private async Task ApplyHeels()
    {
        Logger.LogDebug($"Setting heels offset for {PlayerName}");
        await _ipc.Heels.SetUserOffset(ObjIndex, _appearanceData!.Data[IpcKind.Heels]).ConfigureAwait(false);
    }
    private async Task ApplyHonorific()
    {
        Logger.LogDebug($"Setting honorific title for {PlayerName}");
        await _ipc.Honorific.SetTitleAsync(ObjIndex, _appearanceData!.Data[IpcKind.Honorific]).ConfigureAwait(false);
    }
    private async Task ApplyMoodles()
    {
        Logger.LogDebug($"Setting moodles status for {PlayerName}");
        await _ipc.Moodles.SetByPtr(Address, _appearanceData!.Data[IpcKind.Moodles]).ConfigureAwait(false);
    }
    private async Task ApplyModManips()
    {
        Logger.LogDebug($"Setting mod manipulations for {PlayerName}");
        await _ipc.Penumbra.SetSundesmoManipulations(_tempCollection, _appearanceData!.Data[IpcKind.ModManips]).ConfigureAwait(false);
    }
    private async Task ApplyPetNames()
    {
        var nickData = _appearanceData!.Data[IpcKind.PetNames];
        Logger.LogDebug($"{(string.IsNullOrEmpty(nickData) ? "Clearing" : "Setting")} pet nicknames for {PlayerName}");
        await _ipc.PetNames.SetNamesByIdx(ObjIndex, _appearanceData!.Data[IpcKind.PetNames]).ConfigureAwait(false);
    }
    private async Task ApplyCPlus()
    {
        if (string.IsNullOrEmpty(_appearanceData!.Data[IpcKind.CPlus]) && _tempProfile != Guid.Empty)
        {
            Logger.LogDebug($"Reverting CPlus profile for {PlayerName}");
            await _ipc.CustomizePlus.RevertTempProfile(_tempProfile).ConfigureAwait(false);
            _tempProfile = Guid.Empty;
        }
        else
        {
            Logger.LogDebug($"Applying CPlus profile for {PlayerName}");
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

    // NOTE: This can be very prone to crashing or inconsistant states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // store the name and address to reference removal properly.
        var name = PlayerName;
        // Stop any actively running tasks.
        _newModsCTS.SafeCancelDispose();
        _downloadCTS.SafeCancelDispose();
        _timeoutCTS.SafeCancelDispose();
        // dispose the downloader.
        _downloader.Dispose();
        // If they were valid before, parse out the event message for their disposal.
        if (!string.IsNullOrEmpty(name))
        {
            Logger.LogDebug($"Disposing [{name}] @ [{Address:X}]", LoggerType.PairHandler);
            Mediator.Publish(new EventMessage(new(name, Sundesmo.UserData.UID, DataEventType.Disposed, "Disposed")));
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
                using var timoutCTS = new CancellationTokenSource();
                timoutCTS.CancelAfter(TimeSpan.FromSeconds(30));
                await RevertAlterations(nickAliasOrUid, name, Address, ObjIndex, timoutCTS.Token).ConfigureAwait(false);
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
            _appearanceData = new();
            PlayerName = string.Empty;
            unsafe { _player = null; }
        }
    }
}
