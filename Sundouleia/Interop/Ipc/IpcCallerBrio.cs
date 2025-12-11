using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Text.Json.Nodes;

namespace Sundouleia.Interop;

public sealed class IpcCallerBrio : IIpcCaller
{
    // Version Checks.
    private readonly ICallGateSubscriber<(int, int)> ApiVersion;

    // Event Calls. (None currently)

    // API Getters
    private readonly ICallGateSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)> GetActorTransforms;
    private readonly ICallGateSubscriber<IGameObject, string> GetPoseJson;

    // API Enactors. (Hopefully use something besides gameObject idk)
    private readonly ICallGateSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool> SetActorTransform;
    private readonly ICallGateSubscriber<bool, bool, bool, Task<IGameObject>> SpawnActorAsync;
    private readonly ICallGateSubscriber<IGameObject, bool>                   DespawnActor;
    private readonly ICallGateSubscriber<IGameObject, string, bool, bool>     SetPoseJson;

    private readonly ICallGateSubscriber<IGameObject, bool> FreezeActor;
    private readonly ICallGateSubscriber<bool> FreezePhysics;

    private readonly ILogger<IpcCallerBrio> _logger;

    public IpcCallerBrio(ILogger<IpcCallerBrio> logger)
    {
        _logger = logger;
        // API Version Check
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("Brio.ApiVersion");
        // API Getter Functions
        GetActorTransforms = Svc.PluginInterface.GetIpcSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)>("Brio.Actor.GetModelTransform");
        GetPoseJson = Svc.PluginInterface.GetIpcSubscriber<IGameObject, string>("Brio.Actor.Pose.GetPoseAsJson");
        // API Enactor Functions
        SetActorTransform = Svc.PluginInterface.GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.Actor.SetModelTransform");
        SpawnActorAsync = Svc.PluginInterface.GetIpcSubscriber<bool, bool, bool, Task<IGameObject>>("Brio.Actor.SpawnExAsync");
        DespawnActor = Svc.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Despawn");
        SetPoseJson = Svc.PluginInterface.GetIpcSubscriber<IGameObject, string, bool, bool>("Brio.Actor.Pose.LoadFromJson");
        FreezeActor = Svc.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Freeze");
        FreezePhysics = Svc.PluginInterface.GetIpcSubscriber<bool>("Brio.FreezePhysics");

        CheckAPI();
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            var version = ApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 2 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    { }

    public async Task<IGameObject?> SpawnBrioActor()
    {
        if (!APIAvailable)
            return null;

        _logger.LogDebug("Spawning Brio Actor");
        return await SpawnActorAsync.InvokeFunc(false, false, true).ConfigureAwait(false);
    }

    public async Task<bool> DespawnBrioActor(nint address)
    {
        if (!APIAvailable)
            return false;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (Svc.Objects.CreateObjectReference(address) is not { } gameObj)
            {
                _logger.LogWarning("Failed to despawn Brio Actor: Invalid address {address}", address);
                return false;
            }
            _logger.LogDebug($"Despawning Brio Actor {gameObj.Name.TextValue}");
            DespawnActor.InvokeFunc(gameObj);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task<string> GetActorPoseAsync(nint address)
    {
        if (!APIAvailable)
            return string.Empty;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            // Only accept requests to obtain profiles for players.
            if (Svc.Objects.CreateObjectReference(address) is { } obj && obj is IGameObject go)
            {
                _logger.LogDebug($"Getting Pose for Brio Actor [{go.Name.TextValue}]");
                return GetPoseJson.InvokeFunc(go);
            }
            return string.Empty;
        }).ConfigureAwait(false);
    }

    public async Task<bool> SetPoseAsync(nint address, string poseStr)
    {
        if (!APIAvailable)
            return false;

        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (Svc.Objects.CreateObjectReference(address) is { } obj && obj is IGameObject go)
            {
                _logger.LogDebug($"Setting Pose for Brio Actor [{go.Name.TextValue}]");
                var applicablePose = JsonNode.Parse(poseStr)!;
                var currentPose = GetPoseJson.InvokeFunc(go);
                // Get the model difference to set.
                applicablePose["ModelDifference"] = JsonNode.Parse(JsonNode.Parse(currentPose)!["ModelDifference"]!.ToJsonString());

                // Ensure they are frozen and have physics frozen.
                _logger.LogDebug($"Freezing Brio Actor [{go.Name.TextValue}] for Pose Set");
                FreezeActor.InvokeFunc(go);
                FreezePhysics.InvokeFunc();
                // Then set the pose.
                return SetPoseJson.InvokeFunc(go, poseStr, false);

            }
            _logger.LogWarning($"Failed to set Pose for Brio Actor: Invalid address {address}");
            return false;
        }).ConfigureAwait(false);
    }
}
