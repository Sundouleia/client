using Sundouleia.Interop;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.ModFiles;

/// <summary>
///     Intended to monitor all applied changes for mods in a collection, including inheritance changes. <para />
///     This helps us keep track of what mods are currently used, and what replacements for them are currently active, 
///     allowing us to get additional data from them such as their type, file extensions, etc. <para />
///     
///     This will effectively allow us to have a cached state of penumbra effective changes from the cache, so that we
///     can create a monitored state of loaded resources on the player and their owned objects. <para />
///     
///     This is accomplished by tracking the loaded resources / player resource paths and player resource path calls.
///     When these paths are passed into the monitor, we mark them in our ActiveInSession cache (separate cache) <para />
///     
///     Every time a mod setting changes with the enabled state or setting state changed, we fetch the resolved files from the CollectionCache,
///     and update our ActiveInSession cache accordingly so any files no longer active are removed. <para />
///     
///     Monitoring the state from penumbra will also allow us to view extended information about these files, so we can add/remove/update
///     based on what kind of replacement it is (gear, vfx, animation, emote, etc.) <para />
/// </summary>
public sealed class ActiveModsMonitor : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly PlzNoCrashFrens _noCrashPlz;
    private readonly FileCacheManager _fileDb;
    private readonly IpcManager _ipc;
    private readonly CharaObjectWatcher _watcher;

    public ActiveModsMonitor(ILogger<ActiveModsMonitor> logger, SundouleiaMediator mediator, 
        MainConfig config) : base(logger, mediator)
    {
        _config = config;

    }


    // PENUMBRA NOTES:
    // - CollectionCache houses the combined resolved file changes for a mod collection.
    // - CollectionCache holds cached changes including inheritance from other collections, and is a reliable final source.
    // - CollectionCache references CollectionCacheManager, and holds a CollectionModData, for references.
    // 
    // TODO RESEARCH:
    // - Find where we can relate the CollectionCache.ResolvedFiles and CollectionCache.ModData to correlate to the retrieved retrieved changed item paths of a mod.
    // - How to determine when these are updated (ModSettingChanged maybe) or possibly a new event for it.

    // RESOLVE ROUTE:
    // 1) Player changes a mod setting / mod state.
    //
    // 2) CollectionEditor receives from the mod settings panel (or mod setting API) to update the setting / enabled state.
    //
    // 3) Inheritance is accounted for and correct values are set in collection.GetOwnSettings(mod.Index)!.SetValue(mod, groupIdx, newValue)
    //
    // 4) The change is processed in CollectionEditor via InvokeChange, using the collection as a parameter.
    //
    // 5) In CollectionEditor.InvokeChange, the CommunicatorService ModSettingsChanged is called, providing the old and new
    //    value of the setting, the mod, and the collection. (for API)
    // 
    // 6a) The CommunicatorService Listens to a variety of things to keep the cache up to date:
    //  _framework.Framework.Update += OnFramework;
    //  _communicator.CollectionChange.Subscribe(OnCollectionChange, CollectionChange.Priority.CollectionCacheManager);
    //  _communicator.ModPathChanged.Subscribe(OnModChangeAddition, ModPathChanged.Priority.CollectionCacheManagerAddition);
    //  _communicator.ModPathChanged.Subscribe(OnModChangeRemoval, ModPathChanged.Priority.CollectionCacheManagerRemoval);
    //  _communicator.TemporaryGlobalModChange.Subscribe(OnGlobalModChange, TemporaryGlobalModChange.Priority.CollectionCacheManager);
    //  _communicator.ModOptionChanged.Subscribe(OnModOptionChange, ModOptionChanged.Priority.CollectionCacheManager);
    //  _communicator.ModSettingChanged.Subscribe(OnModSettingChange, ModSettingChanged.Priority.CollectionCacheManager);
    //  _communicator.CollectionInheritanceChanged.Subscribe(OnCollectionInheritanceChange, CollectionInheritanceChanged.Priority.CollectionCacheManager);
    //  _communicator.ModDiscoveryStarted.Subscribe(OnModDiscoveryStarted, ModDiscoveryStarted.Priority.CollectionCacheManager);
    //  _communicator.ModDiscoveryFinished.Subscribe(OnModDiscoveryFinished, ModDiscoveryFinished.Priority.CollectionCacheManager);
    //
    //  if (!MetaFileManager.CharacterUtility.Ready)
    //      MetaFileManager.CharacterUtility.LoadingFinished.Subscribe(IncrementCounters, CharacterUtilityFinished.Priority.CollectionCacheManager);
    //
    // 6b) This CommunicatorService, on collection, ModPath, ModOption, ModSetting, ModDiscovery, Inheritance, any of these changes invoke various functions here.
    //
    // 7a) In our case, OnModOptionChange is called, which processes HandlingInfo, based on type. This result outputs REQUIRESRELOADING and WASPREPARED.
    // 7b) REQUIRESRELOADING is true any time the mod is enabled while the option in the settings tab is changed. (recomputeList in code)
    // 7c) WASPREPARED (justAdd in code) is whenever we do not change from one option to another, but just select/deselect something.
    //
    // 8) If the mod is not enabled, our changes stop here. No additional information is processed, and the cache remains unchanged.
    // 
    // ------ If mod is disabled, all below steps do not apply ------
    // 9) the CollectionCacheManager has a CollectionStorage housing all our collections, it will retrieve all collections that have
    //    caches and are enabled, and will perform either:
    //      if(justAdd) collection._cache!.AddMod(mod, true);
    //      else collection._cache!.ReloadMod(mod, true);
    //
    // 10) CollectionCaches ref the CollectionCacheManager (in the constructor from the CollectionCache, allowing for circular reference without circular dependency)
    //     and process one of the below, based on what step 9 processed:
    //        _manager.AddChange(ChangeData.ModAddition(this, mod, addMetaChanges));
    //        _manager.AddChange(ChangeData.ModReload(this, mod, addMetaChanges));
    //
    // 11) When AddChange is processed, if the Cache is not calculating, it will immediately perform the action if in the framework thread.
    //     Otherwise, it will Enqueue the ChangeData into the changeQueue, which will be processed on the next framework tick.
    //
    // 12) When ChangeData.Apply() is called, with the changeData, it performs the following logic:
    //
    //          if (data.Cache.Calculating == -1)
    //          {
    //              if (_framework.Framework.IsInFrameworkUpdateThread)
    //                  data.Apply();
    //              else
    //                  _changeQueue.Enqueue(data);
    //          }
    //          else if (data.Cache.Calculating == Environment.CurrentManagedThreadId)
    //          {
    //              data.Apply();
    //          }
    //          else
    //          {
    //              _changeQueue.Enqueue(data);
    //          }
    //
    // 13a) In Apply(), it will inform the CollectionCache to perform either RemoveModSync, AddModSync, ReloadModSync, or ForceFileSync.
    // 13b) All of these methods perform the actual internal operations on the cache that resolve conflicts and update the [ResolvedFiles]
    //
    // 14a) When the ResolvedFiles are added/removed, the ResolvedFileChanged is invoked, which performs the file swap update,
    //      and reassigns any potential resolved conflicts after addition / removal.
    // 14b) In addition to removal from the ResolvedFiles, the mod is also removed from ModData.RemoveMod (the CollectionModData object in the collectionCache)
    //
    // 14c) During AddMod, it will retrieve the mod's FileRedirections and pass them all into the function AddFile(path, file, mod), one at a time.
    //      In this method, if the file fails path redirection validation, or redirection is not supported, the file is not added.
    //
    // 15) CollectionCache.ResolvedFiles is now updated, and our ActiveModsMonitor can retrieve the current state from here.

}