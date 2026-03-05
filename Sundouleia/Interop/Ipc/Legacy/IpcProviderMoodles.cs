using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MemoryPack;
using Microsoft.Extensions.Hosting;
using Sundouleia.DrawSystem;
using Sundouleia.Loci;
using Sundouleia.Loci.Data;
using Sundouleia.Pairs;
using Sundouleia.Watchers;

namespace Sundouleia.Interop;

// Provides pseudo-direct calls from Moodles API within Sundouleia to
// provide Loci support to other services that have not yet integrated it.
// This was done due to increasingly difficult circumstances trying to allow both platforms to co-exist.
public sealed class IpcProviderMoodles : IHostedService
{
    private static ICallGateProvider<int> ApiVersion;
    private static ICallGateProvider<object> Ready;
    private static ICallGateProvider<object> Disposing;

    private static ICallGateProvider<nint, object> OnManagerModified;
    private static ICallGateProvider<Guid, bool, object> OnStatusUpdated;
    private static ICallGateProvider<Guid, bool, object> OnPresetUpdated;

    // API Getters
    private static ICallGateProvider<Guid, MoodlesStatusInfo> GetStatusInfo;
    private static ICallGateProvider<List<MoodlesStatusInfo>> GetStatusInfoList;
    private static ICallGateProvider<Guid, MoodlePresetInfo> GetPresetInfo;
    private static ICallGateProvider<List<MoodlePresetInfo>> GetPresetsInfoList;

    // FS Getters
    private static ICallGateProvider<List<MoodlesMoodleInfo>> GetRegisteredMoodles;
    private static ICallGateProvider<List<MoodlesProfileInfo>> GetRegisteredProfiles;

    // Status Manager
    private static ICallGateProvider<List<LociStatusInfo>> GetOwnManagerInfo;
    private static ICallGateProvider<nint, List<LociStatusInfo>> GetManagerInfoByPtr;

    private static ICallGateProvider<string> GetOwnManager;
    private static ICallGateProvider<nint, string> GetManagerByPtr;
    private static ICallGateProvider<string, string> GetManagerByName;

    private static ICallGateProvider<nint, string, object> SetManagerByPtr;
    private static ICallGateProvider<string, string, object> SetManagerByName;
    private static ICallGateProvider<nint, object> ClearMangerByPtr;
    private static ICallGateProvider<string, object> ClearManagerByName;

    // Other Dummy Functions
    private static ICallGateProvider<Guid, nint, object> AddUpdateMoodleByPtr;
    private static ICallGateProvider<Guid, string, object> AddUpdateMoodleByName;
    private static ICallGateProvider<Guid, IPlayerCharacter, object> AddUpdateMoodleByPlayer;

    private static ICallGateProvider<Guid, nint, object> ApplyPresetByPtr;
    private static ICallGateProvider<Guid, string, object> ApplyPresetByName;
    private static ICallGateProvider<Guid, IPlayerCharacter, object> ApplyPresetByPlayer;

    private static ICallGateProvider<Guid, IPlayerCharacter, object> RemoveMoodleByPlayer;

    private static ICallGateProvider<List<Guid>, nint, object> RemoveMoodlesByPtr;
    private static ICallGateProvider<List<Guid>, string, object> RemoveMoodlesByName;
    private static ICallGateProvider<List<Guid>, IPlayerCharacter, object> RemoveMoodlesByPlayer;

    private static ICallGateProvider<Guid, IPlayerCharacter, object> RemovePresetByPlayer;


    private readonly ILogger<IpcProviderMoodles> _logger;
    private readonly LociManager _manager;
    private readonly StatusesFS _statusesFS;
    private readonly PresetsFS _presetsFS;

    public IpcProviderMoodles(ILogger<IpcProviderMoodles> logger, LociManager manager, StatusesFS statusesFS, PresetsFS presetsFS)
    {
        _logger = logger;
        _manager = manager;
        _statusesFS = statusesFS;
        _presetsFS = presetsFS;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ApiVersion = Svc.PluginInterface.GetIpcProvider<int>("Moodles.Version");
        Ready = Svc.PluginInterface.GetIpcProvider<object>("Moodles.Ready");
        Disposing = Svc.PluginInterface.GetIpcProvider<object>("Moodles.Disposing");
        ApiVersion.RegisterFunc(() => 4);

        OnManagerModified = Svc.PluginInterface.GetIpcProvider<nint, object>("Moodles.StatusManagerModified");
        OnStatusUpdated = Svc.PluginInterface.GetIpcProvider<Guid, bool, object>("Moodles.StatusUpdated");
        OnPresetUpdated = Svc.PluginInterface.GetIpcProvider<Guid, bool, object>("Moodles.PresetUpdated");

        // API Getters
        GetStatusInfo = Svc.PluginInterface.GetIpcProvider<Guid, MoodlesStatusInfo>("Moodles.GetStatusInfoV2");
        GetStatusInfoList = Svc.PluginInterface.GetIpcProvider<List<MoodlesStatusInfo>>("Moodles.GetStatusInfoListV2");
        GetPresetInfo = Svc.PluginInterface.GetIpcProvider<Guid, MoodlePresetInfo>("Moodles.GetPresetInfoV2");
        GetPresetsInfoList = Svc.PluginInterface.GetIpcProvider<List<MoodlePresetInfo>>("Moodles.GetPresetsInfoListV2");
        GetStatusInfo.RegisterFunc(GetTupleInfo);
        GetStatusInfoList.RegisterFunc(GetTupleInfoList);
        GetPresetInfo.RegisterFunc(GetPresetTupleInfo);
        GetPresetsInfoList.RegisterFunc(GetPresetTupleInfoList);

        // File system grabbers.
        GetRegisteredMoodles = Svc.PluginInterface.GetIpcProvider<List<MoodlesMoodleInfo>>("Moodles.GetRegisteredMoodlesV2");
        GetRegisteredProfiles = Svc.PluginInterface.GetIpcProvider<List<MoodlesProfileInfo>>("Moodles.GetRegisteredProfilesV2");
        GetRegisteredMoodles.RegisterFunc(GetRegisteredMoodlesInfo);
        GetRegisteredProfiles.RegisterFunc(GetRegisteredProfilesInfo);

        // Status Manager Stuff
        GetOwnManagerInfo = Svc.PluginInterface.GetIpcProvider<List<MoodlesStatusInfo>>("Moodles.GetClientStatusManagerInfoV2");
        GetManagerInfoByPtr = Svc.PluginInterface.GetIpcProvider<nint, List<MoodlesStatusInfo>>("Moodles.GetStatusManagerInfoByPtrV2");
        GetOwnManagerInfo.RegisterFunc(GetOwnStatusManagerInfo);
        GetManagerInfoByPtr.RegisterFunc(GetStatusManagerInfoByPtr);

        GetOwnManager = Svc.PluginInterface.GetIpcProvider<string>("Moodles.GetClientStatusManagerV2");
        GetManagerByPtr = Svc.PluginInterface.GetIpcProvider<nint, string>("Moodles.GetStatusManagerByPtrV2");
        GetManagerByName = Svc.PluginInterface.GetIpcProvider<string, string>("Moodles.GetStatusManagerByNameV2");
        GetOwnManager.RegisterFunc(GetOwnBase64);
        GetManagerByPtr.RegisterFunc(GetDataStrByPtr);
        GetManagerByName.RegisterFunc(GetDataStrByName);

        SetManagerByPtr = Svc.PluginInterface.GetIpcProvider<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        SetManagerByName = Svc.PluginInterface.GetIpcProvider<string, string, object>("Moodles.SetStatusManagerByNameV2");
        SetManagerByPtr.RegisterAction(SetByPtr);
        SetManagerByName.RegisterAction(SetByName);

        ClearMangerByPtr = Svc.PluginInterface.GetIpcProvider<nint, object>("Moodles.ClearStatusManagerByPtrV2");
        ClearManagerByName = Svc.PluginInterface.GetIpcProvider<string, object>("Moodles.ClearStatusManagerByNameV2");
        ClearMangerByPtr.RegisterAction(ClearByPtr);
        ClearManagerByName.RegisterAction(ClearByName);

        // Other Dummy Functions
        AddUpdateMoodleByPtr = Svc.PluginInterface.GetIpcProvider<Guid, nint, object>("Moodles.AddOrUpdateMoodleByPtrV2");
        AddUpdateMoodleByName = Svc.PluginInterface.GetIpcProvider<Guid, string, object>("Moodles.AddOrUpdateStatusByNameV2");
        AddUpdateMoodleByPlayer = Svc.PluginInterface.GetIpcProvider<Guid, IPlayerCharacter, object>("Moodles.AddOrUpdateMoodleByPlayerV2");
        AddUpdateMoodleByPtr.RegisterAction(AddOrUpdateStatusByPtr);
        AddUpdateMoodleByName.RegisterAction(AddOrUpdateStatusByName);
        AddUpdateMoodleByPlayer.RegisterAction(AddOrUpdateStatusByPlayer);

        ApplyPresetByPtr = Svc.PluginInterface.GetIpcProvider<Guid, nint, object>("Moodles.ApplyPresetByPtrV2");
        ApplyPresetByName = Svc.PluginInterface.GetIpcProvider<Guid, string, object>("Moodles.ApplyPresetByNameV2");
        ApplyPresetByPlayer = Svc.PluginInterface.GetIpcProvider<Guid, IPlayerCharacter, object>("Moodles.ApplyPresetByPlayerV2");
        ApplyPresetByPtr.RegisterAction(AddPresetByPtr);
        ApplyPresetByName.RegisterAction(AddPresetByName);
        ApplyPresetByPlayer.RegisterAction(AddPresetByPlayer);

        RemoveMoodleByPlayer = Svc.PluginInterface.GetIpcProvider<Guid, IPlayerCharacter, object>("Moodles.RemoveMoodleByPlayerV2");
        RemoveMoodleByPlayer.RegisterAction(RemStatusByPlayer);

        RemoveMoodlesByPtr = Svc.PluginInterface.GetIpcProvider<List<Guid>, nint, object>("Moodles.RemoveMoodlesByPtrV2");
        RemoveMoodlesByName = Svc.PluginInterface.GetIpcProvider<List<Guid>, string, object>("Moodles.RemoveMoodlesByNameV2");
        RemoveMoodlesByPlayer = Svc.PluginInterface.GetIpcProvider<List<Guid>, IPlayerCharacter, object>("Moodles.RemoveMoodlesByPlayerV2");
        RemoveMoodlesByPtr.RegisterAction(RemStatusesByPtr);
        RemoveMoodlesByName.RegisterAction(RemStatusesByName);
        RemoveMoodlesByPlayer.RegisterAction(RemStatusesByPlayer);

        RemovePresetByPlayer = Svc.PluginInterface.GetIpcProvider<Guid, IPlayerCharacter, object>("Moodles.RemovePresetByPlayerV2");
        RemovePresetByPlayer.RegisterAction(RemPresetByPlayer);
        _logger.LogInformation($"IpcProviderMoodles is ready");
        Generic.Safe(() => Ready?.SendMessage());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        Disposing?.SendMessage();

        ApiVersion.UnregisterFunc();

        GetStatusInfo.UnregisterFunc();
        GetStatusInfoList.UnregisterFunc();
        GetPresetInfo.UnregisterFunc();
        GetPresetsInfoList.UnregisterFunc();
        GetRegisteredMoodles.UnregisterFunc();
        GetRegisteredProfiles.UnregisterFunc();

        GetOwnManagerInfo.UnregisterFunc();
        GetManagerInfoByPtr.UnregisterFunc();
        GetOwnManager.UnregisterFunc();
        GetManagerByPtr.UnregisterFunc();
        GetManagerByName.UnregisterFunc();
        SetManagerByPtr.UnregisterAction();
        SetManagerByName.UnregisterAction();
        ClearMangerByPtr.UnregisterAction();
        ClearManagerByName.UnregisterAction();

        AddUpdateMoodleByPtr.UnregisterAction();
        AddUpdateMoodleByName.UnregisterAction();
        AddUpdateMoodleByPlayer.UnregisterAction();
        
        ApplyPresetByPtr.UnregisterAction();
        ApplyPresetByName.UnregisterAction();
        ApplyPresetByPlayer.UnregisterAction();
        
        RemoveMoodleByPlayer.UnregisterAction();
        
        RemoveMoodlesByPtr.UnregisterAction();
        RemoveMoodlesByName.UnregisterAction();
        RemoveMoodlesByPlayer.UnregisterAction();
        
        RemovePresetByPlayer.UnregisterAction();

        return Task.CompletedTask;
    }

    public unsafe void AddOrUpdateStatusByPtr(Guid id, nint addr)
    {
        if (CharaWatcher.TryGetValue(addr, out Character* chara))
            AddUpdateStatusInternal(chara, id);
    }

    public unsafe void AddOrUpdateStatusByName(Guid id, string name)
    {
        if (CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == name, out nint addr))
            AddUpdateStatusInternal((Character*)addr, id);
    }

    public unsafe void AddOrUpdateStatusByPlayer(Guid id, IPlayerCharacter player)
        => AddUpdateStatusInternal((Character*)player.Address, id);

    private unsafe void AddUpdateStatusInternal(Character* chara, Guid guid)
    {
        if (chara == null)
            return;

        if (_manager.SavedStatuses.FirstOrDefault(x => x.GUID == guid) is { } status)
        {
            var sm = chara->GetManager();
            if (!sm.Ephemeral)
                sm.AddOrUpdate(status.PreApply(), true, true);
        }
    }

    private unsafe void AddPresetByPtr(Guid id, nint addr)
    {
        if (CharaWatcher.TryGetValue(addr, out Character* chara))
            ApplyPresetInternal(chara, id);
    }

    private unsafe void AddPresetByName(Guid id, string name)
    {
        if (CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == name, out nint addr))
            ApplyPresetInternal((Character*)addr, id);
    }

    private unsafe void AddPresetByPlayer(Guid id, IPlayerCharacter player)
        => ApplyPresetInternal((Character*)player.Address, id);

    private unsafe void ApplyPresetInternal(Character* chara, Guid guid)
    {
        if (chara == null)
            return;

        if (_manager.SavedPresets.FirstOrDefault(x => x.GUID == guid) is { } preset)
        {
            var sm = chara->GetManager();
            if (!sm.Ephemeral)
                sm.ApplyPreset(preset, _manager);
        }
    }

    private unsafe void RemStatusByPlayer(Guid id, IPlayerCharacter player)
        => RemoveStatusesInternal((Character*)player.Address, [id]);

    private unsafe void RemStatusesByPtr(List<Guid> ids, nint addr)
    {
        if (CharaWatcher.TryGetValue(addr, out Character* chara))
            RemoveStatusesInternal(chara, ids);
    }

    private unsafe void RemStatusesByName(List<Guid> ids, string name)
    {
        if (CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == name, out nint addr))
            RemoveStatusesInternal((Character*)addr, ids);
    }

    private unsafe void RemStatusesByPlayer(List<Guid> ids, IPlayerCharacter player)
        => RemoveStatusesInternal((Character*)player.Address, ids);

    private unsafe void RemoveStatusesInternal(Character* chara, List<Guid> guids)
    {
        if (chara == null)
            return;

        var sm = chara->GetManager();
        var idSet = guids.ToHashSet();
        var toApply = sm.Statuses.Where(s => idSet.Contains(s.GUID));
        foreach (var id in toApply)
            if (!sm.Ephemeral && !id.Persistent)
                sm.Cancel(id);
    }

    private unsafe void RemPresetByPlayer(Guid id, IPlayerCharacter player)
    {
        if (player is null)
            return;

        if (_manager.SavedPresets.FirstOrDefault(x => x.GUID == id) is not { } preset)
            return;

        var sm = ((Character*)player.Address)->GetManager();
        var statusSet = preset.Statuses.ToHashSet();
        var toRemove = sm.Statuses.Where(s => statusSet.Contains(s.GUID));
        foreach (var stat in toRemove)
            if (!stat.Persistent)
                sm.Cancel(stat);
    }

    public static void InvokeManagerModified(nint address)
        => OnManagerModified?.SendMessage(address);

    public static void InvokeStatusUpdated(Guid statusId, bool deleted)
        => OnStatusUpdated?.SendMessage(statusId, deleted);

    public static void InvokePresetUpdated(Guid presetId, bool deleted)
        => OnPresetUpdated?.SendMessage(presetId, deleted);

    public MoodlesStatusInfo GetTupleInfo(Guid id)
        => _manager.SavedStatuses.FirstOrDefault(x => x.GUID == id) is { } status ? status.ToTuple().ToLegacyTuple() : default;

    public List<MoodlesStatusInfo> GetTupleInfoList()
        => _manager.SavedStatuses.Select(s => s.ToTuple().ToLegacyTuple()).ToList();

    public MoodlePresetInfo GetPresetTupleInfo(Guid id)
        => _manager.SavedPresets.FirstOrDefault(x => x.GUID == id) is { } preset ? preset.ToTuple().ToLegacyPreset() : default;

    public List<MoodlePresetInfo> GetPresetTupleInfoList()
        => _manager.SavedPresets.Select(s => s.ToTuple().ToLegacyPreset()).ToList();

    public List<MoodlesMoodleInfo> GetRegisteredMoodlesInfo()
    {
        var ret = new List<MoodlesMoodleInfo>();
        foreach (var x in _manager.SavedStatuses)
            if (_statusesFS.FindLeaf(x, out var path))
                ret.Add((x.GUID, (uint)x.IconID, path.FullName(), x.Title));
        // register the statuses
        return ret;
    }

    public List<MoodlesProfileInfo> GetRegisteredProfilesInfo()
    {
        var ret = new List<MoodlesProfileInfo>();
        foreach (var x in _manager.SavedPresets)
            if (_presetsFS.FindLeaf(x, out var path))
                ret.Add((x.GUID, path.FullName()));
        // register the presets
        return ret;
    }

    public List<MoodlesStatusInfo> GetOwnStatusManagerInfo()
    {
        var manager = LociManager.ClientSM;
        return manager.Statuses.Select(s => s.ToTuple().ToLegacyTuple()).ToList();
    }

    public unsafe List<MoodlesStatusInfo> GetStatusManagerInfoByPtr(nint addr)
    {
        if (!CharaWatcher.RenderedCharas.Contains(addr))
            return [];
        // Otherwise, get the manager
        Character* chara = (Character*)addr;
        if (chara is null)
            return null!;

        var manager = chara->GetManager();
        return manager.Statuses.Select(s => s.ToTuple().ToLegacyTuple()).ToList();
    }

    public string GetOwnBase64()
    {
        var manager = LociManager.ClientSM;
        // Convert the statuses to their packed equivalent.
        var legacyStatuses = manager.Statuses.Select(s => s.ToLegacyStatus());
        return ToBase64(legacyStatuses.ToList());
    }

    public unsafe string GetDataStrByPtr(nint addr)
    {
        if (!CharaWatcher.TryGetValue(addr, out Character* chara))
            return string.Empty;
        var legacyStatuses = chara->GetManager().Statuses.Select(s => s.ToLegacyStatus());
        return ToBase64(legacyStatuses.ToList());
    }

    public unsafe string GetDataStrByName(string name)
    {
        if (!CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == name, out nint addr))
            return string.Empty;
        var chara = (Character*)addr;
        if (chara is null)
            return string.Empty;
        var legacyStatuses = chara->GetManager().Statuses.Select(s => s.ToLegacyStatus());
        return ToBase64(legacyStatuses.ToList());
    }

    public unsafe void SetByPtr(nint addr, string base64Data)
    {
        if (!CharaWatcher.TryGetValue(addr, out Character* chara))
            return;
        var sm = chara->GetManager();
        // Convert and apply the data
        if (string.IsNullOrEmpty(base64Data))
            sm.UpdateStatusesFromDataString(Array.Empty<LociStatus>());
        else
            Apply(sm, Convert.FromBase64String(base64Data));
    }

    public unsafe void SetByName(string nameWorld, string base64Data)
    {
        if (!CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == nameWorld, out nint addr))
            return;
        var chara = (Character*)addr;
        if (chara is null)
            return;
        var sm = chara->GetManager();
        // Convert and apply the data
        if (string.IsNullOrEmpty(base64Data))
            sm.UpdateStatusesFromDataString(Array.Empty<LociStatus>());
        else
            Apply(sm, Convert.FromBase64String(base64Data));
    }

    public unsafe void ClearByPtr(nint addr)
    {
        if (!CharaWatcher.TryGetValue(addr, out Character* chara))
            return;
        var sm = chara->GetManager();
        foreach (var s in sm.Statuses)
            if (!s.Persistent)
                sm.Cancel(s);
    }

    public unsafe void ClearByName(string nameWorld)
    {
        if (!CharaWatcher.TryGetFirst(c => c.GetNameWithWorld() == nameWorld, out nint addr))
            return;
        var chara = (Character*)addr;
        if (chara is null)
            return;
        var sm = chara->GetManager();
        foreach (var s in sm.Statuses)
            if (!s.Persistent)
                sm.Cancel(s);
    }

    // Helpers
    public string ToBase64(List<MyStatus> statuses)
        => statuses.Any() ? Convert.ToBase64String(BinarySerialize(statuses)) : string.Empty;

    public byte[] BinarySerialize(List<MyStatus> statuses)
        => MemoryPackSerializer.Serialize(statuses, LegacyMoodlesEx.SerializerOptions);

    // Setter helpers.
    public void Apply(LociSM sm, byte[] data)
    {
        Generic.Safe(() =>
        {
            // Attempt to deserialize into the current format. If it fails, warn of old formatting.
            var statuses = MemoryPackSerializer.Deserialize<List<MyStatus>>(data, LegacyMoodlesEx.SerializerOptions);
            if (statuses is null)
                throw new Bagagwa("Deserialized Statuses are null");
            // Convert from legacy statuses to loci statuses, then apply them.
            var lociStatuses = statuses.Select(s => s.FromLegacyStatus());
            sm.UpdateStatusesFromDataString(lociStatuses);
        });
    }
}
