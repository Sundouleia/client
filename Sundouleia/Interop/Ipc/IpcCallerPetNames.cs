using CkCommons;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs;
using Sundouleia.Services.Mediator;
using System.Threading.Tasks;

namespace Sundouleia.Interop;

public sealed class IpcCallerPetNames : IIpcCaller
{
    // API Version
    private readonly ICallGateSubscriber<(uint, uint)> ApiVersion;

    // API Events
    private readonly ICallGateSubscriber<object> OnReady;
    private readonly ICallGateSubscriber<object> OnDisposed;
    public readonly ICallGateSubscriber<string, object> OnNicknamesChanged;
    // API Getters
    private readonly ICallGateSubscriber<bool> GetIsEnabled;
    private readonly ICallGateSubscriber<string> GetNicknameData;
    // API Enactors
    private readonly ICallGateSubscriber<string, object> SetNicknameData;
    private readonly ICallGateSubscriber<ushort, object> ClearNicknameData;

    private readonly ILogger<IpcCallerPetNames> _logger;
    private readonly SundouleiaMediator _mediator;
    public IpcCallerPetNames(ILogger<IpcCallerPetNames> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
        // API Version.
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
        // Events
        OnReady = Svc.PluginInterface.GetIpcSubscriber<object>("PetRenamer.OnReady");
        OnDisposed = Svc.PluginInterface.GetIpcSubscriber<object>("PetRenamer.OnDisposing");
        OnNicknamesChanged = Svc.PluginInterface.GetIpcSubscriber<string, object>("PetRenamer.OnPlayerDataChanged");
        // Getters
        GetIsEnabled = Svc.PluginInterface.GetIpcSubscriber<bool>("PetRenamer.IsEnabled");
        GetNicknameData = Svc.PluginInterface.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
        // Enactors
        SetNicknameData = Svc.PluginInterface.GetIpcSubscriber<string, object>("PetRenamer.SetPlayerData");
        ClearNicknameData = Svc.PluginInterface.GetIpcSubscriber<ushort, object>("PetRenamer.ClearPlayerData");

        OnReady.Subscribe(OnIpcReady);
        OnDisposed.Subscribe(OnDispose);
        OnNicknamesChanged.Subscribe(OnNicknamesChange);

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = GetIsEnabled?.InvokeFunc() ?? false;
            if (APIAvailable)
                APIAvailable = ApiVersion?.InvokeFunc() is { Item1: 4, Item2: >= 0 };
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    {
        OnReady.Unsubscribe(OnIpcReady);
        OnDisposed.Unsubscribe(OnDispose);
        OnNicknamesChanged.Unsubscribe(OnNicknamesChange);
    }

    private void OnIpcReady()
    {
        CheckAPI();
        _mediator.Publish(new PetNamesReady());
    }

    private void OnDispose()
    {
        _mediator.Publish(new PetNamesDataChanged(string.Empty));
    }

    // Respective to the Client's PetNames. Does not trigger for other players pets.
    private void OnNicknamesChange(string newData)
    {
        _mediator.Publish(new PetNamesDataChanged(newData));
    }

    // Pet nicknames runs all of their calls on the framework thread,
    // so we can call anywhere and it will work.

    public string GetPetNicknames()
    {
        if (!APIAvailable) return string.Empty;
        return GetNicknameData.InvokeFunc() ?? string.Empty;
    }

    public void SetNamesByIdx(ushort objIdx, string nicknameData)
    {
        if (!APIAvailable) return;
        
        if (!string.IsNullOrEmpty(nicknameData))
            SetNicknameData.InvokeAction(nicknameData);
        else
            ClearNicknameData.InvokeAction(objIdx);
    }

    public async Task ClearPetNamesByIdx(ushort objectIdx)
    {
        if (!APIAvailable) return;
        _logger.LogDebug("Clearing Pet Names for ObjectIdx {ObjectIdx}", objectIdx);
        await Svc.Framework.RunOnFrameworkThread(() => ClearNicknameData.InvokeAction(objectIdx));
    }
}
