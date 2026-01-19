using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Interop.Windows;

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

    private readonly CancellationTokenSource _runtimeCTS = new();

    public unsafe CharaObjectWatcher(ILogger<CharaObjectWatcher> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
        OnCharaInitializeHook = Svc.Hook.HookFromAddress<Character.Delegates.OnInitialize>((nint)Character.StaticVirtualTablePointer->OnInitialize, InitializeCharacter);
        OnCharaTerminateHook = Svc.Hook.HookFromAddress<Character.Delegates.Terminate>((nint)Character.StaticVirtualTablePointer->Terminate, TerminateCharacter);
        OnCharaDestroyHook = Svc.Hook.HookFromAddress<Character.Delegates.Dtor>((nint)Character.StaticVirtualTablePointer->Dtor, DestroyCharacter);

        OnCharaInitializeHook.SafeEnable();
        OnCharaTerminateHook.SafeEnable();
        OnCharaDestroyHook.SafeEnable();

        // Collect data from existing objects
        CollectInitialData();
    }

    // A persistent static cache holding all rendered Character pointers.
    public static HashSet<nint> RenderedCharas { get; private set; } = new();
    public static HashSet<nint> RenderedCompanions { get; private set; } = new();
    public static HashSet<nint> GPoseActors { get; private set; } = new();

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
        // Standard Actor Handling.
        for (var i = 0; i < 200; i++)
        {
            GameObject* obj = objManager->Objects.IndexSorted[i];
            if (obj is null) continue;
            // this is confusing because sometimes these can be either?
            if (obj->GetObjectKind() is (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion or ObjectKind.Mount))
                NewCharacterRendered(obj);
        }

        // If in GPose, collect all GPose actors.
        if (GameMain.IsInGPose())
        {
            for (var i = 201; i < objManager->Objects.IndexSorted.Length; i++)
            {
                GameObject* obj = objManager->Objects.IndexSorted[i];
                if (obj is null) continue;
                // Look into further maybe, idk.
                if (obj->GetObjectKind() is (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion or ObjectKind.Mount))
                    NewCharacterRendered(obj);
            }
        }
    }

    /// <summary>
    ///     Entry point for initialized characters. Should interface with anything 
    ///     wishing to detect created objects. <para />
    ///     Doing so will ensure any final lines are processed prior to the address invalidating.
    /// </summary>
    private unsafe void NewCharacterRendered(GameObject* chara)
    {
        var address = (nint)chara;

        // Do not track if not a valid object type. (Maybe move to after gpose actor adding)
        if (chara->GetObjectKind() is not (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion or ObjectKind.Mount))
            return;
        
        // For GPose actors.
        if (chara->ObjectIndex > 200 && GameMain.IsInGPose())
        {
            Logger.LogDebug($"New GPose Character Rendered: {(nint)chara:X} - {chara->NameString}", LoggerType.OwnedObjects);
            GPoseActors.Add(address);
            Mediator.Publish(new GPoseObjectCreated(address));
            return;
        }

        // Other Actors.
        Logger.LogDebug($"New Character Rendered: {(nint)chara:X} - {chara->GetName()}", LoggerType.OwnedObjects);
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
        // Otherwise, it is a non-client owned object.
        else
        {
            RenderedCharas.Add(address);
            Mediator.Publish(new WatchedObjectCreated(address));
        }
    }

    private unsafe void CharacterRemoved(GameObject* chara)
    {
        var address = (nint)chara;

        // For GPose actors.
        if (GPoseActors.Remove(address))
        {
            Logger.LogDebug($"GPose Character Removed: {(nint)chara:X} - {chara->NameString}", LoggerType.OwnedObjects);
            // Include a snapshot of the data at time of destruction so we can properly get data
            // such as the namestring after destruction.
            Mediator.Publish(new GPoseObjectDestroyed(address, *chara));
            return;
        }

        // Other Actors.
        Logger.LogDebug($"Character Removed: {(nint)chara:X} - {chara->GetName()}", LoggerType.OwnedObjects);
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
        // For other actors.
        else
        {
            RenderedCharas.Remove(address);
            Mediator.Publish(new WatchedObjectDestroyed(address));
        }
    }

    private void AddOwnedObject(OwnedObject kind, nint address)
    {
        Logger.LogDebug($"OwnedObject Rendered: {kind} - {address:X}", LoggerType.OwnedObjects);
        if (WatchedTypes.TryAdd(address, kind))
        {
            CurrentOwned.Add(address);
            Mediator.Publish(new OwnedObjectCreated(kind, address));
        }
    }

    private void RemoveOwnedObject(OwnedObject kind, nint address)
    {
        Logger.LogDebug($"OwnedObject Removed: {kind} - {address:X}", LoggerType.OwnedObjects);
        if (WatchedTypes.TryRemove(address, out _))
        {
            CurrentOwned.Remove(address);
            Mediator.Publish(new OwnedObjectDestroyed(kind, address));
        }
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

    public unsafe bool TryFindOwnedObjectByIdx(ushort objectIdx, [MaybeNullWhen(false)] out OwnedObject ownedObject)
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
        if ((ulong)gameObj->RenderFlags == 2048) return false;
        // There are models loaded into slots, still being applied.
        if(((CharacterBase*)gameObj->DrawObject)->HasModelInSlotLoaded != 0) return false;
        // There are model files loaded into slots, still being applied.
        if (((CharacterBase*)gameObj->DrawObject)->HasModelFilesInSlotLoaded != 0) return false;
        // Object is fully loaded.
        return true;
    }
}
