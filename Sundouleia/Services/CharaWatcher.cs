using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using static Sundouleia.DrawSystem.SorterExtensions;

namespace Sundouleia.Watchers;

/// <summary> 
///     ClientState.LocalPlayer doesn't allow us to get player data outside the games framework thread. <para />
///     This service tracks all Client-Owned Object Creation, Destruction, & Notifiers. <para />
///     This allows us to cache an address that we can guarantee will always be the current 
///     valid state without checking every tick. <para />
/// </summary>
public unsafe class CharaWatcher : IHostedService
{
    internal static readonly HashSet<ObjectKind> ValidKinds = [ ObjectKind.Pc, ObjectKind.BattleNpc, ObjectKind.Companion, ObjectKind.Mount, ObjectKind.Ornament ];

    internal Hook<Character.Delegates.OnInitialize> OnCharaInitializeHook;
    internal Hook<Character.Delegates.Dtor> OnCharaDestroyHook;
    internal Hook<Character.Delegates.Terminate> OnCharaTerminateHook;

    private readonly ILogger<CharaWatcher> _logger;
    private readonly SundouleiaMediator _mediator;

    private readonly CancellationTokenSource _runtimeCTS = new();

    public unsafe CharaWatcher(ILogger<CharaWatcher> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;

        OnCharaInitializeHook = Svc.Hook.HookFromAddress<Character.Delegates.OnInitialize>((nint)Character.StaticVirtualTablePointer->OnInitialize, InitializeCharacter);
        OnCharaTerminateHook = Svc.Hook.HookFromAddress<Character.Delegates.Terminate>((nint)Character.StaticVirtualTablePointer->Terminate, TerminateCharacter);
        OnCharaDestroyHook = Svc.Hook.HookFromAddress<Character.Delegates.Dtor>((nint)Character.StaticVirtualTablePointer->Dtor, DestroyCharacter);
        
        OnCharaInitializeHook.SafeEnable();
        OnCharaTerminateHook.SafeEnable();
        OnCharaDestroyHook.SafeEnable();
    }

    // A persistent static cache holding all rendered Character pointers.
    public static HashSet<nint> RenderedCharas { get; private set; } = new();
    public static HashSet<nint> RenderedCompanions { get; private set; } = new(); // Unused ATM
    public static HashSet<nint> GPoseActors { get; private set; } = new();

    // Public, Accessible, Managed pointer address to Owned Object addresses
    // (Revise this maybe. I dont really like it.)
    public ConcurrentDictionary<nint, OwnedObject> WatchedTypes { get; private set; } = new();
    public HashSet<nint> CurrentOwned { get; private set; } = new();
    public nint WatchedPlayerAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedMinionMountAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedPetAddr { get; private set; } = IntPtr.Zero;
    public nint WatchedCompanionAddr { get; private set; } = IntPtr.Zero;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Collect data from existing objects
        CollectInitialData();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancel, but do not dispose, the token.
        _runtimeCTS.SafeCancel();

        OnCharaInitializeHook?.Dispose();
        OnCharaTerminateHook?.Dispose();
        OnCharaDestroyHook?.Dispose();

        RenderedCharas.Clear();
        RenderedCompanions.Clear();
        WatchedTypes.Clear();
        return Task.CompletedTask;
    }

    public static bool TryGetFirst(Func<Character, bool> predicate, [NotNullWhen(true)] out nint charaAddr)
    {
        foreach (Character* addr in RenderedCharas)
        {
            if (predicate(*addr))
            {
                charaAddr = (nint)addr;
                return true;
            }
        }
        charaAddr = nint.Zero;
        return false;
    }

    public static unsafe bool TryGetFirstUnsafe(Func<Character, bool> predicate, [NotNullWhen(true)] out Character* character)
    {
        foreach (Character* addr in RenderedCharas)
        {
            if (predicate(*addr))
            {
                character = addr;
                return true;
            }
        }
        character = null;
        return false;
    }

    /// <summary>
    ///     Obtain a Character* if rendered, returning false otherwise.
    /// </summary>
    public static unsafe bool TryGetValue(nint address, [NotNullWhen(true)] out Character* character)
    {
        if (RenderedCharas.Contains(address))
        {
            character = (Character*)address;
            return true;
        }
        character = null;
        return false;
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
            // Only process characters.
            if (!obj->IsCharacter()) continue;

            Character* chara = (Character*)obj;
            // this is confusing because sometimes these can be either?
            if (ValidKinds.Contains(chara->GetObjectKind()))
                NewCharacterRendered(chara);
        }

        // If in GPose, collect all GPose actors.
        if (GameMain.IsInGPose())
        {
            for (var i = 201; i < objManager->Objects.IndexSorted.Length; i++)
            {
                GameObject* obj = objManager->Objects.IndexSorted[i];
                if (obj is null) continue;
                // Only process characters.
                if (!obj->IsCharacter()) continue;

                Character* chara = (Character*)obj;
                // Look into further maybe, idk.
                if (ValidKinds.Contains(chara->GetObjectKind()))
                    NewCharacterRendered(chara);
            }
        }
    }

    /// <summary>
    ///     Entry point for initialized characters. Should interface with anything 
    ///     wishing to detect created objects. <para />
    ///     Doing so will ensure any final lines are processed prior to the address invalidating.
    /// </summary>
    private unsafe void NewCharacterRendered(Character* chara)
    {
        var address = (nint)chara;
        // Do not track if not a valid object type. (Maybe move to after gpose actor adding)
        if (!ValidKinds.Contains(chara->GetObjectKind()))
            return;
        // For GPose actors.
        if (TryAddGPoseActor(chara))
            return;

        // Other Actors
        _logger.LogDebug($"New Character Rendered: {(nint)chara:X} - {chara->GetName()}", LoggerType.OwnedObjects);
        MaybeAddOwnedObject(chara);
        RenderedCharas.Add(address);
        _mediator.Publish(new WatchedObjectCreated(address));
    }

    private unsafe void CharacterRemoved(Character* chara)
    {
        var address = (nint)chara;
        // For GPose actors.
        if (TryRemoveGPoseActor(chara))
            return;

        // Other Actors
        MaybeRemOwnedObject(chara);
        _logger.LogDebug($"Character Removed: {(nint)chara:X} - {chara->GetName()}", LoggerType.OwnedObjects);
        RenderedCharas.Remove(address);
        _mediator.Publish(new WatchedObjectDestroyed(address));
    }

    #region Add Helpers
    private unsafe bool TryAddGPoseActor(Character* chara)
    {
        if (chara->ObjectIndex <= 200 || !GameMain.IsInGPose())
            return false;
        if (!GPoseActors.Add((nint)chara))
            return false;
        _logger.LogDebug($"GPose Chara Rendered: {(nint)chara:X} - {chara->NameString}", LoggerType.OwnedObjects);
        _mediator.Publish(new GPoseObjectCreated((nint)chara));
        return true;
    }

    private unsafe void MaybeAddOwnedObject(Character* chara)
    {
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

        void AddOwnedObject(OwnedObject kind, nint address)
        {
            if (WatchedTypes.TryAdd(address, kind))
            {
                CurrentOwned.Add(address);
                _mediator.Publish(new OwnedObjectCreated(kind, address));
            }
        }
    }
    #endregion Add Helpers

    #region Remove Helpers
    private unsafe bool TryRemoveGPoseActor(Character* chara)
    {
        if (!GPoseActors.Remove((nint)chara))
            return false;
        _logger.LogDebug($"GPose Chara Removed: {(nint)chara:X} - {chara->NameString}", LoggerType.OwnedObjects);
        _mediator.Publish(new GPoseObjectDestroyed((nint)chara, *chara));
        return true;
    }

    private unsafe void MaybeRemOwnedObject(Character* chara)
    {
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

        // Could lead to some WatchedXAddr being out of state?
        void RemoveOwnedObject(OwnedObject kind, nint address)
        {
            if (WatchedTypes.TryRemove(address, out _))
            {
                CurrentOwned.Remove(address);
                _mediator.Publish(new OwnedObjectDestroyed(kind, address));
            }
        }
    }

    #endregion Remove Helpers
    // Init with original first, than handle so it is present in our other lookups.
    private unsafe void InitializeCharacter(Character* chara)
    {
        try { OnCharaInitializeHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { _logger.LogError($"Error: {e}"); }
        Svc.Framework.Run(() => NewCharacterRendered(chara));
    }

    private unsafe void TerminateCharacter(Character* chara)
    {
        CharacterRemoved(chara);
        try { OnCharaTerminateHook!.OriginalDisposeSafe(chara); }
        catch (Exception e) { _logger.LogError($"Error: {e}"); }
    }

    private unsafe GameObject* DestroyCharacter(Character* chara, byte freeMemory)
    {
        CharacterRemoved(chara);
        try { return OnCharaDestroyHook!.OriginalDisposeSafe(chara, freeMemory); }
        catch (Exception e) { _logger.LogError($"Error: {e}"); return null; }
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
}
