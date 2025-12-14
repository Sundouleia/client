using CkCommons;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiNotification;
using Glamourer.Api.Enums;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

// Unlike in GSpeak we do not care that much about glamourer updates in detail
// and can run everything through via the base64 strings.
// The main difference is that we are grabbing the data for all player owned objects.

// While we will listen to OnStateChanged, remember that GSpeaks Glamourer detection is flawless
// for almost every edge case if we run into edge cases to handle down the line.
public sealed class IpcCallerGlamourer : IIpcCaller
{
    // value is Cordy's handle WUV = 01010111 01010101 01010110 = 5723478 (hey, don't cringe! I thought it was cute <3) - Nia
    private const uint SUNDOULEIA_LOCK = 0x05723478;

    // API Version
    private readonly ApiVersion ApiVersion;
    // API EVENTS
    public EventSubscriber<nint, StateChangeType> OnStateChanged;   // Informs us when ANY Glamour Change has occurred.
    // API GETTERS
    private readonly GetState       GetState;  // Obtain the JObject of the client's current state.
    private readonly GetStateBase64 GetBase64; // Get the Base64string of the client's current state.
    // API ENACTORS
    private readonly ApplyState      ApplyState;       // Applies actor state with the obtained base64 strings.
    private readonly UnlockState     UnlockUser;       // Unlocks a User's glamour state for modification.
    private readonly UnlockStateName UnlockUserByName; // Unlock a User's glamour state by name. (try to avoid?)
    private readonly RevertState     RevertUser;       // Revert a User to their game state.
    private readonly RevertStateName RevertUserByName; // Revert a sundesmo to their game state by their name. (try to avoid?)

    private readonly ILogger<IpcCallerGlamourer> _logger;
    private readonly SundouleiaMediator _mediator;

    private bool _shownGlamourerUnavailable = false;

    public IpcCallerGlamourer(ILogger<IpcCallerGlamourer> logger, SundouleiaMediator mediator) 
    {
        _logger = logger;
        _mediator = mediator;

        ApiVersion = new ApiVersion(Svc.PluginInterface);

        GetState = new GetState(Svc.PluginInterface);
        GetBase64 = new GetStateBase64(Svc.PluginInterface);

        ApplyState = new ApplyState(Svc.PluginInterface);
        UnlockUser = new UnlockState(Svc.PluginInterface);
        UnlockUserByName = new UnlockStateName(Svc.PluginInterface);
        RevertUser = new RevertState(Svc.PluginInterface);
        RevertUserByName = new RevertStateName(Svc.PluginInterface);

        CheckAPI();
    }

    public void Dispose()
    { }

    public static bool APIAvailable { get; private set; } = false;
    public void CheckAPI()
    {
        var apiAvailable = false; // assume false at first
        Generic.Safe(() =>
        {
            if (ApiVersion.Invoke() is { Major: 1, Minor: >= 3 })
                apiAvailable = true;
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;
        }, true);
        // update available state.
        APIAvailable = apiAvailable;
        if (!apiAvailable && !_shownGlamourerUnavailable)
        {
            _shownGlamourerUnavailable = true;
            _mediator.Publish(new NotificationMessage("Glamourer inactive", "Features Using Glamourer will not function.", NotificationType.Error));
        }
    }

    public async Task<string> GetClientBase64State()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetBase64.Invoke(0).Item2 ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary>
    ///     Obtains the Base64String of the client's current Actor State
    /// </summary>
    public async Task<string?> GetBase64StateByPtr(IntPtr charaAddr)
    {
        if (!APIAvailable || charaAddr == IntPtr.Zero) return null;
        return await Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (Svc.Objects.CreateObjectReference(charaAddr) is { } obj && obj is ICharacter c)
                return GetBase64.Invoke(obj.ObjectIndex).Item2 ?? string.Empty;
            // If fail ret empty.
            return string.Empty;
        }).ConfigureAwait(false);
    }

    public async Task<string> GetBase64StateByObject(IGameObject obj)
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetBase64.Invoke(obj.ObjectIndex).Item2 ?? string.Empty).ConfigureAwait(false);
    }

    public async Task<string> GetBase64StateByIdx(ushort objectIdx)
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetBase64.Invoke(objectIdx).Item2 ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary>
    ///     Applies a base64State string to an actor. <para />
    ///     Try and make this never be a null string if possible.
    /// </summary>
    public async Task ApplyBase64StateByPtr(IntPtr charaAddr, string? actorData)
    {
        if (!APIAvailable || PlayerData.IsZoning || string.IsNullOrEmpty(actorData))
            return;

        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            // Only accept requests to obtain profiles for players.
            if (Svc.Objects.CreateObjectReference(charaAddr) is { } obj && obj is ICharacter)
                ApplyState.Invoke(actorData, obj.ObjectIndex, SUNDOULEIA_LOCK);
        }).ConfigureAwait(false);
    }

    public async Task ApplyBase64StateByIdx(ushort objectIdx, string? actorData)
    {
        // Had IsZoning before, can add back in if needed, but shouldnt be necessary if we know the obj is valid.
        if (!APIAvailable || string.IsNullOrEmpty(actorData)) return;
        await Svc.Framework.RunOnFrameworkThread(() => ApplyState.Invoke(actorData, objectIdx, SUNDOULEIA_LOCK)).ConfigureAwait(false);
    }

    // Require handler to enforce being called by the SundesmoHandler.
    public async Task ReleaseActor(ushort objIdx)
    {
        // Had IsZoning before, can add back in if needed, but shouldnt be nessisary if we know the obj is valid.
        if (!APIAvailable)
            return;
        
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            RevertUser.Invoke(objIdx, SUNDOULEIA_LOCK);
            UnlockUser.Invoke(objIdx, SUNDOULEIA_LOCK);
        }).ConfigureAwait(false);
    }

    public async Task ReleaseByName(string playerName)
    {
        if (!APIAvailable)
            return;

        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            RevertUserByName.Invoke(playerName, SUNDOULEIA_LOCK);
            UnlockUserByName.Invoke(playerName, SUNDOULEIA_LOCK);
        }).ConfigureAwait(false);
    }
}
