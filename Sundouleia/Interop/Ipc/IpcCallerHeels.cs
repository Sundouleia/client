using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

public sealed class IpcCallerHeels : IIpcCaller
{   
    // Remember, all these are called only when OUR client changes. Not other pairs.
    private readonly ICallGateSubscriber<(int, int)> ApiVersion;

    // API EVENTS.
    public readonly ICallGateSubscriber<string, object?> OnOffsetUpdate;

    // API Getter Functions
    public readonly ICallGateSubscriber<string> GetOffset;

    // API Enactor Functions
    public readonly ICallGateSubscriber<int, string, object?> RegisterPlayer;
    public readonly ICallGateSubscriber<int, object?>         UnregisterPlayer;

    private readonly SundouleiaMediator _mediator;
    public IpcCallerHeels(SundouleiaMediator mediator)
    {
        _mediator = mediator;
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");

        // API Getter
        GetOffset = Svc.PluginInterface.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");

        // API Enactor
        RegisterPlayer = Svc.PluginInterface.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        UnregisterPlayer = Svc.PluginInterface.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");

        // API Events
        OnOffsetUpdate = Svc.PluginInterface.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");

        CheckAPI();

        // Subscribe to events.
        OnOffsetUpdate.Subscribe(ClientOffsetChanged);
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = ApiVersion.InvokeFunc() is { Item1: 2, Item2: >= 1 };
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    {
        OnOffsetUpdate.Unsubscribe(ClientOffsetChanged);
    }

    private void ClientOffsetChanged(string newOffset)
        => _mediator.Publish(new HeelsOffsetChanged());

    /// <returns>
    ///     Gets the heels offset of the client.
    /// </returns>
    public async Task<string> GetClientOffset()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(GetOffset.InvokeFunc).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resets the Heels offset of the provided <paramref name="sundesmo"/>.
    /// </summary>
    public async Task RestoreUserOffset(SundesmoHandler sundesmo)
    {
        if (!APIAvailable || sundesmo.Address == IntPtr.Zero) return;
        await Svc.Framework.RunOnFrameworkThread(() => UnregisterPlayer.InvokeAction(sundesmo.ObjIndex)).ConfigureAwait(false);
    }

    /// <summary>
    ///     Updates the heels offset of the provided <paramref name="sundesmo"/>.
    /// </summary>
    public async Task SetUserOffset(SundesmoHandler sundesmo, string data)
    {
        if (!APIAvailable || sundesmo.Address == IntPtr.Zero) return;
        await Svc.Framework.RunOnFrameworkThread(() => RegisterPlayer.InvokeAction(sundesmo.ObjIndex, data)).ConfigureAwait(false);
    }
}
