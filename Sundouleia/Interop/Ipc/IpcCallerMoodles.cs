using CkCommons;
using Dalamud.Plugin.Ipc;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly ICallGateSubscriber<int> ApiVersion;

    public readonly ICallGateSubscriber<nint, object>       OnStatusManagerModified;
    public readonly ICallGateSubscriber<Guid, bool, object> OnStatusUpdated;
    public readonly ICallGateSubscriber<Guid, bool, object> OnPresetUpdated;

    // API Getters
    private readonly ICallGateSubscriber<string>                        GetOwnStatusManager;
    private readonly ICallGateSubscriber<nint, string>                  GetStatusManagerByPtr;
    private readonly ICallGateSubscriber<List<MoodlesStatusInfo>>       GetOwnStatusManagerInfo;
    private readonly ICallGateSubscriber<nint, List<MoodlesStatusInfo>> GetStatusManagerInfoByPtr;
    private readonly ICallGateSubscriber<Guid, MoodlesStatusInfo>       GetStatusInfo;
    private readonly ICallGateSubscriber<List<MoodlesStatusInfo>>       GetStatusInfoList;
    private readonly ICallGateSubscriber<Guid, MoodlePresetInfo>        GetPresetInfo;
    private readonly ICallGateSubscriber<List<MoodlePresetInfo>>        GetPresetsInfoList;

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
        GetOwnStatusManagerInfo = Svc.PluginInterface.GetIpcSubscriber<List<MoodlesStatusInfo>>("Moodles.GetClientStatusManagerInfoV2");
        GetStatusManagerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        GetStatusManagerInfoByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, List<MoodlesStatusInfo>>("Moodles.GetStatusManagerInfoByPtrV2");

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
    public async Task<string> GetOwnDataStr()
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetOwnStatusManager.InvokeFunc() ?? string.Empty : string.Empty).ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets the ClientPlayer's StatusManager in tuple format.
    /// </summary>
    public async Task<List<MoodlesStatusInfo>> GetOwnDataInfo()
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetOwnStatusManagerInfo.InvokeFunc() : new List<MoodlesStatusInfo>()).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the StatusManager by pointer.
    /// </summary>
    public async Task<string?> GetDataStrByPtr(nint charaAddr)
    {
        return await SafeInvokeCharaAddressAction(charaAddr, func: () => GetStatusManagerByPtr.InvokeFunc(charaAddr)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets another player's StatusManager in tuple format by pointer.
    /// </summary>
    public async Task<List<MoodlesStatusInfo>> GetDataInfoByPtr(nint charaAddr)
    {
        return await SafeInvokeCharaAddressAction(charaAddr, func: () => GetStatusManagerInfoByPtr.InvokeFunc(charaAddr)).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    ///     Gets the StatusTuple for a specified GUID.
    /// </summary>
    public async Task<MoodlesStatusInfo> GetStatusDetails(Guid guid)
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetStatusInfo.InvokeFunc(guid) : new MoodlesStatusInfo()).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the list of all our clients Moodles Info
    /// </summary>
    public async Task<IEnumerable<MoodlesStatusInfo>> GetStatusListDetails()
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetStatusInfoList.InvokeFunc() : Enumerable.Empty<MoodlesStatusInfo>()).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the preset info for a provided GUID from the client.
    /// </summary>
    public async Task<MoodlePresetInfo> GetPresetDetails(Guid guid)
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetPresetInfo.InvokeFunc(guid) : new MoodlePresetInfo()).ConfigureAwait(false);
    }

    /// <summary>
    ///     Gets the list of all our clients Presets Info 
    /// </summary>
    public async Task<IEnumerable<MoodlePresetInfo>> GetPresetListDetails()
    {
        return await Svc.Framework.RunOnFrameworkThread(() => APIAvailable ? GetPresetsInfoList.InvokeFunc() : Enumerable.Empty<MoodlePresetInfo>()).ConfigureAwait(false);
    }


    /// <summary>
    ///     Sets the StatusManager by pointer.
    /// </summary>
    public async Task SetByPtr(nint charaAddr, string statusString)
    {
        await SafeInvokeCharaAddressAction(charaAddr, () => { SetStatusManagerByPtr.InvokeAction(charaAddr, statusString); return true; }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Clears a players StatusManager by pointer.
    /// </summary>
    public async Task ClearByPtr(nint charaAddr)
    {
        await SafeInvokeCharaAddressAction(charaAddr, () => { ClearStatusMangerByPtr.InvokeAction(charaAddr); return true; }).ConfigureAwait(false);
    }

    public async Task ApplyStatuses(IEnumerable<Guid> toApply)
    {
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (!APIAvailable) return;
            var clientNameWorld = PlayerData.NameWithWorld;
            foreach (var guid in toApply)
                ApplyStatusByName.InvokeAction(guid, clientNameWorld);
        }).ConfigureAwait(false);
    }

    public async Task ApplyPreset(Guid id)
    {
        await Svc.Framework.RunOnFrameworkThread(() => { if (APIAvailable) ApplyPresetByName.InvokeAction(id, PlayerData.NameWithWorld); }).ConfigureAwait(false);
    }

    public async Task RemoveStatuses(IEnumerable<Guid> toRemove)
    {
        await Svc.Framework.RunOnFrameworkThread(() => { if (APIAvailable) RemoveStatusesByName.InvokeAction(toRemove.ToList(), PlayerData.NameWithWorld); }).ConfigureAwait(false);
    }

    /// <summary>
    ///   Safely invokes an action on a character address within the framework thread. Verifies the character is still rendered within the framework thread.
    /// </summary>
    private async Task<T?> SafeInvokeCharaAddressAction<T>(nint address, Func<T> func)
    {
        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (!APIAvailable) return default;
            if (!Svc.Objects.Any(obj => obj.Address == address))
                return default;
            return func();
        }).ConfigureAwait(false);
    }
}
