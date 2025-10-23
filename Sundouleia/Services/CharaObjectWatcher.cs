using CkCommons;
using CkCommons.Raii;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using SundouleiaAPI.Network;
using System.Diagnostics.CodeAnalysis;
using static Lumina.Data.Parsing.Layer.LayerCommon;

namespace Sundouleia.Watchers;

/// <summary> 
///     ClientState.LocalPlayer doesn't allow us to get player data outside the games framework thread. <para />
///     This service tracks all Client-Owned Object Creation, Destruction, & Notifiers. <para />
///     This allows us to cache an address that we can guarantee will always be the current 
///     valid state without checking every tick. <para />
/// </summary>
public class CharaObjectWatcher : DisposableMediatorSubscriberBase
{
    internal Hook<Character.Delegates.OnInitialize> OnCharaInitializeHook;
    internal Hook<Character.Delegates.Dtor> OnCharaDestroyHook;
    internal Hook<Character.Delegates.Terminate> OnCharaTerminateHook;
    internal Hook<Companion.Delegates.OnInitialize> OnCompanionInitializeHook;
    internal Hook<Companion.Delegates.Terminate> OnCompanionTerminateHook;

    private readonly CancellationTokenSource _runtimeCTS = new();

    public unsafe CharaObjectWatcher(ILogger<CharaObjectWatcher> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
        OnCharaInitializeHook = Svc.Hook.HookFromAddress<Character.Delegates.OnInitialize>((nint)Character.StaticVirtualTablePointer->OnInitialize, InitializeCharacter);
        OnCharaTerminateHook = Svc.Hook.HookFromAddress<Character.Delegates.Terminate>((nint)Character.StaticVirtualTablePointer->Terminate, TerminateCharacter);
        OnCharaDestroyHook = Svc.Hook.HookFromAddress<Character.Delegates.Dtor>((nint)Character.StaticVirtualTablePointer->Dtor, DestroyCharacter);
        //OnCompanionInitializeHook = Svc.Hook.HookFromAddress<Companion.Delegates.OnInitialize>((nint)Companion.StaticVirtualTablePointer->OnInitialize, InitializeCompanion);
        //OnCompanionTerminateHook = Svc.Hook.HookFromAddress<Companion.Delegates.Terminate>((nint)Companion.StaticVirtualTablePointer->Terminate, TerminateCompanion);

        OnCharaInitializeHook.SafeEnable();
        OnCharaTerminateHook.SafeEnable();
        OnCharaDestroyHook.SafeEnable();
        OnCompanionInitializeHook.SafeEnable();
        OnCompanionTerminateHook.SafeEnable();

        // Collect data from existing objects
        CollectInitialData();
    }

    // A persistent static cache holding all rendered Character pointers.
    public static HashSet<nint> RenderedCharas { get; private set; } = new();
    public static HashSet<nint> RenderedCompanions { get; private set; } = new();

    // Public, Accessible, Managed pointer address to Owned Object addresses
    public ConcurrentDictionary<nint, OwnedObject> WatchedTypes { get; private set; } = new();
    public HashSet<nint> CurrentOwned { get; private set; } = new();
    public nint WatchedPlayerAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedMinionMountAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedPetAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedCompanionAddr { get; private set; } = IntPtr.Zero;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Cancel, but do not dispose, the token.
        _runtimeCTS.SafeCancel();

        OnCharaInitializeHook?.Dispose();
        OnCharaTerminateHook?.Dispose();
        OnCharaDestroyHook?.Dispose();
        OnCompanionInitializeHook?.Dispose();
        OnCompanionTerminateHook?.Dispose();

        RenderedCharas.Clear();
        RenderedCompanions.Clear();
        WatchedTypes.Clear();
    }


    /// <summary>
    ///     Determine if the Sundesmo PlayerHandler is currently rendered. <para />
    ///     Intended to help with object initialization via Sundesmo creation.
    /// </summary>
    /// <returns> True if a match was found, false otherwise. If false, output is <see cref="IntPtr.Zero"/> </returns>
    public bool TryGetExisting(PlayerHandler handler, out IntPtr address)
    {
        address = IntPtr.Zero;

        if (handler.IsRendered && handler.Address != IntPtr.Zero)
        {
            address = handler.Address;
            return true;
        }
     
        // Grab the Ident, and then run a check against all rendered characters.
        var sundesmoIdent = handler.Sundesmo.Ident;
        foreach (var addr in RenderedCharas)
        {
            // Check via their hashed ident. If it doesn't match, skip.
            var ident = SundouleiaSecurity.GetIdentHashByCharacterPtr(addr);
            if (ident != sundesmoIdent)
                continue;

            // Ident match found, so call the handlers object rendered method.
            address = addr;
            return true;
        }
        return false;
    }

    public bool TryGetExisting(string identToCheck, out IntPtr address)
    {
        address = IntPtr.Zero;
        foreach (var addr in RenderedCharas)
        {
            // Check via their hashed ident. If it doesn't match, skip.
            var charaIdent = SundouleiaSecurity.GetIdentHashByCharacterPtr(addr);
            if (charaIdent != identToCheck)
                continue;
            // Ident match found, so call the handlers object rendered method.
            address = addr;
            return true;
        }
        return false;
    }


    /// <summary>
    ///     Determine if one of the sundesmo's OwnedObjects is currently rendered. <para />
    ///     Intended to help with object initialization via Sundesmo creation.
    /// </summary>
    /// <returns> True if a match was found, false otherwise. If false, output is <see cref="IntPtr.Zero"/> </returns>
    public bool TryGetExisting(PlayerOwnedHandler handler, out IntPtr address)
    {
        address = IntPtr.Zero;
        // Must be valid.
        if (!handler.Sundesmo.IsRendered || handler.Address != IntPtr.Zero)
            return false;

        // Get the known addresses of the type we are looking for.
        var parentId = handler.Sundesmo.PlayerEntityId;

        // Check against all known addresses.
        foreach (var addr in RenderedCharas)
        {
            if (!IsMatch(addr))
                continue;
            // Match found, set address and return true.
            address = addr;
            return true;
        }
        return false;

        bool IsMatch(nint addr)
            => handler.ObjectType switch
            {
                OwnedObject.MinionOrMount => handler.Sundesmo.IsMountMinionAddress(addr),
                OwnedObject.Pet => handler.Sundesmo.IsPetAddress(addr),
                OwnedObject.Companion => handler.Sundesmo.IsCompanionAddress(addr),
                _ => false,
            };
    }

    public nint FromOwned(OwnedObject kind)
        => kind switch
        {
            OwnedObject.Player => WatchedPlayerAddr,
            OwnedObject.MinionOrMount => WatchedMinionMountAddr,
            OwnedObject.Pet => WatchedPetAddr,
            OwnedObject.Companion => WatchedCompanionAddr,
            _ => IntPtr.Zero,
        };

    private unsafe void CollectInitialData()
    {
        var objManager = GameObjectManager.Instance();
        for (var i = 0; i < 200; i++)
        {
            GameObject* obj = objManager->Objects.IndexSorted[i];
            if (obj is null) continue;
            // this is confusing because sometimes these can be either?
            if (obj->GetObjectKind() is (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion or ObjectKind.Mount))
                NewCharacterRendered(obj);
        }
    }

    /// <summary>
    ///     Entry point for initialized characters. Should interface with anything 
    ///     wishing to detect created objects. <para />
    ///     Doing so will ensure any final lines are processed prior to the address invalidating.
    /// </summary>
    private unsafe void NewCharacterRendered(GameObject* chara)
    {
        Logger.LogDebug($"New Character Rendered: {(nint)chara:X} - {chara->GetName()}");
        var address = (nint)chara;
        if (address == OwnedObjects.PlayerAddress)
        {
            AddOwnedObject(OwnedObject.Player, address);
            WatchedPlayerAddr = address;
        }
        else if (address == OwnedObjects.MinionOrMountAddress)
        {
            AddOwnedObject(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = address;
        }
        else if (address == OwnedObjects.PetAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            AddOwnedObject(OwnedObject.Pet, address);
            WatchedPetAddr = address;
        }
        else if (chara->BattleNpcSubKind is BattleNpcSubKind.Buddy && address == OwnedObjects.CompanionAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            AddOwnedObject(OwnedObject.Companion, address);
            WatchedCompanionAddr = address;
        }
        // Should PROBABLY ignore event NPC's lol. Idk though!
        else
        {
            // it is possible we may have difficulty assessing order priority of mediator calls here, if this is the case, 
            // we can always call these directly.
            RenderedCharas.Add(address);
            Mediator.Publish(new WatchedObjectCreated(address));
        }
    }

    private unsafe void CharacterRemoved(GameObject* chara)
    {
        Logger.LogDebug($"Character Removed: {(nint)chara:X} - {chara->GetName()}");
        var address = (nint)chara;
        if (address == WatchedPlayerAddr)
        {
            RemoveOwnedObject(OwnedObject.Player, address);
            WatchedPlayerAddr = IntPtr.Zero;
        }
        else if (address == WatchedMinionMountAddr)
        {
            RemoveOwnedObject(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = IntPtr.Zero;
        }
        else if (address == WatchedPetAddr)
        {
            RemoveOwnedObject(OwnedObject.Pet, address);
            WatchedPetAddr = IntPtr.Zero;
        }
        else if (address == WatchedCompanionAddr)
        {
            RemoveOwnedObject(OwnedObject.Companion, address);
            WatchedCompanionAddr = IntPtr.Zero;
        }
        else
        {
            RenderedCharas.Remove(address);
            Mediator.Publish(new WatchedObjectDestroyed(address));
        }
    }

    // Best to leave this for logging mostly, and debug why we should include later.
    // Causes a lot of duplicate additions as they can be called at the same time.
    //private void NewCompanionRendered(Companion* companion)
    //{
    //    Logger.LogDebug($"New Companion Rendered: {(nint)companion:X} - {companion->NameString}");
    //    var address = (nint)companion;
    //    if (OwnedObjects.PlayerAddress != IntPtr.Zero && companion->ObjectKind == ObjectKind.BattleNpc && companion->OwnerId == OwnedObjects.PlayerObject->EntityId)
    //    {
    //        AddOwnedObject(OwnedObject.Companion, address);
    //        WatchedCompanionAddr = address;
    //    }
    //    // if it doesn't exist already
    //    RenderedCompanions.Add(address);
    //    Mediator.Publish(new WatchedObjectCreated(address));
    //}
    // Best to leave this for logging mostly, and debug why we should include later.
    // Causes a lot of duplicate additions as they can be called at the same time.
    //private void CompanionRemoved(Companion* companion)
    //{
    //    //Logger.LogDebug($"Companion Removed: {(nint)companion:X} - {companion->NameString}");
    //    var address = (nint)companion;
    //    if (OwnedObjects.PlayerAddress != IntPtr.Zero && address == WatchedCompanionAddr)
    //    {
    //        RemoveOwnedObject(OwnedObject.Companion, address);
    //        WatchedCompanionAddr = IntPtr.Zero;
    //    }
    //    else
    //    {
    //        RenderedCompanions.Remove(address);
    //        Mediator.Publish(new WatchedObjectDestroyed(address));
    //    }
    //}

    private void AddOwnedObject(OwnedObject kind, nint address)
    {
        Logger.LogDebug($"OwnedObject Rendered: {kind} - {address:X}");
        WatchedTypes.AddOrUpdate(address, kind, (_, __) => kind);
        CurrentOwned.Add(address);
    }

    private void RemoveOwnedObject(OwnedObject kind, nint address)
    {
        Logger.LogDebug($"OwnedObject Removed: {kind} - {address:X}");
        WatchedTypes.TryRemove(address, out _);
        CurrentOwned.Remove(address);
    }

    // Init with original first, than handle so it is present in our other lookups.
    private unsafe void InitializeCharacter(Character* chara)
    {
        try { OnCharaInitializeHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => NewCharacterRendered((GameObject*)chara));
    }

    private unsafe void TerminateCharacter(Character* chara)
    {
        CharacterRemoved((GameObject*)chara);
        try { OnCharaTerminateHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
    }

    private unsafe GameObject* DestroyCharacter(Character* chara, byte freeMemory)
    {
        CharacterRemoved((GameObject*)chara);
        try { return OnCharaDestroyHook!.OriginalDisposeSafe(chara, freeMemory); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); return null; }
    }

    //private void InitializeCompanion(Companion* companion)
    //{
    //    try { OnCompanionInitializeHook!.OriginalDisposeSafe(companion); }
    //    catch (Exception e) { Logger.LogError($"Error: {e}"); }
    //    Svc.Framework.Run(() => NewCompanionRendered(companion));
    //}

    //private void TerminateCompanion(Companion* companion)
    //{
    //    CompanionRemoved(companion);
    //    try { OnCompanionTerminateHook!.OriginalDisposeSafe(companion); }
    //    catch (Exception e) { Logger.LogError($"Error: {e}"); }
    //}

    public unsafe bool TryFindOwnedObjectByIdx(ushort objectIdx, [MaybeNullWhen(false)] out OwnedObject ownedObject)
    {
        unsafe
        {
            if (WatchedPlayerAddr != IntPtr.Zero && ((GameObject*)WatchedPlayerAddr)->ObjectIndex == objectIdx)
            {
                ownedObject = OwnedObject.Player;
                return true;
            }
            else if (WatchedMinionMountAddr != IntPtr.Zero && ((GameObject*)WatchedMinionMountAddr)->ObjectIndex == objectIdx)
            {
                ownedObject = OwnedObject.MinionOrMount;
                return true;
            }
            else if (WatchedPetAddr != IntPtr.Zero && ((GameObject*)WatchedPetAddr)->ObjectIndex == objectIdx)
            {
                ownedObject = OwnedObject.Pet;
                return true;
            }
            else if (WatchedCompanionAddr != IntPtr.Zero && ((GameObject*)WatchedCompanionAddr)->ObjectIndex == objectIdx)
            {
                ownedObject = OwnedObject.Companion;
                return true;
            }
        }
        ownedObject = default;
        return false;
    }

    public async Task WaitForFullyLoadedGameObject(IntPtr address, CancellationToken timeoutToken = default)
    {
        if (address == IntPtr.Zero) throw new ArgumentException("Address cannot be null.", nameof(address));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken, _runtimeCTS.Token);
        while (!cts.IsCancellationRequested)
        {
            // Yes, our clients loading state also impacts anyone else's loading. (that or we are faster than dalamud's object table)
            if (!PlayerData.IsZoning && IsObjectLoaded(address))
                return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     There are conditions where an object can be rendered / created, but not drawable, or currently bring drawn. <para />
    ///     This mainly occurs on login or when transferring between zones, but can also occur during redraws and such.
    ///     We can get around this by checking for various draw conditions.
    /// </summary>
    public unsafe bool IsObjectLoaded(IntPtr gameObjectAddress)
    {
        var gameObj = (GameObject*)gameObjectAddress;
        // Invalid address.
        if (gameObjectAddress == IntPtr.Zero) return false;
        // DrawObject does not exist yet.
        if ((IntPtr)gameObj->DrawObject == IntPtr.Zero) return false;
        // RenderFlags are marked as 'still loading'.
        if (gameObj->RenderFlags == 2048) return false;
        // There are models loaded into slots, still being applied.
        if(((CharacterBase*)gameObj->DrawObject)->HasModelInSlotLoaded != 0) return false;
        // There are model files loaded into slots, still being applied.
        if (((CharacterBase*)gameObj->DrawObject)->HasModelFilesInSlotLoaded != 0) return false;
        // Object is fully loaded.
        return true;
    }
}
