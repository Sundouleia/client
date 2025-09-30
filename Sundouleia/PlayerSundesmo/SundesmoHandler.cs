using CkCommons;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Microsoft.Extensions.Hosting;
using Sundouleia.FileCache;
using Sundouleia.Interop;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Files;
using SundouleiaAPI.Data;
using System.Threading.Tasks;

namespace Sundouleia.Pairs;

// MAINTAINERS NOTE:
// It would actually be benificial to create a 'SundesmoMainHandler' and 'SundesmoSubHandler' (for minions / mounts / pets / ext)
// this would help reduce process time complexity and allow for framented data analysis with faster performance optimizations.
// also since only the player needs to manage download data it can handle it seperately.

// Example:
// - CharaCreated -> Player? -> SundesmoMainHandler -> Manage IPC + Penumbra + Mods + Downloads -> Reapply on application.
// - CharaCreated -> Pet? -> SundesmoSubHandler -> Manage IPC only, Redraw on application.

/// <summary>
///     Stores information about a pairing (Sundesmo) between 2 users. <para />
///     Main difference with sundesmo and past Moon solutions is that sundesmo 
///     does not do hash comparisons between 2 full data updates. <para />
///     Instead, the <see cref="VisualDataUpdate"/> and <see cref="ModDataUpdate"/> 
///     recieved holds all pending changes to be applied to your stored Data 
///     of the same types.
/// </summary>
public class SundesmoHandler : DisposableMediatorSubscriberBase
{
    private readonly IHostApplicationLifetime _lifetime;

    private readonly FileCacheManager _fileCache;
    private readonly FileDownloadManager _downloadManager; // Personalized for this sundesmo, created via factory.
    private readonly IpcManager _ipc;
    private readonly ServerConfigManager _configs;

    private Sundesmo Sundesmo { get; init; }
    // Internal data to be handled.
    private CancellationTokenSource _downloadCTS = new();
    private CancellationTokenSource _newModsCTS = new(); // (could maybe conjoin these two?)
    private CancellationTokenSource _newAppearanceCTS = new();
    private CancellationTokenSource _timeoutCTS = new();
    private Task? _downloadTask;
    private Task? _newModsTask;
    private Task? _newAppearanceTask;
    private Task? _timeoutTask;

    // Cached Sync Data for the sundesmo.
    private Guid _tempCollectionId;
    private Dictionary<string, string> _tempCollectionReplacements = []; // GamePath -> FilePath
    private Dictionary<OwnedObject, Guid?> _tempProfiles = []; // OwnedObject -> ProfileId
    private VisualDataUpdate _ipcData = new();

    // All of the sundesmo's game object pointers are managed here.
    private unsafe Character* _player = null;
    private unsafe GameObject* _minionOrMount = null;
    private unsafe GameObject* _pet = null;
    private unsafe GameObject* _Companion = null;

    public SundesmoHandler(ILogger<SundesmoHandler> logger, SundouleiaMediator mediator,
        Sundesmo sundesmo, IHostApplicationLifetime lifetime, FileCacheManager fileCache, 
        FileDownloadManager downloads, IpcManager ipc, ServerConfigManager configs) 
        : base(logger, mediator)
    {
        Sundesmo = sundesmo;
        _lifetime = lifetime;
        _fileCache = fileCache;
        _downloadManager = downloads;
        _ipc = ipc;
        _configs = configs;

        // Immidiately initialize the temporary collection for this sundesmo.
        _tempCollectionId = _ipc.Penumbra.CreateTempSundesmoCollection(Sundesmo.UserData.UID);

        // Maybe do something on zone switch? but not really sure lol. I would personally continue
        // any existing downloads we have.

        Mediator.Subscribe<PenumbraInitialized>(this, _ =>
        {
            // this just creates 'a collection' it does not assign anyone to it yet.
            _tempCollectionId = _ipc.Penumbra.CreateTempSundesmoCollection(Sundesmo.UserData.UID);
        });

        // Old subscriber here for class/job change?

        // Old subscribers here for combat/performance start/stop. Reapplies should fix this but yeah can probably just do redraw.
    }

    // ---- Current State Context Accessors ----
    private GameObject PlayerState { get { unsafe { return *(GameObject*)_player; } } }
    private GameObject MinionOrMountState { get { unsafe { return *_minionOrMount; } } }
    private GameObject PetState { get { unsafe { return *_pet; } } }
    private GameObject CompanionState { get { unsafe { return *_Companion; } } }
    private unsafe IntPtr MinionMountAddress => (nint)_minionOrMount;
    private unsafe IntPtr PetAddress => (nint)_pet;
    private unsafe IntPtr CompanionAddress => (nint)_Companion;

    // ----- Helper Public Accessor Properties -----
    public unsafe bool IsRendered => _player != null;
    public unsafe IntPtr Address => (nint)_player;
    public unsafe ushort ObjIndex => _player->ObjectIndex;
    public string PlayerName { get; private set; } = string.Empty; // Manually set so it can be used on timeouts.

    public unsafe void SundesmoObjectRendered(OwnedObject type, Character* chara)
    {
        if (chara is null)
            throw new ArgumentNullException(nameof(chara));

        switch (type)
        {
            case OwnedObject.Player:
                _player = chara;
                // handle first time initialization stuff.
                if (string.IsNullOrEmpty(PlayerName))
                    FirstTimeInitialize();
                PlayerName = chara->NameString;
                break;
            
            case OwnedObject.MinionOrMount:
                _minionOrMount = (GameObject*)chara;
                break;
            
            case OwnedObject.Pet:
                _pet = (GameObject*)chara;
                break;
            
            case OwnedObject.Companion:
                _Companion = (GameObject*)chara;
                break;
        }
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {type} rendered!", LoggerType.PairHandler);
    }

    private void FirstTimeInitialize()
    {
        Logger.LogTrace($"First-Time-Initialize [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
        // If we have title data we should apply them when the the client downloads / enables it.
        Mediator.Subscribe<HonorificReady>(this, async _ =>
        {
            if (string.IsNullOrEmpty(_ipcData.Player.Data[IpcKind.Honorific])) return;
            Logger.LogTrace($"Applying [{Sundesmo.GetNickAliasOrUid()}]'s cached Honorific title.");
            await _ipc.Honorific.SetTitleAsync(this, _ipcData.Player.Data[IpcKind.Honorific]).ConfigureAwait(false);
        });
        // If we have cached petnames data we should apply them when the the client downloads / enables it.
        Mediator.Subscribe<PetNamesReady>(this, async _ =>
        {
            if (string.IsNullOrEmpty(_ipcData.Player.Data[IpcKind.PetNames])) return;
            Logger.LogTrace($"Applying [{Sundesmo.GetNickAliasOrUid()}]'s cached Pet Nicknames.");
            await _ipc.PetNames.SetPetNamesByPtr(Address, _ipcData.Player.Data[IpcKind.PetNames]).ConfigureAwait(false);
        });
        // Assign the temporary penumbra collection if not already set.
        _ipc.Penumbra.AssignSundesmoCollection(_tempCollectionId, ObjIndex).GetAwaiter().GetResult();
    }

    public unsafe void ClearRenderedPlayer(OwnedObject type)
    {
        switch (type)
        {
            // do not clear the player name, keep it there for validity on initializations.
            case OwnedObject.Player:
                _player = null;
                _minionOrMount = null;
                _pet = null;
                _Companion = null;
                break;
            case OwnedObject.MinionOrMount:
                _minionOrMount = null;
                break;
            case OwnedObject.Pet:
                _pet = null;
                break;
            case OwnedObject.Companion:
                _Companion = null;
                break;
        }
        Logger.LogInformation($"[{Sundesmo.GetNickAliasOrUid()}]'s {type} unrendered!", LoggerType.PairHandler);
    }

    public void UpdateAndApplyFullData(ModDataUpdate modData, VisualDataUpdate ipcData)
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
            _ipcData = ipcData;
            return;
        }

        // 3) Player is rendered, so process the ipc & mods.
        UpdateAndApplyIpcData(ipcData);
        UpdateAndApplyModData(modData).ConfigureAwait(false);
        Logger.LogInformation($"Applied full data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
    }

    public async Task UpdateAndApplyModData(ModDataUpdate modData)
    {
        if (!IpcCallerPenumbra.APIAvailable)
        {
            Mediator.Publish(new EventMessage(new(PlayerName, Sundesmo.UserData.UID, DataEventType.ReceivedDataDeclined, "Penumbra IPC Unavailable.")));
            Logger.LogDebug("Penumbra IPC is not available, cannot apply mod data.", LoggerType.PairHandler);
            return;
        }

        Logger.LogDebug("Mod Data changes detected, processing comparisons.", LoggerType.PairHandler);



        // Check for any differenced in the modified paths to know what new data we have.
        Logger.LogDebug("ModData update requests removing [X] files from temp collection. [Y] new files are to be added. [if any are awaiting a second send mention it here]", LoggerType.PairHandler);

        // By now the above method should have returned:
        // - What files we in the previous that are not in the current (backup safety net, but should never need)
        // - What hashes were in the ModData's REMOVEFROM group.
        // - What hashes were in the ModData's ADDTO group.
        Logger.LogTrace("Removing outdated and requested removal files from collection.", LoggerType.PairHandler);
        // TODO: Replace this with an IPC call whenever we get the ability to add/remove changed items from a temporary mod.
        // - Remove Gamepaths from it that should be removed.
        // Log what is left to be done.
        Logger.LogDebug("Removed [X] paths, Replaced [Y] paths, Adding [Z] paths.", LoggerType.PairHandler);
        // Determine here via checking the filecache which files we already have the replacement paths for the paths to add.

        // Log the fetched results. [NOTE: 'X of Y paths didnt have download links' will be removed later as they will not be provided in the callback.
        Logger.LogDebug("Of the [X] new paths to add, [Y] were cached. Downloading remainder uncached if any.", LoggerType.PairHandler);
        var downloadLinks = 2;
        if (downloadLinks > 0)
        {
            // Cancel any current 'uploading' display.
            Mediator.Publish(new FileUploaded(this));
            // Push to event.
            Mediator.Publish(new EventMessage(new(PlayerName, Sundesmo.UserData.UID, DataEventType.ModDataReceive, "Downloading mod data.")));
            Logger.LogDebug($"Downloading {downloadLinks} files for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
            // process the download manager here and await its completion or whatever.
            _downloadCTS = _downloadCTS.SafeCancelRecreate();
            var downloadToken = _downloadCTS.Token;

            // Make this fire-and-forget if we run into more problems than solutions.
            // (can pull this into a seperate call or something idk lol. We have a personalized download manager so why not use that XD)

        }

        // 4) Append the new data.
        Logger.LogDebug("Missing files downloaded, setting updated IPC and reapplying.", LoggerType.PairHandler);
        // - Replace any existing gamepaths that have new paths.
        // - Add any new gamepaths that are not already present.
        
        Logger.LogInformation($"Updated mod data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);

        // 5) Reapply the replaced files to the temporary collection. (fix when IPC allows for methods to do this.
        if (IsRendered)
        {
            Logger.LogInformation($"{PlayerName} is rendered, applying mod changes.", LoggerType.PairHandler);
            // NOTE: Old sundeouleia for some reason made this reassign the same temporary collection every time and i have no idea why they did this.
            await _ipc.Penumbra.ReapplySundesmoMods(_tempCollectionId, _tempCollectionReplacements);
            return;
        }
        Logger.LogWarning($"{PlayerName} is not rendered, skipping mod application.", LoggerType.PairHandler);
    }

    public async Task UpdateAndApplyIpcData(VisualDataUpdate ipcData)
    {
        // 1) Recreate the appearance token.
        _newAppearanceCTS = _newAppearanceCTS.SafeCancelRecreate();

        // 2) See what is new to update.
        var changedUpdates = _ipcData.ApplyChanged(ipcData);

        // if the token was cancelled, abort.
        _newAppearanceCTS.Token.ThrowIfCancellationRequested();

        // Queue up the tasks in list fashion.
        var tasks = new List<Task>()
        {
            ApplyIpcDataAsync(OwnedObject.Player, Address, PlayerState, _ipcData.Player, changedUpdates[OwnedObject.Player], _newAppearanceCTS.Token),
            ApplyIpcDataAsync(OwnedObject.MinionOrMount, MinionMountAddress, MinionOrMountState, _ipcData.MinionMount, changedUpdates[OwnedObject.MinionOrMount], _newAppearanceCTS.Token),
            ApplyIpcDataAsync(OwnedObject.Pet, PetAddress, PetState, _ipcData.Pet, changedUpdates[OwnedObject.Pet], _newAppearanceCTS.Token),
            ApplyIpcDataAsync(OwnedObject.Companion, CompanionAddress, CompanionState, _ipcData.Companion, changedUpdates[OwnedObject.Companion], _newAppearanceCTS.Token)
        };
        // Run in parallel.
        await Task.WhenAll(tasks).ConfigureAwait(false);

        Logger.LogInformation($"Updated IPC data for [{Sundesmo.GetNickAliasOrUid()}]", LoggerType.PairHandler);
    }

    // Intended to be super fast and instant.
    public void ApplyIpcSingle(OwnedObject obj, IpcKind kind, string newData)
    {
        // Update the changes.
        if (!_ipcData.TryApplyKind(obj, kind, newData))
            return;
        // If not rendered, return.
        if (!IsRendered)
            return;
        // Apply the changes async but do not await.
        ApplyIpcDataAsync(obj, Address, obj switch
        {
            OwnedObject.Player => PlayerState,
            OwnedObject.MinionOrMount => MinionOrMountState,
            OwnedObject.Pet => PetState,
            OwnedObject.Companion => CompanionState,
            _ => default!
        }, _ipcData switch
        {
            { Player: var p } when obj == OwnedObject.Player => p,
            { MinionMount: var m } when obj == OwnedObject.MinionOrMount => m,
            { Pet: var pet } when obj == OwnedObject.Pet => pet,
            { Companion: var c } when obj == OwnedObject.Companion => c,
            _ => default!
        }, kind, CancellationToken.None).ConfigureAwait(false);
        Logger.LogInformation($"Applied single IPC change for [{Sundesmo.GetNickAliasOrUid()}] - {obj} : {kind}", LoggerType.PairHandler);
    }

    private async Task ApplyIpcDataAsync(OwnedObject obj, IntPtr addr, GameObject state, ObjectIpcData ipc, IpcKind toUpdate, CancellationToken token)
    {
        if (addr == IntPtr.Zero) return;

        // Process Updates.
        if (toUpdate.HasAny(IpcKind.CPlus))
        {
            if (string.IsNullOrEmpty(ipc.Data[IpcKind.CPlus]) && _tempProfiles.TryGetValue(obj, out var scaleId))
            {
                await _ipc.CustomizePlus.RevertTempProfile(_tempProfiles[obj].GetValueOrDefault()).ConfigureAwait(false);
                _tempProfiles.Remove(obj);
            }
            else
            {
                _tempProfiles[obj] = await _ipc.CustomizePlus.ApplyTempProfile(this, state, ipc.Data[IpcKind.CPlus]).ConfigureAwait(false);
            }
        }
        if (toUpdate.HasAny(IpcKind.Glamourer))
            await _ipc.Glamourer.ApplyBase64StateByIdx(state.ObjectIndex, ipc.Data[IpcKind.Glamourer]).ConfigureAwait(false);

        // If not player, ret.
        if (obj is not OwnedObject.Player)
            return;

        // Apply rest for player.
        if (toUpdate.HasAny(IpcKind.Heels))
            await _ipc.Heels.SetUserOffset(this, ipc.Data[IpcKind.Heels]).ConfigureAwait(false);

        if (toUpdate.HasAny(IpcKind.Honorific))
            await _ipc.Honorific.SetTitleAsync(this, ipc.Data[IpcKind.Honorific]).ConfigureAwait(false);

        if (toUpdate.HasAny(IpcKind.Moodles))
            await _ipc.Moodles.SetByPtr(Address, ipc.Data[IpcKind.Moodles]).ConfigureAwait(false);

        if (toUpdate.HasAny(IpcKind.ModManips))
            await _ipc.Penumbra.SetSundesmoManipulations(_tempCollectionId, ipc.Data[IpcKind.Mods]).ConfigureAwait(false);

        if (toUpdate.HasAny(IpcKind.PetNames))
            await _ipc.PetNames.SetPetNamesByPtr(Address, ipc.Data[IpcKind.PetNames]).ConfigureAwait(false);
    }

    public async Task RevertAlterationDataAsync(OwnedObject objType, string playerName, CancellationToken token)
    {
        if (Address == nint.Zero) return;
        // grab the cplus profile if any. CPlus wont process invalid outputs anyways so its fine.
        bool cPlusRemoved = _tempProfiles.Remove(objType, out var profileId);
        // Perform the revert.
        await (objType switch
        {
            OwnedObject.Player => RevertPlayer(objType),
            OwnedObject.MinionOrMount => RevertNonPlayer(objType, MinionOrMountState),
            OwnedObject.Pet => RevertNonPlayer(objType, PetState),
            OwnedObject.Companion => RevertNonPlayer(objType, CompanionState),
            _ => Task.CompletedTask
        }).ConfigureAwait(false);

        // Helper sub-funcs
        async Task RevertPlayer(OwnedObject type)
        {
            Logger.LogDebug($"Reverting {playerName}'s alteration data", LoggerType.PairHandler);
            await _ipc.Glamourer.ReleaseActor(this, PlayerState).ConfigureAwait(false);
            await _ipc.Heels.RestoreUserOffset(this).ConfigureAwait(false);
            await _ipc.CustomizePlus.RevertTempProfile(profileId).ConfigureAwait(false);
            await _ipc.Honorific.ClearTitleAsync(this).ConfigureAwait(false);
            await _ipc.Moodles.ClearByPtr(Address).ConfigureAwait(false);
            await _ipc.PetNames.ClearPetNamesByPtr(Address).ConfigureAwait(false);
        }
        async Task RevertNonPlayer(OwnedObject type, GameObject objectState)
        {
            Logger.LogDebug($"Reverting {playerName}'s owned {type} alterations.", LoggerType.PairHandler);
            await _ipc.CustomizePlus.RevertTempProfile(profileId).ConfigureAwait(false);
            await _ipc.Glamourer.ReleaseActor(this, objectState).ConfigureAwait(false);
            _ipc.Penumbra.RedrawGameObject(objectState.ObjectIndex);
        }
    }

    // NOTE: This can be very prone to crashing or inconsistant states!
    // Please be sure to look into it and verify everything is correct!
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // store the name and address to reference removal properly.
        var name = PlayerName;
        var address = Address;
        Logger.LogDebug($"Disposing [{name}] @ [{address:X}]", LoggerType.PairHandler);
        // Perform a safe disposal.
        try
        {
            // Stop any actively running tasks.
            _newModsCTS.SafeCancelDispose();
            _newAppearanceCTS.SafeCancelDispose();
            // dispose of our personal download manager.
            _downloadManager.Dispose();
            // null the rendered player.
            unsafe { _player = null; }

            // If they were valid before, parse out the event message for their disposal.
            if (!string.IsNullOrEmpty(name))
                Mediator.Publish(new EventMessage(new(name, Sundesmo.UserData.UID, DataEventType.Disposed, "Disposed")));

            // If the lifetime host is stopping, log it is and return.
            if (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                Logger.LogDebug("Host application is stopping, skipping cleanup.", LoggerType.PairHandler);
                return;
            }

            // Otherwise begin cleanup if valid to do so.
            if (!PlayerData.IsZoning && !PlayerData.InCutscene && !string.IsNullOrEmpty(name))
            {
                Logger.LogTrace($"Restoring state for {name}. ({Sundesmo.UserData.AliasOrUID})", LoggerType.PairHandler);
                Logger.LogTrace($"Removing temp collection for {name}. ({Sundesmo.UserData.AliasOrUID})", LoggerType.PairHandler);
                _ipc.Penumbra.RemoveSundesmoCollection(_tempCollectionId);
                // If they are no longer visible, revert states by their name.
                if (!IsRendered)
                {
                    Logger.LogTrace($"Disposed sundesmo {name} is not rendered, reverting by name where possible.", LoggerType.PairHandler);
                    _ipc.Glamourer.ReleaseByName(name).GetAwaiter().GetResult();
                    // Anything else that could be maybe?
                }
                // They are visible, so revert the rest of their customization data.
                else
                {
                    using var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    Logger.LogInformation($"There is CachedData for {name} that requires removal.");
                    foreach (var objType in Enum.GetValues<OwnedObject>())
                    {
                        try
                        {
                            RevertAlterationDataAsync(objType, name, cts.Token).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error reverting {objType} for {name}: {ex.Message}", LoggerType.PairHandler);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Error upon disposal of {name}: {ex.Message}", LoggerType.PairHandler);
        }
        finally
        {
            PlayerName = string.Empty;
            _tempCollectionReplacements.Clear();
            _ipcData = new();
        }
    }
}
