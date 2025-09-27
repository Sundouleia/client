using CkCommons;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

/// <summary>
/// The primary manager for all IPC calls.
/// </summary>
public sealed partial class IpcManager : DisposableMediatorSubscriberBase
{
    public IpcCallerCustomize   CustomizePlus { get; }
    public IpcCallerGlamourer   Glamourer { get; }
    public IpcCallerHeels       Heels { get; }
    public IpcCallerHonorific   Honorific { get; }
    public IpcCallerMoodles     Moodles { get; }
    public IpcCallerPenumbra    Penumbra { get; }
    public IpcCallerPetNames    PetNames { get; }

    public IpcManager(ILogger<IpcManager> logger, SundouleiaMediator mediator,
        IpcCallerCustomize customizePlus,
        IpcCallerGlamourer glamourer,
        IpcCallerHeels heels,
        IpcCallerHonorific honorific,
        IpcCallerMoodles moodles,
        IpcCallerPenumbra penumbra,
        IpcCallerPetNames petNames
        ) : base(logger, mediator)
    {
        CustomizePlus = customizePlus;
        Glamourer = glamourer;
        Heels = heels;
        Honorific = honorific;
        Moodles = moodles;
        Penumbra = penumbra;
        PetNames = petNames;

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
        Moodles.CheckAPI();
        Heels.CheckAPI();
        PetNames.CheckAPI();
        Honorific.CheckAPI();
    }
}
