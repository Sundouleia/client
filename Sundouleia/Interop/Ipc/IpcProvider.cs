using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Mediator;
using Microsoft.Extensions.Hosting;

namespace Sundouleia.Interop;

/// <summary>
/// The IPC Provider for Sundouleia to interact with other plugins by sharing information about visible players.
/// </summary>
public class IpcProvider : IHostedService
{
    private const int SundouleiaApiVersion = 1;

    private readonly ILogger<IpcProvider> _logger;
    private readonly SundesmoManager _sundesmoManager;
    private readonly List<nint> _handledPlayers = [];

    // Sundouleia's Personal IPC Events.
    private static ICallGateProvider<int>?    ApiVersion; // FUNC 
    private static ICallGateProvider<object>? ListUpdated; // ACTION
    private static ICallGateProvider<object>? Ready; // FUNC
    private static ICallGateProvider<object>? Disposing; // FUNC

    // IPC Getters
    private ICallGateProvider<List<nint>>? HandledPlayers; // Consumer GETTER


    public IpcProvider(ILogger<IpcProvider> logger, SundesmoManager pairs)
    {
        _logger = logger;
        _sundesmoManager = pairs;

        // Could add methods here for NoLongerVisible, Visible, DataAttached, DataUnattached.
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IpcProviderService");
        // init API
        ApiVersion = Svc.PluginInterface.GetIpcProvider<int>("Sundouleia.GetApiVersion");
        // init Events
        Ready = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Ready");
        Disposing = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Disposing");
        // init Getters
        HandledPlayers = Svc.PluginInterface.GetIpcProvider<List<nint>>("Sundouleia.GetHandledPlayers");
        // init appliers
        ListUpdated = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.VisiblePlayersUpdated");

        // register api
        ApiVersion.RegisterFunc(() => SundouleiaApiVersion);
        // register getters
        HandledPlayers.RegisterFunc(GetHandledPlayers);

        _logger.LogInformation("Started IpcProviderService");
        NotifyReady();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        NotifyDisposing();

        ApiVersion?.UnregisterFunc();
        Ready?.UnregisterFunc();
        Disposing?.UnregisterFunc();

        HandledPlayers?.UnregisterFunc();
        ListUpdated?.UnregisterAction();
        return Task.CompletedTask;
    }

    private static void NotifyReady() => Ready?.SendMessage();
    private static void NotifyDisposing() => Disposing?.SendMessage();
    private static void NotifyListChanged() => ListUpdated?.SendMessage();

    private List<nint> GetHandledPlayers()
        => _handledPlayers.Where(g => g  != nint.Zero).Distinct().ToList();
}

