using CkCommons;
using Dalamud.Plugin.Ipc;
using Sundouleia.Pairs.Handlers;
using Sundouleia.Services.Mediator;

namespace Sundouleia.Interop;

public sealed class IpcCallerHonorific : IIpcCaller
{
    // API Calls
    private readonly ICallGateSubscriber<(uint, uint)> ApiVersion;
    // API Events
    private readonly ICallGateSubscriber<object> Ready;
    private readonly ICallGateSubscriber<object> Disposing;
    public readonly ICallGateSubscriber<string, object> OnTitleChange; // When the client changed their honorific title.
    // API Getters
    private readonly ICallGateSubscriber<string> GetClientTitle;
    // API Enactors
    private readonly ICallGateSubscriber<int, string, object> SetUserTitle;
    private readonly ICallGateSubscriber<int, object> ClearUserTitle;

    private readonly ILogger<IpcCallerHonorific> _logger;
    private readonly SundouleiaMediator _mediator;

    public IpcCallerHonorific(ILogger<IpcCallerHonorific> logger, SundouleiaMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
        // API Version.
        ApiVersion = Svc.PluginInterface.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        // Events
        Ready = Svc.PluginInterface.GetIpcSubscriber<object>("Honorific.Ready");
        Disposing = Svc.PluginInterface.GetIpcSubscriber<object>("Honorific.Disposing");
        OnTitleChange = Svc.PluginInterface.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
        // Getters
        GetClientTitle = Svc.PluginInterface.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
        // Enactors
        SetUserTitle = Svc.PluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        ClearUserTitle = Svc.PluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");

        Ready.Subscribe(OnHonorificReady);
        Disposing.Subscribe(OnHonorificDisposing);

        CheckAPI();
    }

    public static bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        try
        {
            APIAvailable = ApiVersion.InvokeFunc() is { Item1: 3, Item2: >= 1 };
        }
        catch
        {
            APIAvailable = false;
        }
    }

    public void Dispose()
    {
        OnTitleChange.Unsubscribe(OnTitleChanged);
        Ready.Unsubscribe(OnHonorificReady);
        Disposing.Unsubscribe(OnHonorificDisposing);
    }
    private void OnHonorificReady()
    {
        CheckAPI();
        _mediator.Publish(new HonorificReady());
    }
    private void OnHonorificDisposing()
    {
        _mediator.Publish(new HonorificTitleChanged(string.Empty));
    }

    /// <summary>
    ///     The Client's Title changed. Does not fire for a title change occurring on any other players.
    /// </summary>
    /// <param name="titleJson"></param>
    private void OnTitleChanged(string titleJson)
    {
        string titleData = string.IsNullOrEmpty(titleJson) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(titleJson));
        _mediator.Publish(new HonorificTitleChanged(titleData));
    }

    /// <summary>
    ///     Obtains the titleJson of our current Player. <para />
    ///     The titleJson is convered to Base64 for DTO Transfer.
    /// </summary>
    /// <returns></returns>
    public async Task<string> GetTitle()
    {
        if (!APIAvailable) return string.Empty;
        var title = await Svc.Framework.RunOnFrameworkThread(GetClientTitle.InvokeFunc).ConfigureAwait(false);
        return string.IsNullOrEmpty(title) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
    }

    /// <summary>
    ///     Applies the titleJson string to the provided <paramref name="sundesmo"/>. <para />
    ///     Expects <paramref name="titleDataBase64"/> to be a base64 encoded string of the titleJson.
    /// </summary>
    public async Task SetTitleAsync(SundesmoHandler sundesmo, string titleDataBase64)
    {
        if (!APIAvailable || sundesmo.PairObject is null) return;

        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            _logger.LogTrace($"Applying title to {sundesmo.PlayerName}");
            string titleData = string.IsNullOrEmpty(titleDataBase64) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(titleDataBase64));
            // Clear if empty, set if not.
            if (string.IsNullOrEmpty(titleData))
                ClearUserTitle.InvokeAction(sundesmo.PairObject.ObjectIndex);
            else
                SetUserTitle.InvokeAction(sundesmo.PairObject.ObjectIndex, titleData);
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Clears off the title from the provided <paramref name="sundesmo"/>.
    /// </summary>
    public async Task ClearTitleAsync(SundesmoHandler sundesmo)
    {
        if (!APIAvailable || sundesmo.PairObject is null) return;

        await Svc.Framework.RunOnFrameworkThread(() =>
        {
            _logger.LogTrace($"Removing title for {sundesmo.PlayerName}");
            ClearUserTitle.InvokeAction(sundesmo.PairObject.ObjectIndex);
        }).ConfigureAwait(false);
    }
}
