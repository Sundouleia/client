using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Hosting;
using Sundouleia.Services.Mediator;
using Sundouleia.Watchers;

namespace Sundouleia.Interop;

/// <summary>
///     The IPC Provider for Sundouleia to interact with other plugins <para />
///     It is probably best to move Loci to a seperate provider and host two different providers.
/// </summary>
public class IpcProvider : DisposableMediatorSubscriberBase, IHostedService
{
    private const int SundouleiaApiVersion = 1;

    private readonly CharaWatcher _watcher;

    // Current players handled by Sundouleia
    private readonly HashSet<nint> _handledSundesmos = [];

    // Sundouleia's Personal IPC Events.
    private static ICallGateProvider<int>?          ApiVersion;     // Getter (Returns Int)
    private static ICallGateProvider<object>?       Ready;          // Action (Fired by Sundouleia)
    private static ICallGateProvider<object>?       Disposing;      // Action (Fired by Sundouleia)
    private static ICallGateProvider<nint, object>? PairRendered;   // Action (Fired by Sundouleia, provides nint)
    private static ICallGateProvider<nint, object>? PairUnrendered; // Action (Fired by Sundouleia, provides nint)
    private static ICallGateProvider<List<nint>>?   GetAllRendered; // Getter (Returns List<nint>)

    // Validators (Func Getters)
    private ICallGateProvider<string, Task<bool>>? IsFileValid;       // Validates if a SMAD, SMAB, SMAO, SMAI, or SMAIP file is valid.
    private ICallGateProvider<string, Task<bool>>? IsUpdateFileValid; // Validates if an update token is valid for a given SMAD file.

    // Enactors
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmadFile;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmabFile;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaoFile;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaoFiles;
    private ICallGateProvider<string, int, Task<bool>>?       LoadSmaiFile;
    private ICallGateProvider<List<string>, int, Task<bool>>? LoadSmaiFiles;

    public IpcProvider(ILogger<IpcProvider> logger, SundouleiaMediator mediator, CharaWatcher watcher)
        : base(logger, mediator)
    {
        _watcher = watcher;
        // Should subscribe to characterActorCreated or rendered / unrendered events.
        Mediator.Subscribe<SundesmoPlayerRendered>(this, _ =>
        {
            _handledSundesmos.Add(_.Handler.Address);
            PairRendered?.SendMessage(_.Handler.Address);
        });
        Mediator.Subscribe<SundesmoPlayerUnrendered>(this, _ =>
        {
            _handledSundesmos.Remove(_.Address);
            PairUnrendered?.SendMessage(_.Address);
        });

        InitCommon();
        InitSund();
        Logger.LogInformation("Started IpcProviderService");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Ready?.SendMessage();
        }
        catch (Bagagwa ex)
        {
            Logger.LogWarning($"Error During OnReady (Likely another plugin calling a message that no longer exists):\n{ex}");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("Stopping IpcProvider Service");
        Disposing?.SendMessage();
        // Halt the providers
        DeinitCommon();
        DeinitSund();
        return Task.CompletedTask;
    }

    private void InitCommon()
    {
        ApiVersion = Svc.PluginInterface.GetIpcProvider<int>("Sundouleia.GetApiVersion");
        Ready = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Ready");
        Disposing = Svc.PluginInterface.GetIpcProvider<object>("Sundouleia.Disposing");
        PairRendered = Svc.PluginInterface.GetIpcProvider<nint, object>("Sundouleia.PairRendered");
        PairUnrendered = Svc.PluginInterface.GetIpcProvider<nint, object>("Sundouleia.PairUnrendered");
        // Getters
        GetAllRendered = Svc.PluginInterface.GetIpcProvider<List<nint>>("Sundouleia.GetRendered");
        // Configure Funcs and Actions
        ApiVersion.RegisterFunc(() => SundouleiaApiVersion);
        GetAllRendered.RegisterFunc(() => _handledSundesmos.ToList());
    }

    private void DeinitCommon()
    {
        ApiVersion?.UnregisterFunc();
        PairRendered?.UnregisterAction();
        PairUnrendered?.UnregisterAction();
        GetAllRendered?.UnregisterFunc();
    }

    private void InitSund()
    { 
        IsFileValid = Svc.PluginInterface.GetIpcProvider<string, Task<bool>>("Sundouleia.IsFileValid");
        IsUpdateFileValid = Svc.PluginInterface.GetIpcProvider<string, Task<bool>>("Sundouleia.IsUpdateFileValid");
        LoadSmadFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmadFile");
        LoadSmabFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmabFile");
        LoadSmaoFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaoFile");
        LoadSmaoFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaoFiles");
        LoadSmaiFile = Svc.PluginInterface.GetIpcProvider<string, int, Task<bool>>("Sundouleia.LoadSmaiFile");
        LoadSmaiFiles = Svc.PluginInterface.GetIpcProvider<List<string>, int, Task<bool>>("Sundouleia.LoadSmaiFiles");
        // register loaders
        IsFileValid.RegisterFunc(ValidateFile);
        IsUpdateFileValid.RegisterFunc(ValidateUpdateFile);
        LoadSmadFile.RegisterFunc(LoadSMAD);
        LoadSmabFile.RegisterFunc(LoadSMAB);
        LoadSmaoFile.RegisterFunc(LoadSMAO);
        LoadSmaoFiles.RegisterFunc(LoadSMAO);
        LoadSmaiFile.RegisterFunc(LoadSMAI);
        LoadSmaiFiles.RegisterFunc(LoadSMAI);
    }

    private void DeinitSund()
    {
        IsFileValid?.UnregisterFunc();
        IsUpdateFileValid?.UnregisterFunc();
        LoadSmadFile?.UnregisterFunc();
        LoadSmabFile?.UnregisterFunc();
        LoadSmaoFile?.UnregisterFunc();
        LoadSmaoFiles?.UnregisterFunc();
        LoadSmaiFile?.UnregisterFunc();
        LoadSmaiFiles?.UnregisterFunc();
    }

    // Validation.
    private async Task<bool> ValidateFile(string path)
        => await Task.FromResult(false);

    private async Task<bool> ValidateUpdateFile(string path)
        => await Task.FromResult(false);


    /// <summary>
    ///     Loads a SundouleiaModularActorData file onto the given object index. <br />
    ///     <b>Only works in GPOSE</b>
    /// </summary>
    /// <returns> True if loaded successfully, false otherwise. </returns>
    private async Task<bool> LoadSMAD(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAB(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAO(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAO(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAI(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAI(List<string> paths, int objectIdx)
        => await Task.FromResult(false); // Not ready.

    private async Task<bool> LoadSMAIP(string path, int objectIdx)
        => await Task.FromResult(false); // Not ready.
}

