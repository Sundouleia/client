using CkCommons;
using CkCommons.Helpers;
using CkCommons.HybridSaver;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Interop;
using Sundouleia.Loci;
using Sundouleia.Loci.Data;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.Pairs;

/// <summary>
///     Manages the currently active status managers processed by Loci. <para />
///     Don't worry about turning this into a savable thing until later.
/// </summary>
public sealed class LociManager : DisposableMediatorSubscriberBase, IHybridSavable
{
    private readonly MainConfig _config;
    private readonly ConfigFileProvider _fileNames;
    private readonly HybridSaveService _saver;

    // Stores the ClientSM's of all our client's created characters
    private static Dictionary<string, LociSM> _clientSMs = new();

    private static Dictionary<string, LociSM> _statusManagers = new();
    private List<LociStatus> _statuses = [];
    private List<LociPreset> _presets  = [];
    public LociManager(ILogger<LociManager> logger, SundouleiaMediator mediator,
        MainConfig config, ConfigFileProvider fileNames, HybridSaveService saver)
        : base(logger, mediator)
    {
        _config = config;
        _fileNames = fileNames;
        _saver = saver;
        // Load the config and mark for save on disposal.
        Load();
        _saver.MarkForSaveOnDispose(this);
        // Process object creation here
        Mediator.Subscribe<WatchedObjectCreated>(this, _ => OnObjectCreated(_.Address));
        Mediator.Subscribe<WatchedObjectDestroyed>(this, _ => OnObjectDeleted(_.Address));
        Mediator.Subscribe<TerritoryChanged>(this, _ => OnTerritoryChange(_.PrevTerritory, _.NewTerritory));
        Svc.ClientState.Login += OnLogin;

        if (Svc.ClientState.IsLoggedIn)
            OnLogin();
    }

    // A static LociSM for the ClientPlayer (May need to make nullable for certainty if this causes issues.
    public static LociSM ClientSM = new LociSM();
    public static IReadOnlyDictionary<string, LociSM> StatusManagers => _statusManagers;

    // Modifiable, internal Status & Presets.
    internal IReadOnlyList<LociStatus> SavedStatuses => _statuses;
    internal IReadOnlyList<LociPreset> SavedPresets => _presets;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.Login -= OnLogin;
    }

    private async void OnLogin()
    {
        // Wait for the player to be fully loaded in first.
        await SundouleiaEx.WaitForPlayerLoading().ConfigureAwait(false);
        // Init data
        InitializeData();
    }

    // This occurs after the player is finished rendering.
    private void OnTerritoryChange(ushort prev, ushort next)
    {
        var clientNameWorld = PlayerData.NameWithWorld;
        // Clean up all non-client and non-ephemeral managers.
        foreach (var (name, lociSM) in _statusManagers.ToList())
        {
            if (name == clientNameWorld || lociSM.Ephemeral || lociSM.OwnerValid)
                continue;
            // Remove it
            _statusManagers.Remove(name);
        }
    }

    private unsafe void InitializeData()
    {
        InitClientSM();
        // Then also do this for all other characters
        foreach (var charaAddr in CharaWatcher.RenderedCharas)
        {
            var chara = (Character*)charaAddr;
            if (chara is null || !chara->IsCharacter() || chara->ObjectKind is not ObjectKind.Pc)
                continue;

            var nameWorld = chara->GetNameWithWorld();
            // Assign if existing, otherwise create and assign
            if (_statusManagers.TryGetValue(nameWorld, out var lociSM))
            {
                lociSM.Owner = chara;
                Logger.LogTrace($"Assigned {{{nameWorld}}} to their LociSM", LoggerType.LociData);
            }
            else
            {
                var newSM = new LociSM() { Owner = chara };
                _statusManagers.TryAdd(nameWorld, newSM);
                Logger.LogTrace($"Created and Assigned {{{nameWorld}}} to a new LociSM", LoggerType.LociData);
            }
        }
    }

    private unsafe void InitClientSM()
    {
        var playerName = PlayerData.NameWithWorld;
        // If we have a stored clientSM
        if (_clientSMs.TryGetValue(playerName, out var existing))
        {
            Logger.LogDebug($"Found existing client status manager for player {playerName}, assigning ClientSM.", LoggerType.LociData);
            ClientSM = existing;
            ClientSM.Owner = PlayerData.Character;
            // If one exists in the StatusManagers, dictionary, overwrite the value.
            _statusManagers[playerName] = existing;
        }
        // Otherwise, if it exists, we need to ensure sync.
        else if (_statusManagers.TryGetValue(playerName, out var existingSM))
        {
            Logger.LogDebug($"Found existing status manager for player {playerName}, assigning to ClientSM and syncing data.", LoggerType.LociData);
            ClientSM = existingSM;
            ClientSM.Owner = PlayerData.Character;
            // Add to then tracked clientSM's mimicing the value from the status managers.
            _clientSMs.TryAdd(playerName, existingSM);
        }
        // Otherwise, we need to create a new entry for both.
        else
        {
            Logger.LogDebug($"No existing client status manager for player {playerName}, creating new one and assigning to ClientSM and StatusManagers.", LoggerType.LociData);
            var manager = new LociSM() { Owner = PlayerData.Character };
            _clientSMs.TryAdd(playerName, manager);
            _statusManagers.TryAdd(playerName, manager);
            ClientSM = manager;
        }
    }

    private unsafe void OnObjectCreated(IntPtr address)
    {
        var chara = (Character*)address;
        if (chara is null || chara->ObjectIndex >= 200 || !chara->IsCharacter() || chara->ObjectKind is not ObjectKind.Pc)
            return;

        var nameWorld = chara->GetNameWithWorld();
        if (_statusManagers.TryGetValue(nameWorld, out var lociSM))
        {
            lociSM.Owner = chara;
            Logger.LogTrace($"Assigned {{{nameWorld}}} to their LociSM", LoggerType.LociData);
        }
        else
        {
            var newSM = new LociSM() { Owner = chara };
            _statusManagers.TryAdd(nameWorld, newSM);
            Logger.LogTrace($"Created and Assigned {{{nameWorld}}} to a new LociSM", LoggerType.LociData);
        }
    }

    private unsafe void OnObjectDeleted(IntPtr address)
    {
        var chara = (Character*)address;
        if (chara is null || chara->ObjectIndex >= 200 || !chara->IsCharacter() || chara->ObjectKind is not ObjectKind.Pc)
            return;

        var nameWorld = chara->GetNameWithWorld();
        if (_statusManagers.TryGetValue(nameWorld, out var lociSM))
        {
            // Do not de-init client SM
            if (lociSM == ClientSM)
                return;

            // We dont want to remove the status manager, but we do want to unassign the owner.
            if (lociSM.OwnerValid)
                lociSM.Owner = null;

            // If ephemeral, we can assume they will apply things right away after, so remove them.
            if (lociSM.Ephemeral)
            {
                _statusManagers.Remove(nameWorld);
                Logger.LogDebug($"Removed LociSM for {nameWorld}, as it was controlled by external sources.", LoggerType.LociData);

            }
        }
    }

    // For registering and unregistering identifiers to existing status managers.
    public unsafe bool AttachIdToActor(string nameWorld, string identifier)
    {
        // Fail if the Client.
        if (PlayerData.NameWithWorld == nameWorld)
            return false;
        // Grab the manager, creating one if not yet present.
        var sm = GetStatusManager(nameWorld);
        // return if we could add it to the manager or not.
        return sm.EphemeralHosts.Add(identifier);
    }

    public unsafe bool DetachIdFromActor(string nameWorld, string identifier)
    {
        if (PlayerData.NameWithWorld == nameWorld)
            return false;
        // Grab the manager, creating one if not yet present.
        var sm = GetStatusManager(nameWorld);
        // return if we could add it to the manager or not.
        return sm.EphemeralHosts.Remove(identifier);
    }

    /// <summary>
    ///     Retrieves a status manager for a given Player Name. 
    ///     If one does not exist, an empty, invalid one is created.
    /// </summary>
    public unsafe static LociSM GetStatusManager(string playerName, bool create = true)
    {
        if (!StatusManagers.TryGetValue(playerName, out var manager))
        {
            if (create)
            {
                manager = new();
                // Add it to the dictionary.
                _statusManagers.TryAdd(playerName, manager);
                // If we can identify the player from the object watcher, we should set it in the manager.
                if (CharaWatcher.TryGetFirst(x => x.GetNameWithWorld() == playerName, out var chara))
                    manager.Owner = (Character*)chara;
            }
        }
        return manager!;
    }

    public LociStatus CreateStatus(string name)
    {
        name = RegexEx.EnsureUniqueName(name, _statuses, (s) => s.Title);
        var newStatus = new LociStatus() { Title = name };
        _statuses.Add(newStatus);
        _saver.Save(this);
        Mediator.Publish(new LociStatusChanged(FSChangeType.Created, newStatus, null));
        return newStatus;
    }

    public LociPreset CreatePreset(string name)
    {
        name = RegexEx.EnsureUniqueName(name, _statuses, (s) => s.Title);
        var newPreset = new LociPreset() { Title = name };
        _presets.Add(newPreset);
        _saver.Save(this);
        Mediator.Publish(new LociPresetChanged(FSChangeType.Created, newPreset, null));
        return newPreset;
    }

    public bool ImportStatus(LociStatus? imported)
    {
        if (imported is null)
            return false;

        var newStatus = imported.NewtonsoftDeepClone();
        newStatus.Title = RegexEx.EnsureUniqueName(imported.Title, _statuses, (s) => s.Title);
        _statuses.Add(newStatus);
        _saver.Save(this);
        Mediator.Publish(new LociStatusChanged(FSChangeType.Created, newStatus, null));
        return true;
    }

    public bool ImportPreset(LociPreset? imported)
    {
        if (imported is null)
            return false;

        var newPreset = imported.NewtonsoftDeepClone();
        newPreset.GUID = Guid.NewGuid();
        newPreset.Title = RegexEx.EnsureUniqueName(imported.Title, _presets, (s) => s.Title);
        _presets.Add(newPreset);
        _saver.Save(this);
        Mediator.Publish(new LociPresetChanged(FSChangeType.Created, newPreset, null));
        return true;
    }

    public LociStatus CloneStatus(LociStatus other, string newName)
    {
        newName = RegexEx.EnsureUniqueName(newName, _statuses, t => t.Title);
        var clonedItem = other.NewtonsoftDeepClone();
        clonedItem.GUID = Guid.NewGuid();
        clonedItem.Title = newName;
        _statuses.Add(clonedItem);
        _saver.Save(this);
        Logger.LogDebug($"Cloned status {other.Title} to {newName}.", LoggerType.LociData);
        Mediator.Publish(new LociStatusChanged(FSChangeType.Created, clonedItem, null));
        return clonedItem;
    }

    public LociPreset ClonePreset(LociPreset other, string newName)
    {
        newName = RegexEx.EnsureUniqueName(newName, _presets, t => t.Title);
        var clonedItem = other.NewtonsoftDeepClone();
        clonedItem.GUID = Guid.NewGuid();
        clonedItem.Title = newName;
        _presets.Add(clonedItem);
        _saver.Save(this);
        Logger.LogDebug($"Cloned preset {other.Title} to {newName}.", LoggerType.LociData);
        Mediator.Publish(new LociPresetChanged(FSChangeType.Created, clonedItem, null));
        return clonedItem;
    }

    public void RenameStatus(LociStatus status, string newName)
    {
        var prevName = status.Title;
        newName = RegexEx.EnsureUniqueName(newName, _statuses, (s) => s.Title);
        Logger.LogDebug($"Renaming status {prevName} to {newName}.", LoggerType.LociData);
        status.Title = newName;
        _saver.Save(this);
        Mediator.Publish(new LociStatusChanged(FSChangeType.Renamed, status, prevName));
    }

    public void RenamePreset(LociPreset preset, string newName)
    {
        var prevName = preset.Title;
        newName = RegexEx.EnsureUniqueName(newName, _presets, (s) => s.Title);
        Logger.LogDebug($"Renaming preset {prevName} to {newName}.", LoggerType.LociData);
        preset.Title = newName;
        _saver.Save(this);
        Mediator.Publish(new LociPresetChanged(FSChangeType.Renamed, preset, prevName));
    }

    public void MarkStatusModified(LociStatus status, string? newName = null)
    {
        // Ensure the title is still unique.
        var prevName = status.Title;
        if (newName is not null)
        {
            newName = RegexEx.EnsureUniqueName(newName, _statuses, (s) => s.Title);
            if (prevName != newName)
                Logger.LogInformation($"Modified status {prevName} to {newName} due to title conflict.", LoggerType.LociData);
            else
                Logger.LogDebug($"Modified status {newName}.", LoggerType.LociData);
            status.Title = newName;
        }
        _saver.Save(this);
        Mediator.Publish(new LociStatusChanged(FSChangeType.Modified, status, prevName != status.Title ? status.Title : null));
        IpcProviderLoci.OnStatusModified(status, false);
    }

    public void MarkPresetModified(LociPreset preset, string? newName = null)
    {
        // Ensure the title is still unique.
        var prevName = preset.Title;
        if (newName is not null)
        {
            newName = RegexEx.EnsureUniqueName(newName, _statuses, (s) => s.Title);
            if (prevName != newName)
                Logger.LogInformation($"Modified preset {prevName} to {newName} due to title conflict.", LoggerType.LociData);
            else
                Logger.LogDebug($"Modified preset {newName}.", LoggerType.LociData);
            preset.Title = newName;
        }
        // Save, then inform mediator and IPC.
        _saver.Save(this);
        Mediator.Publish(new LociPresetChanged(FSChangeType.Modified, preset, prevName != preset.Title ? preset.Title : null));
        IpcProviderLoci.OnPresetModified(preset, false);
    }

    public void DeleteStatus(LociStatus status)
    {
        if (_statuses.Remove(status))
        {
            Logger.LogDebug($"Deleted status {status.Title}.", LoggerType.LociData);
            Mediator.Publish(new LociStatusChanged(FSChangeType.Deleted, status, null));
            _saver.Save(this);
        }
    }

    public void DeletePreset(LociPreset preset)
    {
        if (_presets.Remove(preset))
        {
            Logger.LogDebug($"Deleted preset {preset.Title}.", LoggerType.LociData);
            Mediator.Publish(new LociPresetChanged(FSChangeType.Deleted, preset, null));
            _saver.Save(this);
        }
    }

    public void Save()
        => _saver.Save(this);

    public void MigrateStatusesFromConfig(JObject jObj)
    {
        if (jObj is null)
            return;
        var savedStatuses = jObj["SavedStatuses"]?.ToObject<List<JObject>>() ?? new();
        foreach (var statusObj in savedStatuses)
        {
            try
            {
                // Construct the LociStatus
                var status = new LociStatus
                {
                    GUID = statusObj["GUID"]?.ToObject<Guid>() ?? throw new Exception("Status missing GUID"),
                    IconID = statusObj["IconID"]?.ToObject<int>() ?? 0,
                    Title = statusObj["Title"]?.ToObject<string>() ?? "",
                    Description = statusObj["Description"]?.ToObject<string>() ?? "",
                    CustomFXPath = statusObj["CustomFXPath"]?.ToObject<string>() ?? "",
                    ExpiresAt = statusObj["ExpiresAt"]?.ToObject<long>() ?? 0,
                    Type = (StatusType)(statusObj["Type"]?.ToObject<byte>() ?? 0), // convert to byte enum
                    Modifiers = (Modifiers)(statusObj["Modifiers"]?.ToObject<int>() ?? 0),
                    Stacks = statusObj["Stacks"]?.ToObject<int>() ?? 1,
                    StackSteps = statusObj["StackSteps"]?.ToObject<int>() ?? 0,
                    ChainedStatus = statusObj["ChainedStatus"]?.ToObject<Guid>() ?? Guid.Empty,
                    ChainTrigger = (ChainTrigger)(statusObj["ChainTrigger"]?.ToObject<int>() ?? 0),
                    Applier = statusObj["Applier"]?.ToObject<string>() ?? "",
                    Dispeller = statusObj["Dispeller"]?.ToObject<string>() ?? "",
                    Persistent = statusObj["Persistent"]?.ToObject<bool>() ?? false,
                    Days = statusObj["Days"]?.ToObject<int>() ?? 0,
                    Hours = statusObj["Hours"]?.ToObject<int>() ?? 0,
                    Minutes = statusObj["Minutes"]?.ToObject<int>() ?? 0,
                    Seconds = statusObj["Seconds"]?.ToObject<int>() ?? 0,
                    NoExpire = statusObj["NoExpire"]?.ToObject<bool>() ?? false,
                    AsPermanent = statusObj["AsPermanent"]?.ToObject<bool>() ?? false
                };
                // Ignore clones
                if (SavedStatuses.Any(s => s.GUID == status.GUID))
                    continue;
                // Ensure unique title.
                status.Title = RegexEx.EnsureUniqueName(status.Title, SavedStatuses, s => s.Title);
                _statuses.Add(status);
            }
            catch
            {
                Logger.LogWarning($"Failed to migrate status: {statusObj}");
            }
        }
        _saver.Save(this);
    }

    public void MigratePresetsFromConfig(JObject jObj)
    {
        if (jObj is null)
            return;
        var savedPresets = jObj["SavedPresets"]?.ToObject<List<JObject>>() ?? new();
        foreach (var presetObj in savedPresets)
        {
            try
            {
                var preset = new LociPreset
                {
                    GUID = presetObj["GUID"]?.ToObject<Guid>() ?? Guid.NewGuid(),
                    Statuses = presetObj["Statuses"]?.ToObject<List<Guid>>() ?? new(),
                    ApplyType = (PresetApplyType)(presetObj["ApplicationType"]?.ToObject<byte>() ?? 0),
                    Title = presetObj["Title"]?.ToObject<string>() ?? ""
                };
                // Prevent duplicates
                if (SavedPresets.Any(p => p.GUID == preset.GUID))
                    continue;
                // Ensure unique title.
                preset.Title = RegexEx.EnsureUniqueName(preset.Title, SavedPresets, s => s.Title);
                _presets.Add(preset);
            }
            catch
            {
                Logger.LogWarning($"Failed to migrate preset: {presetObj}");
            }
        }
        _saver.Save(this);
    }

    #region HybridSavable
    public int ConfigVersion => 0;
    public HybridSaveType SaveType => HybridSaveType.Json;
    public DateTime LastWriteTimeUTC { get; private set; } = DateTime.MinValue;
    public string GetFileName(ConfigFileProvider files, out bool isAccountUnique)
        => (isAccountUnique = false, files.LociConfig).Item2;
    
    public void WriteToStream(StreamWriter writer) => throw new NotImplementedException();
    public string JsonSerialize()
    {
        // construct the config object to serialize.
        return new JObject()
        {
            ["Version"] = ConfigVersion,
            ["Statuses"] = JArray.FromObject(_statuses),
            ["Presets"] = JArray.FromObject(_presets),
            ["ClientManagers"] = JObject.FromObject(_clientSMs),
        }.ToString(Formatting.None); // No pretty formatting here.
    }

    public void Load()
    {
        var file = _fileNames.LociConfig;
        Logger.LogInformation($"Loading Loci Config for: {file}");
        _statuses.Clear();
        _presets.Clear();
        if (!File.Exists(file))
        {
            Logger.LogWarning($"No Loci Config found at {file}");
            // create a new file with default values.
            _saver.Save(this);
            return;
        }

        // Read the json from the file.
        var jsonText = File.ReadAllText(file);
        var jObject = JObject.Parse(jsonText);
        var version = jObject["Version"]?.Value<int>() ?? 0;

        _statuses = jObject["Statuses"]?.ToObject<List<LociStatus>>() ?? new List<LociStatus>();
        _presets = jObject["Presets"]?.ToObject<List<LociPreset>>() ?? new List<LociPreset>();
        _clientSMs = jObject["ClientManagers"]?.ToObject<Dictionary<string, LociSM>>() ?? new Dictionary<string, LociSM>();
        // Clear out all data aside from statuses from the clientManagers.
        foreach (var (name, data) in _clientSMs.ToList())
        {
            data.AddTextShown.Clear();
            data.RemTextShown.Clear();
            data.LockedStatuses.Clear();
            data.EphemeralHosts.Clear();
        }
        // Update the saved data.
        _saver.Save(this);
    }
    #endregion HybridSavable
}