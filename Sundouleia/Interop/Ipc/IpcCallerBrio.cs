using Brio.API;
using Brio.API.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using System.Text.Json.Nodes;

namespace Sundouleia.Interop;

public sealed class IpcCallerBrio : IIpcCaller
{
    // Version Checks.
    private readonly ApiVersion ApiVersion;
    private readonly IsAvailable IsAvailable;

    // Event calls (If needed ever)

    // API Getters
    private readonly GetModelTransform GetTransform;
    private readonly GetPoseAsJson GetPoseJson;

    // API Enactors
    private readonly SpawnActor SpawnActor;
    private readonly DespawnActor DespawnActor;
    private readonly SetModelTransform SetTransform;
    private readonly LoadPoseFromJson SetPoseJson;
    private readonly FreezeActor FreezeActor;
    private readonly FreezePhysics FreezePhysics;

    private readonly ILogger<IpcCallerBrio> _logger;

    public IpcCallerBrio(ILogger<IpcCallerBrio> logger)
    {
        _logger = logger;
        // API Version Check
        ApiVersion = new ApiVersion(Svc.PluginInterface);
        IsAvailable = new IsAvailable(Svc.PluginInterface);
        // API Getters
        GetTransform = new GetModelTransform(Svc.PluginInterface);
        GetPoseJson = new GetPoseAsJson(Svc.PluginInterface);
        // API Enactors
        SpawnActor = new SpawnActor(Svc.PluginInterface);
        DespawnActor = new DespawnActor(Svc.PluginInterface);
        SetTransform = new SetModelTransform(Svc.PluginInterface);
        SetPoseJson = new LoadPoseFromJson(Svc.PluginInterface);
        FreezeActor = new FreezeActor(Svc.PluginInterface);
        FreezePhysics = new FreezePhysics(Svc.PluginInterface);

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            var version = ApiVersion.Invoke();
            APIAvailable = (version.Item1 == 3 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    { }

    public async Task<IGameObject?> Spawn()
    {
        if (!APIAvailable)
            return null;

        _logger.LogDebug("Spawning Brio Actor");
        return await Svc.Framework.RunOnFrameworkThread(() => SpawnActor.Invoke(SpawnFlags.Default, false, true)).ConfigureAwait(false);
    }

    public async Task<bool> Despawn(nint address)
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
            DespawnActor.Invoke(gameObj);
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
                return GetPoseJson.Invoke(go) ?? string.Empty;
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
                var currentPose = GetPoseJson.Invoke(go);
                if (currentPose is null)
                {
                    _logger.LogWarning($"Failed to set Pose for Brio Actor: Could not get current pose for {go.Name.TextValue}");
                    return false;
                }

                // Get the model difference to set.
                applicablePose["ModelDifference"] = JsonNode.Parse(JsonNode.Parse(currentPose)!["ModelDifference"]!.ToJsonString());

                // Ensure they are frozen and have physics frozen.
                _logger.LogDebug($"Freezing Brio Actor [{go.Name.TextValue}] for Pose Set");
                FreezeActor.Invoke(go);
                FreezePhysics.Invoke();
                // Then set the pose.
                return SetPoseJson.Invoke(go, poseStr, false);

            }
            _logger.LogWarning($"Failed to set Pose for Brio Actor: Invalid address {address}");
            return false;
        }).ConfigureAwait(false);
    }
}
