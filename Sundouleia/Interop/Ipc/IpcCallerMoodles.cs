using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly ICallGateSubscriber<int> ApiVersion;

    public readonly ICallGateSubscriber<IPlayerCharacter, object> OnStatusModified;

    // API Getters
    private readonly ICallGateSubscriber<string>       GetOwnStatus;
    private readonly ICallGateSubscriber<nint, string> GetStatusByPtr;

    // API Enactors
    private readonly ICallGateSubscriber<nint, string, object>  SetStatusByPtr;
    private readonly ICallGateSubscriber<nint, object>          ClearStatusByPtr;

    private readonly SundouleiaMediator _mediator;

    public IpcCallerMoodles(SundouleiaMediator mediator)
    {
        _mediator = mediator;

        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<int>("Moodles.Version");

        // API Getters
        GetOwnStatus = Svc.PluginInterface.GetIpcSubscriber<string>("Moodles.GetClientStatusManagerV2");
        GetStatusByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");

        // API Enactors
        SetStatusByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        ClearStatusByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");

        // API Action Events:
        OnStatusModified = Svc.PluginInterface.GetIpcSubscriber<IPlayerCharacter, object>("Moodles.StatusManagerModified");

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            var result = ApiVersion.InvokeFunc() >= 3;
            if(!APIAvailable && result)
                _mediator.Publish(new MoodlesReady());
            APIAvailable = result;
        }
        catch
        {
            // Moodles was not ready yet / went offline. Set back to false. (Statuses are auto-cleared by moodles)
            APIAvailable = false;
        }
    }

    public void Dispose()
    {
        OnStatusModified.Unsubscribe(StatusManagerModified);
    }

    private void StatusManagerModified(IPlayerCharacter player)
        => _mediator.Publish(new MoodlesChanged(player.Address));

    /// <summary> 
    ///     Gets the ClientPlayer's StatusManager string.
    /// </summary>
    public async Task<string> GetOwn()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetOwnStatus.InvokeFunc() ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary> 
    ///     Gets the StatusManager by pointer.
    /// </summary>
    public async Task<string?> GetByPtr(nint charaAddr)
    {
        if (!APIAvailable) return null;
        return await Svc.Framework.RunOnFrameworkThread(() => GetStatusByPtr.InvokeFunc(charaAddr)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Sets the StatusManager by pointer.
    /// </summary>
    public async Task SetByPtr(nint charaAddr, string statusString)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => SetStatusByPtr.InvokeAction(charaAddr, statusString)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Clears a players StatusManager by pointer.
    /// </summary>
    public async Task ClearByPtr(nint charaAddr)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearStatusByPtr.InvokeAction(charaAddr)).ConfigureAwait(false);
    }
}
