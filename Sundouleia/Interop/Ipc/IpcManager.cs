using CkCommons;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

/// <summary>
/// The primary manager for all IPC calls.
/// </summary>
public sealed partial class IpcManager : DisposableMediatorSubscriberBase
{
    internal const string LOCI_REGISTER_TAG = "Sundouleia";

    public IpcCallerBrio        Brio        { get; }
    public IpcCallerCustomize   CPlus       { get; }
    public IpcCallerGlamourer   Glamourer   { get; }
    public IpcCallerHeels       Heels       { get; }
    public IpcCallerHonorific   Honorific   { get; }
    public IpcCallerLoci        Loci        { get; }
    public IpcCallerMoodles     Moodles     { get; }
    public IpcCallerPenumbra    Penumbra    { get; }
    public IpcCallerPetNames    PetNames    { get; }

    public IpcManager(ILogger<IpcManager> logger, SundouleiaMediator mediator,
        IpcCallerBrio brio,
        IpcCallerCustomize customizePlus,
        IpcCallerGlamourer glamourer,
        IpcCallerHeels heels,
        IpcCallerHonorific honorific,
        IpcCallerLoci loci,
        IpcCallerMoodles moodles,
        IpcCallerPenumbra penumbra,
        IpcCallerPetNames petNames)
        : base(logger, mediator)
    {
        Brio = brio;
        CPlus = customizePlus;
        Glamourer = glamourer;
        Heels = heels;
        Honorific = honorific;
        Loci = loci;
        Moodles = moodles;
        Penumbra = penumbra;
        PetNames = petNames;

        if (Initialized)
            Mediator.Publish(new PenumbraInitialized());

        // subscribe to the delayed framework update message, which will call upon the periodic API state check.
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, _ => PeriodicApiStateCheck());

        Generic.Safe(PeriodicApiStateCheck);
    }

    public static bool Initialized => IpcCallerPenumbra.APIAvailable && IpcCallerGlamourer.APIAvailable;

    private void PeriodicApiStateCheck()
    {
        Penumbra.CheckAPI();
        Penumbra.CheckModDirectory();
        Glamourer.CheckAPI();
        CPlus.CheckAPI();
        Heels.CheckAPI();
        Honorific.CheckAPI();
        Loci.CheckAPI();
        Moodles.CheckAPI();
        PetNames.CheckAPI();
        Brio.CheckAPI();
    }
}
