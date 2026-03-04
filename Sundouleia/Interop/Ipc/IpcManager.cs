using CkCommons;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

/// <summary>
/// The primary manager for all IPC calls.
/// </summary>
public sealed partial class IpcManager : DisposableMediatorSubscriberBase
{
    internal const string LOCI_REGISTER_TAG = "Sundouleia";

    public IpcCallerBrio        Brio { get; }
    public IpcCallerCustomize   CustomizePlus { get; }
    public IpcCallerGlamourer   Glamourer { get; }
    public IpcCallerHeels       Heels { get; }
    public IpcCallerHonorific   Honorific { get; }
    public IpcCallerPenumbra    Penumbra { get; }
    public IpcCallerPetNames    PetNames { get; }
    public IpcProviderLoci      Loci { get; }

    public IpcManager(ILogger<IpcManager> logger, SundouleiaMediator mediator,
        IpcCallerBrio brio,
        IpcCallerCustomize customizePlus,
        IpcCallerGlamourer glamourer,
        IpcCallerHeels heels,
        IpcCallerHonorific honorific,
        IpcCallerPenumbra penumbra,
        IpcCallerPetNames petNames,
        IpcProviderLoci lociProvider)
        : base(logger, mediator)
    {
        Brio = brio;
        CustomizePlus = customizePlus;
        Glamourer = glamourer;
        Heels = heels;
        Honorific = honorific;
        Penumbra = penumbra;
        PetNames = petNames;
        Loci = lociProvider;

        if (Initialized)
            Mediator.Publish(new PenumbraInitialized());

        // subscribe to the delayed framework update message, which will call upon the periodic API state check.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => PeriodicApiStateCheck());

        Generic.Safe(PeriodicApiStateCheck);
    }

    public static bool Initialized => IpcCallerPenumbra.APIAvailable && IpcCallerGlamourer.APIAvailable;

    private void PeriodicApiStateCheck()
    {
        Penumbra.CheckAPI();
        Penumbra.CheckModDirectory();
        Glamourer.CheckAPI();
        CustomizePlus.CheckAPI();
        Heels.CheckAPI();
        PetNames.CheckAPI();
        Honorific.CheckAPI();
        Brio.CheckAPI();
    }

    #region Loci-Async Calls
    public async Task<string> LociGetOwnManager()
    {
        return await Svc.Framework.RunOnFrameworkThread(Loci.GetClientSM).ConfigureAwait(false);
    }

    public async Task<bool> LociRegister(nint addr)
    {
        return await Svc.Framework.RunOnFrameworkThread(() => Loci.RegisterByPtr(addr, LOCI_REGISTER_TAG)).ConfigureAwait(false);
    }

    public async Task LociRelease(nint addr)
    { 
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            Loci.UnregisterByPtr(addr, LOCI_REGISTER_TAG);
            Loci.ClearSMByPtr(addr);
        }).ConfigureAwait(false);
    }

    public async Task LociReleaseByName(string nameWorld)
    {
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            Loci.UnregisterByName(nameWorld, LOCI_REGISTER_TAG);
            Loci.ClearSMByName(nameWorld);
        }).ConfigureAwait(false);
    }

    public async Task LociApplyStatuses(List<Guid> ids)
    {
        await Svc.Framework.RunOnFrameworkThread(() => Loci.ApplyBulkStatuses(ids)).ConfigureAwait(false);
    }
    public async Task LociApplyStatusInfos(List<LociStatusInfo> tuples)
    {
        await Svc.Framework.RunOnFrameworkThread(() => Loci.ApplyBulkStatusInfos(tuples)).ConfigureAwait(false);
    }
    public async Task LociRemoveStatuses(List<Guid> ids)
    {
        await Svc.Framework.RunOnFrameworkThread(() => Loci.RemoveBulkStatuses(ids)).ConfigureAwait(false);
    }
    public async Task LociSetByPtr(nint addr, string data)
    {
        await Svc.Framework.RunOnFrameworkThread(() => Loci.SetSMByPtr(addr, data)).ConfigureAwait(false);
    }
    public async Task LociClearByPtr(nint addr)
    {
        await Svc.Framework.RunOnFrameworkThread(() => Loci.ClearSMByPtr(addr)).ConfigureAwait(false);
    }

    #endregion Loci-Async Calls
}
