using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.Services;
using Sundouleia.Watchers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;

/// <summary>
///     Manages the processed SMA data states and their
///     application to expected targets. <para />
///     Additionally handles data assignment and IPC communication.
/// </summary>
public class SMAManager
{
    private readonly ILogger<SMAManager> _logger;
    private readonly GPoseActorHandler _gPoseHandler;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    private HashSet<ModularActorData> _processedSMAData = [];
    
    public SMAManager(ILogger<SMAManager> logger, GPoseActorHandler gPoseHandler, 
        IpcManager ipc, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _gPoseHandler = gPoseHandler;
        _ipc = ipc;
        _watcher = watcher;
    }

    public IEnumerable<ModularActorData> ProcessedActors => _processedSMAData;

    // For actors loaded in but not yet applied.
    internal bool AddProcessedActorData(ModularActorData actorData)
        => _processedSMAData.Add(actorData);

    public async Task SpawnAndApplySMAData(ModularActorData data)
    {
        if (!_ipc.Brio.APIAvailable)
        {
            _logger.LogWarning("Brio API is not available, cannot spawn SMA Actor.");
            return;
        }

        _logger.LogInformation($"Spawning SMA Actor for BaseId: {data.BaseId}.");
        if (await _ipc.Brio.SpawnBrioActor().ConfigureAwait(false) is not { } newActor)
        {
            _logger.LogError("Failed to spawn SMA Actor via Brio.");
            return;
        }

        HandledActorDataEntry? entry = null;
        try
        {
            unsafe { entry = new(newActor.Name.TextValue, (GameObject*)newActor.Address, data); }

            if (entry is null)
                throw new InvalidOperationException("Failed to create Entry for GPose Target.");

            _gPoseHandler.AddActor(entry);

            // Now wait for the actor to be fully loaded.
            _logger.LogInformation($"Waiting for spawned Actor: {newActor.Name.TextValue} to be fully loaded.");
            await _watcher.WaitForFullyLoadedGameObject(entry.ObjectAddress).ConfigureAwait(false);

            // Perform the assignment of application.
            _logger.LogInformation($"Applying SMA Data to spawned Actor: {newActor.Name.TextValue}.");
            await ApplyDataInternal(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply SMA Data to GPose Target.");
            if (entry is not null)
                await _gPoseHandler.RemoveActor(entry).ConfigureAwait(false);
        }
    }

    public async Task ApplySMAToGPoseTarget(ModularActorData data)
    {
        if (!_gPoseHandler.HasGPoseTarget)
            return;

        HandledActorDataEntry? entry = null;
        try
        {
            unsafe { entry = new(_gPoseHandler.GPoseTarget->NameString, _gPoseHandler.GPoseTarget, data); }

            if (entry is null)
                throw new InvalidOperationException("Failed to create HandledActorDataEntry for GPose Target.");

            _gPoseHandler.AddActor(entry);
            // Perform the assignment of application.
            _logger.LogInformation($"Applying SMA Data to Actor: {entry.ActorName}.");
            await ApplyDataInternal(entry).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply SMA Data to GPose Target.");
            if (entry is not null)
                await _gPoseHandler.RemoveActor(entry).ConfigureAwait(false);
        }
    }

    // This does not work inside of a UITask and must be assigned to one if you intend to block operations.
    private async Task ApplyDataInternal(HandledActorDataEntry entry)
    {
        // If there was a previous application, 
        if (entry.CollectionId == Guid.Empty)
        {
            _logger.LogDebug($"SMA ({entry.Data.BaseId}) Had no collection. Assigning!");
            entry.CollectionId = await _ipc.Penumbra.NewSMACollection(entry).ConfigureAwait(false);
        }
        // If it was correctly set, assign the actor.
        if (entry.CollectionId != Guid.Empty)
        {
            _logger.LogDebug($"SMA ({entry.Data.BaseId}) Assigned collection {entry.CollectionId}.");
            await _ipc.Penumbra.AssignSundesmoCollection(entry.CollectionId, entry.ObjectIndex).ConfigureAwait(false);
            await _ipc.Penumbra.ReloadSMABase(entry).ConfigureAwait(false);
        }
        
        _logger.LogDebug($"SMA ({entry.Data.BaseId}) Applying Glamourer Data.");
        await _ipc.Glamourer.ApplyBase64StateByPtr(entry.ObjectAddress, entry.Data.FinalGlamourData).ConfigureAwait(false);

        _logger.LogDebug($"SMA ({entry.Data.BaseId}) Applying CustomizePlus Data.");
        if (!string.IsNullOrEmpty(entry.Data.CPlusData))
            entry.CPlusId = await _ipc.CustomizePlus.ApplyTempProfile(entry).ConfigureAwait(false);

        // Finally Redraw the actor.
        _logger.LogInformation($"SMA ({entry.Data.BaseId}) Finished application, redrawing actor.");
        _ipc.Penumbra.RedrawGameObject(entry.ObjectIndex);
    }
}