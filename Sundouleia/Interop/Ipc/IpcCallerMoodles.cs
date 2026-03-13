using Dalamud.Plugin.Ipc;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

/// <summary>
///     Likely to be removed soon, and considered Depricated, but injected for legacy support. <para />
///     Also used as an example for how both can co-exist in a data-sync service without conflict.
///     <b> Moodle data recieved by Sundouleia is parsed to LociData if Loci is in use. </b>
/// </summary>
/// <remarks> All calls here will be void if moodles is disabled. </remarks>
public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly ICallGateSubscriber<int> ApiVersion;

    public readonly ICallGateSubscriber<nint, object> ManagedModified;

    private readonly ICallGateSubscriber<string> GetManager;
    private readonly ICallGateSubscriber<nint, string, object> SetManagerByPtr;
    private readonly ICallGateSubscriber<nint, object> ClearManagerByPtr;

    private readonly SundouleiaMediator _mediator;

    public IpcCallerMoodles(SundouleiaMediator mediator)
    {
        _mediator = mediator;

        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<int>("Moodles.Version");
        ManagedModified = Svc.PluginInterface.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        GetManager = Svc.PluginInterface.GetIpcSubscriber<string>("Moodles.GetClientStatusManagerV2");
        SetManagerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        ClearManagerByPtr = Svc.PluginInterface.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");
        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            var prevRes = APIAvailable;
            APIAvailable = ApiVersion.InvokeFunc() >= 4;
            // Check mediator calls
            if (APIAvailable && !prevRes)
                _mediator.Publish(new MoodlesReady());
            else if (!APIAvailable && prevRes)
                _mediator.Publish(new MoodlesDisposed());
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    { }

    public async Task<string> GetOwnManager()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(() => GetManager.InvokeFunc() ?? string.Empty).ConfigureAwait(false);
    }

    public async Task SetManager(nint address, string dataString)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => SetManagerByPtr.InvokeAction(address, dataString)).ConfigureAwait(false);
    }

    public async Task ClearManager(nint charaAddr)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() => ClearManagerByPtr.InvokeAction(charaAddr)).ConfigureAwait(false);
    }
}