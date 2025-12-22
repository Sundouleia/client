using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using System.Text.Json.Nodes;

namespace Sundouleia.Interop;

// Personal enum flag introduced in Brio's API 3.0
// https://github.com/Etheirys/Brio.API/blob/main/Brio.API/Enums/SpawnFlags.cs
[Flags]
public enum SpawnFlags : ulong
{
    None = 0,
    ReserveCompanionSlot = 1 << 1,
    CopyPosition = 1 << 2,
    IsProp = 1 << 4,
    IsEffect = 1 << 8,
    SetDefaultAppearance = 1 << 16,

    Prop = IsProp | SetDefaultAppearance | CopyPosition,
    Effect = IsEffect | SetDefaultAppearance | CopyPosition,
    Default = CopyPosition,
}

public sealed class IpcCallerBrio : IIpcCaller
{
    // Version Checks.
    private readonly ICallGateSubscriber<(int, int)> ApiVersion;
    private readonly ICallGateSubscriber<bool> IsAvailable;

    // Event Calls. (None currently)

    // API Getters
    private readonly ICallGateSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)> GetActorTransforms;
    private readonly ICallGateSubscriber<IGameObject, string> GetPoseJson;

    // API Enactors. (Hopefully use something besides gameObject idk)
    private readonly ICallGateSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool> SetActorTransform;
    private readonly ICallGateSubscriber<SpawnFlags, bool, bool, IGameObject?>  SpawnActor;
    private readonly ICallGateSubscriber<IGameObject, bool>                     DespawnActor;
    private readonly ICallGateSubscriber<IGameObject, string, bool, bool>       SetPoseJson;

    private readonly ICallGateSubscriber<IGameObject, bool> FreezeActor;
    private readonly ICallGateSubscriber<IGameObject, bool> UnfreezeActor;
    private readonly ICallGateSubscriber<bool> FreezePhysics;
    private readonly ICallGateSubscriber<bool> UnfreezePhysics;

    private readonly ILogger<IpcCallerBrio> _logger;

    public IpcCallerBrio(ILogger<IpcCallerBrio> logger)
    {
        _logger = logger;
        // API Version Check
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("Brio.ApiVersion");
        IsAvailable = Svc.PluginInterface.GetIpcSubscriber<bool>("Brio.IsAvailable");
        // API Getter Functions
        GetActorTransforms = Svc.PluginInterface.GetIpcSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)>("Brio.GetModelTransform.V3");
        GetPoseJson = Svc.PluginInterface.GetIpcSubscriber<IGameObject, string>("Brio.GetPoseAsJson.V3");
        // API Enactor Functions
        SetActorTransform = Svc.PluginInterface.GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.SetModelTransform.V3");
        SpawnActor = Svc.PluginInterface.GetIpcSubscriber<SpawnFlags, bool, bool, IGameObject?>("Brio.SpawnActor.V3");
        DespawnActor = Svc.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.DespawnActor.V3");
        SetPoseJson = Svc.PluginInterface.GetIpcSubscriber<IGameObject, string, bool, bool>("Brio.LoadPoseFromJson.V3");
        FreezeActor = Svc.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.FreezeActor.V3");
        UnfreezeActor = Svc.PluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.FreezeActor.V3");
        FreezePhysics = Svc.PluginInterface.GetIpcSubscriber<bool>("Brio.FreezePhysics.V3");
        UnfreezePhysics = Svc.PluginInterface.GetIpcSubscriber<bool>("Brio.UnfreezePhysics.V3");

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            // Replace with IsAvailable later.
            var version = ApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 3 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    { }

    public IGameObject? SpawnBrioActor()
    {
        if (!APIAvailable)
            return null;

        _logger.LogDebug("Spawning Brio Actor");
        return SpawnActor.InvokeFunc(SpawnFlags.Default, false, true);
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
