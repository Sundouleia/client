using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Utils;

namespace Sundouleia.Watchers;

/// <summary> 
///     ClientState.LocalPlayer doesn't allow us to get player data outside the games framework thread. <para />
///     This service tracks all Client-Owned Object Creation, Destruction, & Notifiers. <para />
///     This allows us to cache an address that we can guarantee will always be the current 
///     valid state without checking every tick. <para />
/// </summary>
public unsafe class CharaObjectWatcher : DisposableMediatorSubscriberBase
{
    internal Hook<Character.Delegates.OnInitialize> OnCharaInitializeHook;
    internal Hook<Character.Delegates.Dtor> OnCharaDestroyHook;
    internal Hook<Character.Delegates.Terminate> OnCharaTerminateHook;
    internal Hook<Companion.Delegates.OnInitialize> OnCompanionInitializeHook;
    internal Hook<Companion.Delegates.Terminate> OnCompanionTerminateHook;

    public CharaObjectWatcher(ILogger<CharaObjectWatcher> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
        OnCharaInitializeHook = Svc.Hook.HookFromAddress<Character.Delegates.OnInitialize>((nint)Character.StaticVirtualTablePointer->OnInitialize, InitializeCharacter);
        OnCharaTerminateHook = Svc.Hook.HookFromAddress<Character.Delegates.Terminate>((nint)Character.StaticVirtualTablePointer->Terminate, TerminateCharacter);
        OnCharaDestroyHook = Svc.Hook.HookFromAddress<Character.Delegates.Dtor>((nint)Character.StaticVirtualTablePointer->Dtor, DestroyCharacter);
        OnCompanionInitializeHook = Svc.Hook.HookFromAddress<Companion.Delegates.OnInitialize>((nint)Companion.StaticVirtualTablePointer->OnInitialize, InitializeCompanion);
        OnCompanionTerminateHook = Svc.Hook.HookFromAddress<Companion.Delegates.Terminate>((nint)Companion.StaticVirtualTablePointer->Terminate, TerminateCompanion);

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

        if (handler.IsRendered)
        {
            address = handler.Address;
            return true;
        }

        // Fail if the address already exists.
        if (handler.Address != IntPtr.Zero)
            return false;

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
        var known = handler.ObjectType is OwnedObject.Companion ? RenderedCompanions : RenderedCharas;
        var parentId = handler.Sundesmo.PlayerEntityId;

        // Check against all known addresses.
        foreach (var addr in known)
        {
            if (((GameObject*)addr)->OwnerId != parentId)
                continue;

            address = addr;
            return true;
        }
        return false;
    }

    public nint FromOwned(OwnedObject kind)
        => kind switch
        {
            OwnedObject.Player       => WatchedPlayerAddr,
            OwnedObject.MinionOrMount=> WatchedMinionMountAddr,
            OwnedObject.Pet          => WatchedPetAddr,
            OwnedObject.Companion    => WatchedCompanionAddr,
            _                        => IntPtr.Zero,
        };

    private void CollectInitialData()
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
    private void NewCharacterRendered(GameObject* chara)
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

    private void CharacterRemoved(GameObject* chara)
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
    private void NewCompanionRendered(Companion* companion)
    {
        //Logger.LogDebug($"New Companion Rendered: {(nint)companion:X} - {companion->NameString}");
        var address = (nint)companion;
        //if (OwnedObjects.PlayerAddress != IntPtr.Zero && companion->ObjectKind == ObjectKind.BattleNpc && companion->OwnerId == OwnedObjects.PlayerObject->EntityId)
        //{
        //    AddOwnedObject(OwnedObject.Companion, address);
        //    WatchedCompanionAddr = address;
        //}
        // if it doesn't exist already
        RenderedCompanions.Add(address);
        // Mediator.Publish(new WatchedObjectCreated(address));
    }
    // Best to leave this for logging mostly, and debug why we should include later.
    // Causes a lot of duplicate additions as they can be called at the same time.
    private void CompanionRemoved(Companion* companion)
    {
        //Logger.LogDebug($"Companion Removed: {(nint)companion:X} - {companion->NameString}");
        var address = (nint)companion;
        //if (OwnedObjects.PlayerAddress != IntPtr.Zero && address == WatchedCompanionAddr)
        //{
        //    RemoveOwnedObject(OwnedObject.Companion, address);
        //    WatchedCompanionAddr = IntPtr.Zero;
        //}
        //else
        //{
        RenderedCompanions.Remove(address);
        // Mediator.Publish(new WatchedObjectDestroyed(address));
        //}
    }

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
    private void InitializeCharacter(Character* chara)
    {
        try { OnCharaInitializeHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => NewCharacterRendered((GameObject*)chara));
    }

    private void TerminateCharacter(Character* chara)
    {
        CharacterRemoved((GameObject*)chara);
        try { OnCharaTerminateHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
    }

    private GameObject* DestroyCharacter(Character* chara, byte freeMemory)
    {
        CharacterRemoved((GameObject*)chara);
        try { return OnCharaDestroyHook!.OriginalDisposeSafe(chara, freeMemory); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); return null; }
    }

    private void InitializeCompanion(Companion* companion)
    {
        try { OnCompanionInitializeHook!.OriginalDisposeSafe(companion); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => NewCompanionRendered(companion));
    }

    private void TerminateCompanion(Companion* companion)
    {
        CompanionRemoved(companion);
        try { OnCompanionTerminateHook!.OriginalDisposeSafe(companion); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
    }
}
