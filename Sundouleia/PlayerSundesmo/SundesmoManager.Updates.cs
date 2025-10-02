using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Util;

namespace Sundouleia.Pairs;

public sealed partial class SundesmoManager
{
    // Should happen only on initial loads.
    public void ReceiveIpcUpdateFull(UserData target, ModDataUpdate modData, VisualDataUpdate ipcData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"Received update for {sundesmo.GetNickAliasOrUid()}'s mod and appearance data!", LoggerType.Callbacks);
        sundesmo.ApplyFullData(modData, ipcData);
    }

    // Happens whenever mods should be added or removed.
    public void ReceiveIpcUpdateMods(UserData target, ModDataUpdate modData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"Received update for {sundesmo.GetNickAliasOrUid()}'s mod data!", LoggerType.Callbacks);
        sundesmo.ApplyModData(modData);
    }

    // Happens whenever non-mod visuals are updated.
    public void ReceiveIpcUpdateOther(UserData target, VisualDataUpdate ipcData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"{sundesmo.GetNickAliasOrUid()}'s appearance data updated!", LoggerType.Callbacks);
        sundesmo.ApplyIpcData(ipcData);
    }

    // Happens whenever a single non-mod appearance item is updated.
    public void ReceiveIpcUpdateSingle(UserData target, OwnedObject relatedObject, IpcKind type, string newData)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        Logger.LogTrace($"{sundesmo.GetNickAliasOrUid()}'s [{relatedObject}] updated its [{type}] data!", LoggerType.Callbacks);
        sundesmo.ApplyIpcSingle(relatedObject, type, newData);
    }

    // A pair updated one of their global permissions, so handle the change properly.
    public void PermChangeGlobal(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // Fail if the change could not be properly set.
        if (!PropertyChanger.TrySetProperty(sundesmo.PairGlobals, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        // Log change and lazily recreate the pairlist.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s GlobalPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();
    }

    public void PermChangeGlobal(UserData target, GlobalPerms newGlobals)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // cache prev globals and update them.
        var prevGlobals = sundesmo.PairGlobals with { };
        sundesmo.UserPair.Globals = newGlobals;

        // Log change and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s GlobalPerms updated in bulk]", LoggerType.PairDataTransfer);
        RecreateLazy();
    }

    public void PermChangeUnique(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // If we need to cache the previous state of anything here do so.
        var prevPause = sundesmo.OwnPerms.PauseVisuals;

        // Perform change.
        if (!PropertyChanger.TrySetProperty(sundesmo.OwnPerms, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        // Log change and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s OwnPairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Clear profile is pause toggled.
        if (prevPause != sundesmo.OwnPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }

    public void PermChangeUniqueOther(UserData target, string permName, object newValue)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // If we need to cache the previous state of anything here do so.
        var prevPause = sundesmo.PairPerms.PauseVisuals;

        if (!PropertyChanger.TrySetProperty(sundesmo.PairPerms, permName, newValue, out var finalVal) || finalVal is null)
            throw new InvalidOperationException($"Failed to set property '{permName}' on {sundesmo.GetNickAliasOrUid()} with value '{newValue}'");

        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s PairPerm {{{permName}}} is now {{{finalVal}}}]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Toggle pausing if pausing changed.
        if (prevPause != sundesmo.PairPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }

    public void PermBulkChangeUnique(UserData target, PairPerms newPerms)
    {
        if (!_allSundesmos.TryGetValue(target, out var sundesmo))
            throw new InvalidOperationException($"User [{target.AliasOrUID}] not found.");

        // cache prev state and update them.
        var prevPerms = sundesmo.OwnPerms with { };
        sundesmo.UserPair.OwnPerms = newPerms;

        // Log and recreate the pair list.
        Logger.LogDebug($"[{sundesmo.GetNickAliasOrUid()}'s OwnPerms updated in bulk.]", LoggerType.PairDataTransfer);
        RecreateLazy();

        // Clear profile if pausing changed.
        if (prevPerms.PauseVisuals != newPerms.PauseVisuals)
            Mediator.Publish(new ClearProfileDataMessage(target));
    }
}
