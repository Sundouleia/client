using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;

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


    private readonly ILogger<IpcCallerHeels> _logger;
    public IpcCallerHeels(ILogger<IpcCallerHeels> logger)
    {
        _logger = logger;
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");

        // API Getter
        GetOffset = Svc.PluginInterface.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");

        // API Enactor
        RegisterPlayer = Svc.PluginInterface.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        UnregisterPlayer = Svc.PluginInterface.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");

        // API Events
        OnOffsetUpdate = Svc.PluginInterface.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");

        CheckAPI();
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
    { }

    /// <returns>
    ///     Gets the heels offset of the client.
    /// </returns>
    public async Task<string> GetClientOffset()
    {
        if (!APIAvailable) return string.Empty;
        return await Svc.Framework.RunOnFrameworkThread(GetOffset.InvokeFunc).ConfigureAwait(false);
    }

    /// <summary>
    ///     Updates the heels offset of the provided <paramref name="sundesmo"/>.
    /// </summary>
    public async Task SetUserOffset(PlayerHandler sundesmo, string data)
    {
        if (!APIAvailable) return;
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            _logger.LogDebug($"Setting heels offset for {sundesmo.PlayerName} to {data}");
            RegisterPlayer.InvokeAction(sundesmo.ObjIndex, data);
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resets the Heels offset of the provided <paramref name="sundesmo"/>.
    /// </summary>
    public async Task RestoreUserOffset(PlayerHandler sundesmo)
    {
        if (!APIAvailable || sundesmo.Address == IntPtr.Zero) return;
        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            _logger.LogDebug($"Restoring heels offset for {sundesmo.PlayerName}");
            UnregisterPlayer.InvokeAction(sundesmo.ObjIndex);
        }).ConfigureAwait(false);
    }
}
