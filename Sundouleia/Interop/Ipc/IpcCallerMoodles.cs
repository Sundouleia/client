using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using TerraFX.Interop.Windows;

namespace Sundouleia.Interop;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly ICallGateSubscriber<int> ApiVersion;

    public readonly ICallGateSubscriber<nint, object>       OnStatusManagerModified;
    public readonly ICallGateSubscriber<Guid, bool, object> OnStatusUpdated;
    public readonly ICallGateSubscriber<Guid, bool, object> OnPresetUpdated;

    // API Getters
    private readonly ICallGateSubscriber<string>                    GetOwnStatusManager;
    private readonly ICallGateSubscriber<nint, string>              GetStatusManagerByPtr;
    private readonly ICallGateSubscriber<Guid, MoodlesStatusInfo>   GetStatusInfo;
    private readonly ICallGateSubscriber<List<MoodlesStatusInfo>>   GetStatusInfoList;
    private readonly ICallGateSubscriber<Guid, MoodlePresetInfo>    GetPresetInfo;
    private readonly ICallGateSubscriber<List<MoodlePresetInfo>>    GetPresetsInfoList;

    // API Enactors
    private readonly ICallGateSubscriber<nint, string, object>          SetStatusManagerByPtr;
    private readonly ICallGateSubscriber<nint, object>                  ClearStatusMangerByPtr;
    private readonly ICallGateSubscriber<Guid, string, object>          ApplyStatusByName;
    private readonly ICallGateSubscriber<Guid, string, object>          ApplyPresetByName;
    private readonly ICallGateSubscriber<List<Guid>, string, object>    RemoveStatusesByName;

    private readonly SundouleiaMediator _mediator;

    public IpcCallerMoodles(SundouleiaMediator mediator)
    {
        _mediator = mediator;

        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<int>("Moodles.Version");

        // API Getters
        GetOwnStatusManager = Svc.PluginInterface.GetIpcSubscriber<string>("Moodles.GetClientStatusManagerV2");
        GetStatusManagerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        GetStatusInfo = Svc.PluginInterface.GetIpcSubscriber<Guid, MoodlesStatusInfo>("Moodles.GetStatusInfoV2");
        GetStatusInfoList = Svc.PluginInterface.GetIpcSubscriber<List<MoodlesStatusInfo>>("Moodles.GetStatusInfoListV2");
        GetPresetInfo = Svc.PluginInterface.GetIpcSubscriber<Guid, MoodlePresetInfo>("Moodles.GetPresetInfoV2");
        GetPresetsInfoList = Svc.PluginInterface.GetIpcSubscriber<List<MoodlePresetInfo>>("Moodles.GetPresetsInfoListV2");

        // API Enactors
        SetStatusManagerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        ClearStatusMangerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");
        ApplyStatusByName = Svc.PluginInterface.GetIpcSubscriber<Guid, string, object>("Moodles.AddOrUpdateStatusByNameV2");
        ApplyPresetByName = Svc.PluginInterface.GetIpcSubscriber<Guid, string, object>("Moodles.ApplyPresetByNameV2");
        RemoveStatusesByName = Svc.PluginInterface.GetIpcSubscriber<List<Guid>, string, object>("Moodles.RemoveStatusesByNameV2");

        // API Action Events:
        OnStatusManagerModified = Svc.PluginInterface.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        OnStatusUpdated = Svc.PluginInterface.GetIpcSubscriber<Guid, bool, object>("Moodles.StatusUpdated");
        OnPresetUpdated = Svc.PluginInterface.GetIpcSubscriber<Guid, bool, object>("Moodles.PresetUpdated");

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            var result = ApiVersion.InvokeFunc() >= 4;
            if(!APIAvailable && result)
                _mediator.Publish(new MoodlesReady());
            APIAvailable = result;
        }
        catch
        {
            // Moodles was not ready yet / went offline. Set back to false. (Statuses are auto-cleared by moodles)
            APIAvailable = false;
        }
    }

    public void Dispose()
    { }

    /// <summary> 
    ///     Gets the ClientPlayer's StatusManager string.
    /// </summary>
    public async Task<string> GetOwn()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetOwnStatusManager.InvokeFunc() ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the StatusManager by pointer.
    /// </summary>
    public async Task<string?> GetByPtr(nint charaAddr)
    {
        if (!APIAvailable) return null;
        return await Svc.Framework.RunOnFrameworkThread(() => GetStatusManagerByPtr.InvokeFunc(charaAddr)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets the StatusTuple for a specified GUID.
    /// </summary>
    public async Task<MoodlesStatusInfo> GetStatusDetails(Guid guid)
    {
        if (!APIAvailable) return new MoodlesStatusInfo();
        return await Svc.Framework.RunOnFrameworkThread(() => GetStatusInfo.InvokeFunc(guid)).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the list of all our clients Moodles Info
    /// </summary>
    public async Task<IEnumerable<MoodlesStatusInfo>> GetStatusListDetails()
    {
        if (!APIAvailable) return Enumerable.Empty<MoodlesStatusInfo>();
        return await Svc.Framework.RunOnFrameworkThread(GetStatusInfoList.InvokeFunc).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the preset info for a provided GUID from the client.
    /// </summary>
    public async Task<MoodlePresetInfo> GetPresetDetails(Guid guid)
    {
        if (!APIAvailable) return new MoodlePresetInfo();
        return await Svc.Framework.RunOnFrameworkThread(() => GetPresetInfo.InvokeFunc(guid)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets the list of all our clients Presets Info 
    /// </summary>
    public async Task<IEnumerable<MoodlePresetInfo>> GetPresetListDetails()
    {
        if (!APIAvailable) return Enumerable.Empty<MoodlePresetInfo>();
        return await Svc.Framework.RunOnFrameworkThread(GetPresetsInfoList.InvokeFunc).ConfigureAwait(false);
    }


    /// <summary>
    ///     Sets the StatusManager by pointer.
    /// </summary>
    public async Task SetByPtr(nint charaAddr, string statusString)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => SetStatusManagerByPtr.InvokeAction(charaAddr, statusString)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Clears a players StatusManager by pointer.
    /// </summary>
    public async Task ClearByPtr(nint charaAddr)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearStatusMangerByPtr.InvokeAction(charaAddr)).ConfigureAwait(false);
    }

    public async Task ApplyStatuses(IEnumerable<Guid> toApply)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            var clientNameWorld = PlayerData.NameWithWorld;
            foreach (var guid in toApply)
                ApplyStatusByName.InvokeAction(guid, clientNameWorld);
        }).ConfigureAwait(false);
    }

    public async Task ApplyPreset(Guid id)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyPresetByName.InvokeAction(id, PlayerData.NameWithWorld)).ConfigureAwait(false);
    }

    public async Task RemoveStatuses(IEnumerable<Guid> toRemove)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => RemoveStatusesByName.InvokeAction(toRemove.ToList(), PlayerData.NameWithWorld)).ConfigureAwait(false);
    }
}
