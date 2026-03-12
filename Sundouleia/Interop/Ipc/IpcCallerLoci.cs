using LociApi.Enums;
using LociApi.Helpers;
using LociApi.Ipc;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

// The primary sync method used for Sundouleia
public sealed class IpcCallerLoci : IIpcCaller
{
    public const string SUNDOULEIA_TAG = "Sundouleia";

    private readonly ApiVersion ApiVersion;
    private readonly IsEnabled  IsEnabled;
    // Events that can handled within this class to dicate enabled state.
    private readonly EventSubscriber        Ready;
    private readonly EventSubscriber        Disposed;
    private readonly EventSubscriber<bool>  EnabledChanged;

    // API Registry
    private readonly RegisterByPtr Register;
    private readonly UnregisterByPtr Unregister;
    private readonly UnregisterByName UnregisterName;
    private readonly UnregisterAll UnregisterAll;
    // internal EventSubscriber<nint, string> ActorHostsChanged;

    // API StatusManagers
    private readonly GetManager GetManager;
    private readonly GetManagerByPtr GetManagerByPtr;
    private readonly GetManagerInfo GetManagerInfo;
    private readonly GetManagerInfoByPtr GetManagerInfoByPtr;
    private readonly SetManagerByPtr SetManagerByPtr;
    private readonly ClearManagerByPtr ClearManagerByPtr;
    private readonly ClearManagerByName ClearManagerByName;
    private readonly ConvertLegacyData ConvertLegacyData;

    // API Statuses
    private readonly GetStatusInfo GetStatusTuple;
    private readonly GetStatusInfoList GetAllStatuseTuples;
    private readonly ApplyStatus ApplyStatusById;
    private readonly ApplyStatuses ApplyStatusByIds;
    private readonly ApplyStatusInfo ApplyStatusTuple;      // Can only be done on client.
    private readonly ApplyStatusInfos ApplyStatusTuples;
    private readonly RemoveStatus RemoveStatus;
    private readonly RemoveStatuses RemoveStatuses;

    // API Presets
    private readonly GetPresetInfo GetPresetTuple;
    private readonly GetPresetInfoList GetAllPresetTuples;
    private readonly ApplyPreset ApplyPresetById;
    private readonly ApplyPresetInfo ApplyPresetTuple;

    // API Events (Later)

    public IpcCallerLoci(ILogger<IpcCallerLoci> logger, SundouleiaMediator mediator)
    {
        // Base
        ApiVersion = new ApiVersion(Svc.PluginInterface);
        IsEnabled = new IsEnabled(Svc.PluginInterface);
        Ready = LociApi.Ipc.Ready.Subscriber(Svc.PluginInterface, () =>
        {
            APIAvailable = true;
            FeaturesEnabled = IsEnabled.Invoke();
            logger.LogDebug("Loci Enabled!", LoggerType.IpcLoci);
            mediator.Publish(new LociReady());
        });
        Disposed = LociApi.Ipc.Disposed.Subscriber(Svc.PluginInterface, () =>
        {
            APIAvailable = false;
            FeaturesEnabled = false;
            logger.LogDebug("Loci Disabled!", LoggerType.IpcLoci);
            mediator.Publish(new LociDisposed());
        });
        EnabledChanged = EnabledStateChanged.Subscriber(Svc.PluginInterface, state => FeaturesEnabled = state);

        // Registry
        Register = new RegisterByPtr(Svc.PluginInterface);
        Unregister = new UnregisterByPtr(Svc.PluginInterface);
        UnregisterName = new UnregisterByName(Svc.PluginInterface);
        UnregisterAll = new UnregisterAll(Svc.PluginInterface);

        // Status Managers
        GetManager = new GetManager(Svc.PluginInterface);
        GetManagerByPtr = new GetManagerByPtr(Svc.PluginInterface);
        GetManagerInfo = new GetManagerInfo(Svc.PluginInterface);
        GetManagerInfoByPtr = new GetManagerInfoByPtr(Svc.PluginInterface);
        SetManagerByPtr = new SetManagerByPtr(Svc.PluginInterface);
        ClearManagerByPtr = new ClearManagerByPtr(Svc.PluginInterface);
        ClearManagerByName = new ClearManagerByName(Svc.PluginInterface);
        ConvertLegacyData = new ConvertLegacyData(Svc.PluginInterface);

        // Statuses
        GetStatusTuple = new GetStatusInfo(Svc.PluginInterface);
        GetAllStatuseTuples = new GetStatusInfoList(Svc.PluginInterface);
        ApplyStatusById = new ApplyStatus(Svc.PluginInterface);
        ApplyStatusByIds = new ApplyStatuses(Svc.PluginInterface);
        ApplyStatusTuple = new ApplyStatusInfo(Svc.PluginInterface);
        ApplyStatusTuples = new ApplyStatusInfos(Svc.PluginInterface);
        RemoveStatus = new RemoveStatus(Svc.PluginInterface);
        RemoveStatuses = new RemoveStatuses(Svc.PluginInterface);

        // Presets
        GetPresetTuple = new GetPresetInfo(Svc.PluginInterface);
        GetAllPresetTuples = new GetPresetInfoList(Svc.PluginInterface);
        ApplyPresetById = new ApplyPreset(Svc.PluginInterface);
        ApplyPresetTuple = new ApplyPresetInfo(Svc.PluginInterface);

        CheckAPI();
    }

    public void Dispose()
    {
        Ready.Dispose();
        Disposed.Dispose();
        EnabledChanged.Dispose();
    }

    public static bool APIAvailable { get; private set; } = false;
    public static bool FeaturesEnabled { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            var version = ApiVersion.Invoke();
            APIAvailable = (version.Item1 == 1 && version.Item2 >= 0);
        }
        catch
        {
            APIAvailable = false;
        }
    }

    // Assuming we know an actor is valid, these calls could all run syncronously.

    /// <inheritdoc cref="LociApi.Ipc.RegisterByPtr"/>
    public async Task<bool> RegisterActor(nint address)
    {
        if (!APIAvailable) return false;
        return await Svc.Framework.RunOnFrameworkThread(() => Register.Invoke(address, SUNDOULEIA_TAG)).ConfigureAwait(false) is LociApiEc.Success;
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByPtr"/>
    public async Task UnregisterActor(nint address)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => Unregister.Invoke(address, SUNDOULEIA_TAG)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByName"/>
    public async Task UnregisterPlayer(string playerNameWorld)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => UnregisterName.Invoke(playerNameWorld, SUNDOULEIA_TAG)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterByName"/>
    public async Task UnregisterBuddy(string playerName, string buddyName)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => UnregisterName.Invoke(playerName, buddyName, SUNDOULEIA_TAG)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.UnregisterAll"/>
    public async Task HailMerryUnregister()
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => UnregisterAll.Invoke(SUNDOULEIA_TAG)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManager"/>
    public async Task<string> GetOwnManagerStr()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetManager.Invoke().Item2 ?? string.Empty).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManagerByPtr"/>
    public async Task<string> GetActorSMStr(nint actorAddr)
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetManagerByPtr.Invoke(actorAddr).Item2 ?? string.Empty).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManagerInfo"/>
    public async Task<List<LociStatusInfo>> GetOwnManagerInfo()
    {
        if (!APIAvailable) return [];
        return await Svc.Framework.RunOnFrameworkThread(() => GetManagerInfo.Invoke()).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetManagerInfoByPtr"/>
    public async Task<List<LociStatusInfo>> GetActorSMInfo(nint actorAddr)
    {
        if (!APIAvailable) return [];
        return await Svc.Framework.RunOnFrameworkThread(() => GetManagerInfoByPtr.Invoke(actorAddr)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.SetManagerByPtr"/>
    public async Task SetActorSM(nint actorAddr, string dataStr)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => SetManagerByPtr.Invoke(actorAddr, dataStr)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByPtr"/>
    public async Task ClearActorSM(nint actorAddr)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearManagerByPtr.Invoke(actorAddr)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByName"/>
    public async Task ClearPlayerSM(string playerNameWorld)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearManagerByName.Invoke(playerNameWorld)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ClearManagerByName"/>
    public async Task ClearBuddySM(string playerName, string buddyName)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearManagerByName.Invoke(playerName, buddyName)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ConvertLegacyData"/>
    public string ConvertToLociData(string legacyStatusManagerBase64)
    {
        if (!APIAvailable) return string.Empty;
        return ConvertLegacyData.Invoke(legacyStatusManagerBase64);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetStatusInfo"/>
    public async Task<LociStatusInfo> GetStatusInfo(Guid guid)
    {
        if (!APIAvailable) return default;
        return await Svc.Framework.RunOnFrameworkThread(() => GetStatusTuple.Invoke(guid).Item2).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetStatusInfoList"/>
    public async Task<List<LociStatusInfo>> GetStatusInfos()
    {
        if (!APIAvailable) return [];
        return await Svc.Framework.RunOnFrameworkThread(GetAllStatuseTuples.Invoke).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyStatus"/>
    public async Task ApplyStatus(Guid id)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyStatusById.Invoke(id)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyStatuses"/>
    public async Task ApplyStatus(List<Guid> ids)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyStatusByIds.Invoke(ids, out _)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyStatusInfo"/>
    public async Task ApplyStatusInfo(LociStatusInfo tuple)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyStatusTuple.Invoke(tuple)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyStatusInfos"/>
    public async Task ApplyStatusInfo(List<LociStatusInfo> tuples)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyStatusTuples.Invoke(tuples)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.RemoveStatus"/>
    public async Task BombStatus(Guid id)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => RemoveStatus.Invoke(id)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.RemoveStatuses"/>
    public async Task BombStatus(List<Guid> ids)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => RemoveStatuses.Invoke(ids, out _)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetPresetInfo"/>
    public async Task<LociPresetInfo> GetPresetInfo(Guid guid)
    {
        if (!APIAvailable) return default;
        return await Svc.Framework.RunOnFrameworkThread(() => GetPresetTuple.Invoke(guid).Item2).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.GetPresetInfoList"/>
    public async Task<List<LociPresetInfo>> GetPresetInfos()
    {
        if (!APIAvailable) return [];
        return await Svc.Framework.RunOnFrameworkThread(GetAllPresetTuples.Invoke).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyPreset"/>
    public async Task ApplyPreset(Guid id)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyPresetById.Invoke(id)).ConfigureAwait(false);
    }

    /// <inheritdoc cref="LociApi.Ipc.ApplyPresetInfo"/>
    public async Task ApplyPresetInfo(LociPresetInfo tuple)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyPresetTuple.Invoke(tuple)).ConfigureAwait(false);
    }
}
