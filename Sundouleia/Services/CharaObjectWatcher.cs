using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI.Utils;

namespace Sundouleia.Services;

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
    ///     Checks against <see cref="RenderedCharas"/> to see if the character is rendered.
    ///     If so, enacts the handler to create the object.
    /// </summary>
    public void CheckForExisting(PlayerHandler handler)
    {
        var sundesmoIdent = handler.Sundesmo.Ident;
        Logger.LogDebug($"Checking against Ident: {sundesmoIdent}");
        foreach (var addr in RenderedCharas)
        {
            var ident = SundouleiaSecurity.GetIdentHashByCharacterPtr(addr);
            if (ident != sundesmoIdent)
                continue;
            Logger.LogDebug($"ContentIdHash Match: {ident} - {((Character*)addr)->NameString}");

            handler.ObjectRendered((Character*)addr);
            break;
        }
    }

    public void CheckForExisting(PlayerOwnedHandler handler)
    {
        // if the owner is not rendered there is no point in checking.
        if (!handler.Sundesmo.PlayerRendered)
            return;

        var addresses = handler.ObjectType is OwnedObject.Companion 
            ? RenderedCompanions : RenderedCharas;
        // check all for parent relation
        var parentId = handler.Sundesmo.PlayerEntityId;
        foreach (var addr in addresses)
        {
            if (((GameObject*)addr)->OwnerId != parentId)
                continue;

            handler.ObjectRendered((GameObject*)addr);
            break;
        }
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
            if (obj->GetObjectKind() is not ObjectKind.Pc or ObjectKind.BattleNpc) continue;
            if (obj->IsCharacter()) NewCharacterRendered((Character*)obj);
        }
    }

    /// <summary>
    ///     Entry point for initialized characters. Should interface with anything 
    ///     wishing to detect created objects. <para />
    ///     Doing so will ensure any final lines are processed prior to the address invalidating.
    /// </summary>
    private void NewCharacterRendered(Character* chara)
    {
        Logger.LogDebug($"New Character Rendered: {(nint)chara:X} - {chara->NameString}");
        var address = (nint)chara;
        if (address == OwnedObjects.PlayerAddress)
        {
            AddOwnedCharacter(OwnedObject.Player, address);
            WatchedPlayerAddr = address;
        }
        else if (address == OwnedObjects.MinionOrMountAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            AddOwnedCharacter(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = address;
        }
        else if (address == OwnedObjects.PetAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            AddOwnedCharacter(OwnedObject.Pet, address);
            WatchedPetAddr = address;
        }
        else
        {
            RenderedCharas.Add(address);
        }
    }

    private void CharacterRemoved(Character* chara)
    {
        var address = (nint)chara;
        if (address == WatchedPlayerAddr)
        {
            RemoveOwnedCharacter(OwnedObject.Player, address);
            WatchedPlayerAddr = IntPtr.Zero;
        }
        else if (address == WatchedMinionMountAddr)
        {
            RemoveOwnedCharacter(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = IntPtr.Zero;
        }
        else if (address == WatchedPetAddr)
        {
            RemoveOwnedCharacter(OwnedObject.Pet, address);
            WatchedPetAddr = IntPtr.Zero;
        }
        else
        {
            RenderedCharas.Remove(address);
        }
    }

    private void SetCompanionData(Companion* companion)
    {
        if (OwnedObjects.PlayerAddress == IntPtr.Zero)
            return;
        var address = (nint)companion;
        if (companion->CompanionOwnerId != OwnedObjects.PlayerObject->EntityId)
            RenderedCompanions.Add(address);
        else
        {
            AddOwnedCharacter(OwnedObject.Companion, address);
            WatchedCompanionAddr = address;
        }
    }
    private void ClearCompanionData(Companion* companion)
    {
        if (OwnedObjects.PlayerAddress == IntPtr.Zero)
            return;
        var address = (nint)companion;
        if (address != WatchedCompanionAddr)
            RenderedCompanions.Remove(address);
        else
        {
            RemoveOwnedCharacter(OwnedObject.Companion, address);
            WatchedCompanionAddr = IntPtr.Zero;
        }
    }

    private void AddOwnedCharacter(OwnedObject kind, nint address)
    {
        WatchedTypes.AddOrUpdate(address, kind, (_, __) => kind);
        CurrentOwned.Add(address);
        Mediator.Publish(new WatchedObjectCreated(kind, address));
    }

    private void RemoveOwnedCharacter(OwnedObject kind, nint address)
    {
        WatchedTypes.TryRemove(address, out _);
        CurrentOwned.Remove(address);
        Mediator.Publish(new WatchedObjectDestroyed(kind, address));
    }

    // Init with original first, than handle so it is present in our other lookups.
    private void InitializeCharacter(Character* chara)
    {
        try { OnCharaInitializeHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => NewCharacterRendered(chara));
    }

    private void TerminateCharacter(Character* chara)
    {
        CharacterRemoved(chara);
        try { OnCharaTerminateHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
    }

    private GameObject* DestroyCharacter(Character* chara, byte freeMemory)
    {
        CharacterRemoved(chara);
        try { return OnCharaDestroyHook!.OriginalDisposeSafe(chara, freeMemory); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); return null; }
    }

    private void InitializeCompanion(Companion* companion)
    {
        try { OnCompanionInitializeHook!.OriginalDisposeSafe(companion); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => SetCompanionData(companion));
    }

    private void TerminateCompanion(Companion* companion)
    {
        ClearCompanionData(companion);
        try { OnCompanionTerminateHook!.OriginalDisposeSafe(companion); }
        catch (Exception e) { Logger.LogError($"Error: {e}"); }
    }
}
