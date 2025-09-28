using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary> 
///     ClientState.LocalPlayer no longer allows us to determine attributes 
///     of a player outside the games framework. <para />
///     This service tracks all Client-Owned Object Creation, Destruction, & Notifiers. <para />
///     This allows us to cache an address that we can guarantee will always be the current 
///     valid state without checking every tick. <para />
/// </summary>
public unsafe class OwnedObjectService : DisposableMediatorSubscriberBase
{
    // Private Getters that function outside the framework thread.
    private BattleChara* _playerChara => CharacterManager.Instance()->BattleCharas[0].Value;
    private GameObject* _playerObject => GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
    private BattleChara* _minionOrMountChara => CharacterManager.Instance()->BattleCharas[1].Value;
    private GameObject* _minionOrMountObject => GameObjectManager.Instance()->Objects.IndexSorted[1].Value;
    private IntPtr _playerAddress => (IntPtr)_playerObject;
    private IntPtr _minionOrMountAddress => (IntPtr)_minionOrMountObject;
    private IntPtr _petAddress => (nint)CharacterManager.Instance()->LookupPetByOwnerObject(_playerChara);
    private IntPtr _companionAddress => (nint)CharacterManager.Instance()->LookupBuddyByOwnerObject(_playerChara);

    // Hooks.
    internal Hook<Character.Delegates.OnInitialize> OnCharaInitializeHook;
    internal Hook<Character.Delegates.Dtor> OnCharaDestroyHook;
    internal Hook<Character.Delegates.Terminate> OnCharaTerminateHook;

    internal Hook<Companion.Delegates.OnInitialize> OnCompanionInitializeHook;
    internal Hook<Companion.Delegates.Terminate> OnCompanionTerminateHook;

    public OwnedObjectService(ILogger<AccountService> logger, SundouleiaMediator mediator)
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

    // Public, Accessible, Managed pointer address to Owned Object addresses
    public ConcurrentDictionary<nint, OwnedObject> WatchedTypes { get; private set; } = new();
    public HashSet<nint> WatchedAddresses { get; private set; } = new();
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
            if (obj is null)
                continue;

            if (obj->GetObjectKind() is not ObjectKind.Pc or ObjectKind.BattleNpc)
                continue;

            if (obj->IsCharacter())
                SetCharacterData((Character*)obj);
        }
    }

    // We should not care about the object if it is not one of the objects we are looking for.
    private void SetCharacterData(Character* chara)
    {
        // If the address matches the PlayerObject from the GameObjectManager, set that.
        if ((nint)chara == _playerAddress)
        {
            WatchedTypes.AddOrUpdate((nint)chara, OwnedObject.Player, (_, __) => OwnedObject.Player);
            WatchedAddresses.Add((nint)chara);
            WatchedPlayerAddr = (nint)chara;
            Mediator.Publish(new OwnedObjectCreated(OwnedObject.Player, (nint)chara));
            return;
        }

        // If player addr is null then do not process anything else.
        if (_playerAddress == IntPtr.Zero)
            return;

        // If the character is a mount object, only set if the owner is the player object.
        if ((nint)chara == _minionOrMountAddress && _minionOrMountChara->OwnerId == _playerObject->EntityId)
        {
            WatchedTypes.AddOrUpdate((nint)chara, OwnedObject.MinionOrMount, (_, __) => OwnedObject.MinionOrMount);
            WatchedAddresses.Add((nint)chara);
            WatchedMinionMountAddr = (nint)chara;
            Mediator.Publish(new OwnedObjectCreated(OwnedObject.MinionOrMount, (nint)chara));
            return;
        }
        // If the character is a pet object, only set if the owner is the player object.
        else if ((nint)chara == _petAddress && chara->OwnerId == _playerObject->EntityId)
        {
            WatchedTypes.AddOrUpdate((nint)chara, OwnedObject.Pet, (_, __) => OwnedObject.Pet);
            WatchedAddresses.Add((nint)chara);
            WatchedPetAddr = (nint)chara;
            Mediator.Publish(new OwnedObjectCreated(OwnedObject.Pet, (nint)chara));
            return;
        }
    }

    private void ClearCharacterData(Character* chara)
    {
        // If the address matches the PlayerObject from the GameObjectManager, clear that.
        if ((nint)chara == WatchedPlayerAddr)
        {
            WatchedTypes.TryRemove((nint)chara, out _);
            WatchedAddresses.Remove((nint)chara);
            WatchedPlayerAddr = IntPtr.Zero; 
            Mediator.Publish(new OwnedObjectDestroyed(OwnedObject.Player, (nint)chara));
            return;
        }

        // If player addr is null then do not process anything else.
        if (_playerAddress == IntPtr.Zero)
            return;

        // If the character is a mount object, only clear if the owner is the player object.
        else if ((nint)chara == WatchedMinionMountAddr)
        {
            WatchedTypes.TryRemove((nint)chara, out _);
            WatchedAddresses.Remove((nint)chara);
            WatchedMinionMountAddr = IntPtr.Zero;
            Mediator.Publish(new OwnedObjectDestroyed(OwnedObject.MinionOrMount, (nint)chara));
            return;
        }
        // If the character is a pet object, only clear if the owner is the player object.
        else if ((nint)chara == WatchedPetAddr)
        {
            WatchedTypes.TryRemove((nint)chara, out _);
            WatchedAddresses.Remove((nint)chara);
            WatchedPetAddr = IntPtr.Zero;
            Mediator.Publish(new OwnedObjectDestroyed(OwnedObject.Pet, (nint)chara));
            return;
        }
    }

    private void SetCompanionData(Companion* companion)
    {
        if (_playerAddress == IntPtr.Zero) return;

        // Only set if the CompanionOwnerID == PlayerObject.EntityID
        if (companion->CompanionOwnerId == _playerObject->EntityId)
        {
            WatchedTypes.AddOrUpdate((nint)companion, OwnedObject.Companion, (_, __) => OwnedObject.Companion);
            WatchedAddresses.Add((nint)companion);
            WatchedCompanionAddr = (nint)companion;
            Mediator.Publish(new OwnedObjectCreated(OwnedObject.Companion, (nint)companion));
            return;
        }
    }
    private void ClearCompanionData(Companion* companion)
    {
        if (_playerAddress == IntPtr.Zero) return;

        if ((nint)companion == WatchedCompanionAddr)
        {
            WatchedTypes.TryRemove((nint)companion, out _);
            WatchedAddresses.Remove((nint)companion);
            WatchedCompanionAddr = IntPtr.Zero;
            Mediator.Publish(new OwnedObjectDestroyed(OwnedObject.Companion, (nint)companion));
            return;
        }
    }

    // Init with original first, than handle so it is present in our other lookups.
    private void InitializeCharacter(Character* chara)
    {
        try
        {
            OnCharaInitializeHook!.OriginalDisposeSafe(chara);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error: {e}");
        }
        Svc.Framework.Run(() => SetCharacterData(chara));
    }

    private void TerminateCharacter(Character* chara)
    {
        ClearCharacterData(chara);
        try
        {
            OnCharaTerminateHook!.OriginalDisposeSafe(chara);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error: {e}");
        }
    }

    private GameObject* DestroyCharacter(Character* chara, byte freeMemory)
    {
        ClearCharacterData(chara);
        try
        {
            return OnCharaDestroyHook!.OriginalDisposeSafe(chara, freeMemory);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error: {e}");
            return null;
        }
    }

    private void InitializeCompanion(Companion* companion)
    {
        try
        {
            OnCompanionInitializeHook!.OriginalDisposeSafe(companion);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error: {e}");
        }
        // Check Validity, init, and notify.
        Svc.Framework.Run(() => SetCompanionData(companion));
    }

    private void TerminateCompanion(Companion* companion)
    {
        ClearCompanionData(companion);
        try
        {
            OnCompanionTerminateHook!.OriginalDisposeSafe(companion);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error: {e}");
        }
    }
}
