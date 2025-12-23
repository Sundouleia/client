using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.ModularActor;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;


// We need to know the name in order to revert some cases properly. (Additionally profiles associate per-player)
public sealed record AttachedActor(string Name, ModularActorData Data)
{
    public Guid CollectionId { get; set; } = Guid.Empty;
    public Guid? CplusProfile { get; set; } = null;
    public string CollectionName => $"SMA_{Data.Base.ID}";
    public string TempModName => $"SMA_Mod_{Data.Base.ID}";
}

// ---- GPOSE HANDLER ----
// RESPONSIBILITIES:
//  - Handle the logic for binding certain actors to SMAD's
//  - Manage the assignment and removal of IPC associations
//  - Monitor actor removal and GPOSE lifetime.
//  - Handle actor updates when the associated SMAD is updated.
public class GPoseHandler : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _mainConfig;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    private Dictionary<nint, AttachedActor> _attachedActors = new();

    public GPoseHandler(ILogger<GPoseHandler> logger, SundouleiaMediator mediator,
        MainConfig mainConfig, FileCacheManager cacheManager, SMAFileCacheManager smaFileCache, 
        IpcManager ipc, CharaObjectWatcher watcher)
        : base(logger, mediator)
    {
        _mainConfig = mainConfig;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _ipc = ipc;
        _watcher = watcher;

        Mediator.Subscribe<GPoseObjectDestroyed>(this, _ =>
        {
            Logger.LogInformation($"Handled @ GPoseObjectDestroyed: " +
                $"{string.Join("\n", _attachedActors.Select(kvp => $"{kvp.Value.Name} ({kvp.Key:X}) [SMAD: {kvp.Value.Data.Name}]"))}");

            // If the gpose object was handled, revert it.
            if (_attachedActors.Remove(_.Address, out var attached))
                DetachActorInternal(_.Address, attached).ConfigureAwait(false);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    public IReadOnlyDictionary<nint, AttachedActor> AttachedActors => _attachedActors;
    public unsafe bool HasGPoseTarget => GPoseTarget != null;
    public unsafe GameObject* GPoseTarget
    {
        get => TargetSystem.Instance()->GPoseTarget;
        set => TargetSystem.Instance()->GPoseTarget = value;
    }
    public unsafe nint GPoseTargetAddress => (nint)GPoseTarget;

    private unsafe GameObject GetSnapshot(nint objAddress) => *(GameObject*)objAddress;

    public async Task<AttachedActor?> AttachActor(ModularActorData data, nint address)
    {
        // Fail if not in gpose.
        if (!GameMain.IsInGPose())
            return null;

        // If the actor is already attached to a profile, be sure to revert that state first.
        if (_attachedActors.Remove(address, out var existing))
        {
            Logger.LogInformation($"Actor at {address:X} is already attached to SMAD {existing.Data.Name}, reverting first.");
            await DetachActorInternal(address, existing).ConfigureAwait(false);
        }

        // Create a new AttachedActor entry.
        var gameObj = GetSnapshot(address);

        AttachedActor entry = new(gameObj.NameString, data);
        entry.CollectionId = await _ipc.Penumbra.NewGPoseCollection(entry).ConfigureAwait(false);
        await _ipc.Penumbra.AssignToGPoseCollection(entry, gameObj.ObjectIndex).ConfigureAwait(false);

        return _attachedActors.TryAdd(address, entry) ? entry : null;
    }

    public async Task ApplyDataToActor(nint actorAddress)
    {
        if (actorAddress == nint.Zero)
            return;

        if (!_attachedActors.TryGetValue(actorAddress, out var entry))
        {
            Logger.LogWarning($"Tried to apply SMA Data to actor at {actorAddress:X} that is not attached.");
            return;
        }

        var gameObj = GetSnapshot(actorAddress);
        await ApplyDataInternal(actorAddress, gameObj.ObjectIndex, entry).ConfigureAwait(false);
    }

    private async Task ApplyDataInternal(nint actorAddress, ushort objIdx, AttachedActor entry)
    {
        // If it was correctly set, assign the actor.
        if (entry.CollectionId != Guid.Empty)
        {
            Logger.LogDebug($"SMA ({entry.Data.Name}) had assigned collection {entry.CollectionId}.");
            await _ipc.Penumbra.UpdateGPoseCollection(entry).ConfigureAwait(false);
        }

        Logger.LogDebug($"SMA ({entry.Data.Name}) Applying Glamourer Data.");
        await _ipc.Glamourer.ApplyStateByPtr(actorAddress, entry.Data.FinalGlamourData).ConfigureAwait(false);

        Logger.LogDebug($"SMA ({entry.Data.Name}) Applying CustomizePlus Data.");
        if (!string.IsNullOrEmpty(entry.Data.CPlusData))
            entry.CplusProfile = await _ipc.CustomizePlus.ApplyTempProfile(entry, objIdx).ConfigureAwait(false);

        // Finally Redraw the actor.
        Logger.LogInformation($"SMA ({entry.Data.Name}) Finished application, redrawing actor.");
        _ipc.Penumbra.RedrawGameObject(objIdx);
    }

    public async Task DetachActor(nint address)
    {
        if (_attachedActors.Remove(address, out var attachedData))
            await DetachActorInternal(address, attachedData).ConfigureAwait(false);
    }

    // Perform a full revert on the actor and remove their association from the handled SMAD.
    private async Task DetachActorInternal(nint actorAddress, AttachedActor attached)
    {
        // Revert the glamourer.
        Logger.LogInformation($"Reverting Glamourer for actor {attached.Name}.");
        await _ipc.Glamourer.ReleaseByName(attached.Name).ConfigureAwait(false);
        // If CPlus is valid, revert it.
        if (attached.CplusProfile != null)
        {
            Logger.LogInformation($"Reverting CustomizePlus for actor {attached.Name}.");
            await _ipc.CustomizePlus.RevertTempProfile(attached.CplusProfile).ConfigureAwait(false);
        }
        // Remove their association to the collection.

        // Bomb the temporary collection.
        Logger.LogInformation($"Removing Sundesmo collection for actor {attached.Name}.");
        await _ipc.Penumbra.RemoveGPoseCollection(attached).ConfigureAwait(false);
        unsafe
        {
            // Cast the address to a game object, and if it still valid, redraw the object.
            var actorObj = (GameObject*)actorAddress;
            if (actorObj != null)
                _ipc.Penumbra.RedrawGameObject(actorObj->ObjectIndex);
        }
    }

    // Might want to move this location or something?
    public async Task SpawnAndApplySMAData(ModularActorData data)
    {
        if (!IpcCallerBrio.APIAvailable)
            return;

        Logger.LogInformation($"Spawning SMA Actor for BaseId: {data.Base.ID}.");
        if (await _ipc.Brio.Spawn().ConfigureAwait(false) is not { } newActor)
            return;

        try
        {
            // Now wait for the actor to be fully loaded.
            Logger.LogInformation($"Waiting for spawned Actor: {newActor.Name.TextValue} to be fully loaded.");
            await _watcher.WaitForFullyLoadedGameObject(newActor.Address).ConfigureAwait(false);

            // Assign the entry to the data.
            if (await AttachActor(data, newActor.Address).ConfigureAwait(false) is not { } entry)
                throw new Exception("Failed to attach object to SMAD!");

            // Force an initial application of the data.
            Logger.LogInformation($"Applying SMA Data to spawned Actor: {newActor.Name.TextValue}.");
            await ApplyDataToActor(newActor.Address).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply SMA Data to Spawned Actor.");
            if (newActor != null && newActor.Address != nint.Zero)
                _ = Task.Run(() => DetachActor(newActor.Address));
        }
    }

    public async Task ApplySMAToGPoseTarget(ModularActorData data)
    {
        if (!HasGPoseTarget)
            return;

        AttachedActor? entry = null;
        nint addr = GPoseTargetAddress;

        try
        {
            // Assign the entry to the data.
            entry = await AttachActor(data, addr).ConfigureAwait(false);
            if (entry is null)
                throw new Exception("Failed to attach object to SMAD!");

            // Force an initial application of the data.
            Logger.LogInformation($"Applying SMA Data to spawned Actor: {entry.Name}.");
            await ApplyDataToActor(addr).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply SMA Data to GPose Target.");
            if (entry != null && addr != nint.Zero)
                _ = Task.Run(() => DetachActor(addr));
        }
    }
}