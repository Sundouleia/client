using CkCommons;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using Sundouleia.WebAPI;

namespace Sundouleia.Services;

/// <summary> 
///     ClientState.LocalPlayer doesn't allow us to get player data outside the games framework thread. <para />
///     This service tracks all Client-Owned Object Creation, Destruction, & Notifiers. <para />
///     This allows us to cache an address that we can guarantee will always be the current 
///     valid state without checking every tick. <para />
/// </summary>
public unsafe class CharaObjectWatcher : DisposableMediatorSubscriberBase
{
    private readonly SundesmoManager _sundesmos;

    internal Hook<Character.Delegates.OnInitialize> OnCharaInitializeHook;
    internal Hook<Character.Delegates.Dtor> OnCharaDestroyHook;
    internal Hook<Character.Delegates.Terminate> OnCharaTerminateHook;
    internal Hook<Companion.Delegates.OnInitialize> OnCompanionInitializeHook;
    internal Hook<Companion.Delegates.Terminate> OnCompanionTerminateHook;

    public CharaObjectWatcher(ILogger<CharaObjectWatcher> logger, SundouleiaMediator mediator, SundesmoManager sundesmos)
        : base(logger, mediator)
    {
        _sundesmos = sundesmos;

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
    public static HashSet<Character> RenderedCharas { get; private set; } = new();

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
        WatchedTypes.Clear();
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
        var address = (nint)chara;
        // Check for ClientOwned object creation.
        if (address == OwnedObjects.PlayerAddress)
        {
            AddOwnedCharacter(OwnedObject.Player, address);
            WatchedPlayerAddr = address;
            return;
        }
        else if (address == OwnedObjects.MinionOrMountAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId) 
        {
            AddOwnedCharacter(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = address;
            return;
        }
        else if (address == OwnedObjects.PetAddress && chara->OwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            AddOwnedCharacter(OwnedObject.Pet, address);
            WatchedPetAddr = address;
            return;
        }
        // If non-owned object, check our sundesmoManager to see if it matches any created sundesmo.
        RenderedCharas.Add(*chara);
        _sundesmos.CharaEnteredRenderRange(chara);
    }

    private void CharacterRemoved(Character* chara)
    {
        var address = (nint)chara;
        if (address == WatchedPlayerAddr)
        {
            RemoveOwnedCharacter(OwnedObject.Player, address);
            WatchedPlayerAddr = IntPtr.Zero;
            return;
        }
        else if (address == WatchedMinionMountAddr)
        {
            RemoveOwnedCharacter(OwnedObject.MinionOrMount, address);
            WatchedMinionMountAddr = IntPtr.Zero;
            return;
        }
        else if (address == WatchedPetAddr)
        {
            RemoveOwnedCharacter(OwnedObject.Pet, address);
            WatchedPetAddr = IntPtr.Zero;
            return;
        }
        // Otherwise notify sundesmo manager of unrendered chara.
        RenderedCharas.Remove(*chara);
        _sundesmos.CharaLeftRenderRange(chara);
    }

    private void SetCompanionData(Companion* companion)
    {
        if (OwnedObjects.PlayerAddress == IntPtr.Zero) return;
        if (companion->CompanionOwnerId == OwnedObjects.PlayerObject->EntityId)
        {
            WatchedTypes.AddOrUpdate((nint)companion, OwnedObject.Companion, (_, __) => OwnedObject.Companion);
            CurrentOwned.Add((nint)companion);
            WatchedCompanionAddr = (nint)companion;
        }
    }
    private void ClearCompanionData(Companion* companion)
    {
        if (OwnedObjects.PlayerAddress == IntPtr.Zero) return;
        if ((nint)companion == WatchedCompanionAddr)
        {
            WatchedTypes.TryRemove((nint)companion, out _);
            CurrentOwned.Remove((nint)companion);
            WatchedCompanionAddr = IntPtr.Zero;
        }
    }

    private void AddOwnedCharacter(OwnedObject kind, nint address)
    {
        WatchedTypes.AddOrUpdate(address, kind, (_, __) => kind);
        CurrentOwned.Add(address);
        Mediator.Publish(new OwnedCharaCreated(kind, address));
    }

    private void RemoveOwnedCharacter(OwnedObject kind, nint address)
    {
        WatchedTypes.TryRemove(address, out _);
        CurrentOwned.Remove(address);
        Mediator.Publish(new OwnedObjectDestroyed(kind, address));
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
