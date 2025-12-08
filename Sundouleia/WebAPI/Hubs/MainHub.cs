using CkCommons;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services.Configs;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using System.Reflection;

namespace Sundouleia.WebAPI;
/// <summary>
///     Facilitates interactions with the Sundouleia Hub connection. <para />
///     To ensure that interactions with vibe lobbies function correctly, <see cref="_hubConnection"/> will be static.
/// </summary>
public partial class MainHub : DisposableMediatorSubscriberBase, ISundouleiaHubClient, IHostedService
{
    public const string MAIN_SERVER_NAME = "Sundouleia Main";
    public const string MAIN_SERVER_URI = "wss://sundouleia.kinkporium.studio";

    private readonly HubFactory _hubFactory;
    private readonly TokenProvider _tokenProvider;
    private readonly ServerConfigManager _serverConfigs;
    private readonly RequestsManager _requests;
    private readonly SundesmoManager _sundesmos;

    // Static private accessors (persistent across singleton instantiations for other static accessors.)
    private static ServerState _serverStatus = ServerState.Offline;
    private static Version _clientVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
    private static Version _expectedVersion = new Version(0, 0, 0, 0);
    private static int _expectedApiVersion = 0;
    private static bool _apiHooksInitialized = false;

    private static ConnectionResponse? _connectionResponse = null;
    private static ServerInfoResponse? _serverInfo = null;

    // Private accessors (handled within the singleton instance)
    private CancellationTokenSource _hubConnectionCTS = new();
    private CancellationTokenSource? _hubHealthCTS = new();
    private HubConnection? _hubConnection = null;
    private string? _latestToken = null;
    private bool _suppressNextNotification = false;

    public MainHub(ILogger<MainHub> logger,
        SundouleiaMediator mediator,
        HubFactory hubFactory,
        TokenProvider tokenProvider,
        ServerConfigManager serverConfigs,
        RequestsManager requests,
        SundesmoManager sundesmos)
        : base(logger, mediator)
    {
        _hubFactory = hubFactory;
        _tokenProvider = tokenProvider;
        _serverConfigs = serverConfigs;
        _requests = requests;
        _sundesmos = sundesmos;

        // Subscribe to the things.
        Mediator.Subscribe<ClosedMessage>(this, _ => HubInstanceOnClosed(_.Exception));
        Mediator.Subscribe<ReconnectedMessage>(this, async _ => await HubInstanceOnReconnected().ConfigureAwait(false));
        Mediator.Subscribe<ReconnectingMessage>(this, _ => HubInstanceOnReconnecting(_.Exception));
        Mediator.Subscribe<SendTempRequestMessage>(this, _ => OnSendTempRequest(_.TargetUser));
        Svc.ClientState.Login += OnLogin;
        Svc.ClientState.Logout += (_, _) => OnLogout();

        // If already logged in, begin.
        if (PlayerData.IsLoggedIn)
            OnLogin();
    }

    // Public static accessors.
    public static string ClientVerString => $"[Client: v{_clientVersion} (Api {ISundouleiaHub.ApiVersion})]";
    public static string ExpectedVerString => $"[Server: v{_expectedVersion} (Api {_expectedApiVersion})]";
    public static ConnectionResponse? ConnectionResponse
    {
        get => _connectionResponse;
        set
        {
            _connectionResponse = value;
            if (value != null)
            {
                _expectedVersion = _connectionResponse?.CurrentClientVersion ?? new Version(0, 0, 0, 0);
                _expectedApiVersion = _connectionResponse?.ServerVersion ?? 0;
            }
        }
    }

    public static string AuthFailureMessage { get; private set; } = string.Empty;
    public static int OnlineUsers => _serverInfo?.OnlineUsers ?? 0;
    public static UserData OwnUserData => ConnectionResponse!.User;
    public static string DisplayName => ConnectionResponse?.User.AliasOrUID ?? string.Empty;
    public static string UID => ConnectionResponse?.User.UID ?? string.Empty;
    // See how to update this later.
    public static GlobalPerms GlobalPerms => ConnectionResponse?.GlobalPerms ?? new();
    public static UserReputation Reputation => ConnectionResponse?.Reputation ?? new();
    public static ServerState ServerStatus
    {
        get => _serverStatus;
        private set
        {
            if (_serverStatus != value)
            {
                Svc.Logger.Debug($"[Hub-Main]: New ServerState: {value}, prev ServerState: {_serverStatus}", LoggerType.ApiCore);
                _serverStatus = value;
            }
        }
    }

    public static bool IsConnectionDataSynced => _serverStatus is ServerState.ConnectedDataSynced;
    public static bool IsConnected => _serverStatus is ServerState.Connected or ServerState.ConnectedDataSynced;
    public static bool IsServerAlive => _serverStatus is ServerState.ConnectedDataSynced or ServerState.Connected or ServerState.Unauthorized or ServerState.Disconnected;
    public bool ClientHasConnectionPaused => _serverConfigs.AccountStorage.FullPause;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.Login -= OnLogin;
        Svc.ClientState.Logout -= (_, _) => OnLogout();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("MainHub is starting.");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("MainHub is stopping. Closing down SundouleiaHub-Main!", LoggerType.ApiCore);
        _hubHealthCTS?.Cancel();
        // Notify of unloading upon halting the plugin.
        await Disconnect(ServerState.Disconnected, DisconnectIntent.LogoutShutdown).ConfigureAwait(false);
        _hubConnectionCTS?.Cancel();
        return;
    }

    private async void OnSendTempRequest(UserData user)
    {
        var msg = $"Temporary Request from {OwnUserData.AnonName}";
        var ret = await UserSendRequest(new(new(user.UID), true, msg)).ConfigureAwait(false);
        if (ret.ErrorCode is SundouleiaApiEc.Success && ret.Value is { } request)
        {
            Logger.LogInformation($"Temporary request sent to {user.AnonName}.", LoggerType.RadarData);
            // Add to our requests, updating the requests manager.
            _requests.AddNewRequest(request);
            return;
        }

        Logger.LogWarning($"Failed to send temporary pair request to {user.AnonName} [{ret.ErrorCode}]", LoggerType.RadarData);
    }
    private async void OnLogin()
    {
        Logger.LogInformation("Starting connection on login after fully loaded...");
        await SundouleiaEx.WaitForPlayerLoading();
        Logger.LogInformation("Client fully loaded in, Connecting.");
        // Run the call to attempt a connection to the server.
        await Connect().ConfigureAwait(false);
    }

    private async void OnLogout()
    {
        Logger.LogInformation("Stopping connection on logout", LoggerType.ApiCore);
        // as we are changing characters, we should fully unload any chara's we have data on.
        await Disconnect(ServerState.Disconnected, DisconnectIntent.LogoutShutdown).ConfigureAwait(false);
        // switch the server state to offline.
        ServerStatus = ServerState.Offline;
    }

    private void InitializeApiHooks()
    {
        if (_hubConnection is null)
            return;

        Logger.LogDebug("Initializing data", LoggerType.ApiCore);
        // [ WHEN GET SERVER CALLBACK ] --------> [PERFORM THIS FUNCTION]
        OnServerMessage((sev, msg) => _ = Callback_ServerMessage(sev, msg));
        OnHardReconnectMessage((sev, msg, state) => _ = Callback_HardReconnectMessage(sev, msg, state));
        OnRadarUserFlagged(uid => _ = Callback_RadarUserFlagged(uid));
        OnServerInfo(dto => _ = Callback_ServerInfo(dto));

        OnAddPair(dto => _ = Callback_AddPair(dto));
        OnRemovePair(dto => _ = Callback_RemovePair(dto));
        OnPersistPair(dto => _ = Callback_PersistPair(dto)); 
        OnAddRequest(dto => _ = Callback_AddRequest(dto));
        OnRemoveRequest(dto => _ = Callback_RemoveRequest(dto));

        OnBlocked(dto => _ = Callback_Blocked(dto));
        OnUnblocked(dto => _ = Callback_Unblocked(dto));

        OnIpcUpdateFull(dto => _ = Callback_IpcUpdateFull(dto));
        OnIpcUpdateMods(dto => _ = Callback_IpcUpdateMods(dto));
        OnIpcUpdateOther(dto => _ = Callback_IpcUpdateOther(dto));
        OnIpcUpdateSingle(dto => _ = Callback_IpcUpdateSingle(dto));
        OnSingleChangeGlobal(dto => _ = Callback_SingleChangeGlobal(dto));
        OnBulkChangeGlobal(dto => _ = Callback_BulkChangeGlobal(dto));
        OnSingleChangeUnique(dto => _ = Callback_SingleChangeUnique(dto));
        OnBulkChangeUnique(dto => _ = Callback_BulkChangeUnique(dto));

        OnRadarAddUpdateUser(dto => _ = Callback_RadarAddUpdateUser(dto));
        OnRadarRemoveUser(dto => _ = Callback_RadarRemoveUser(dto));
        OnRadarChat(dto => _ = Callback_RadarChat(dto));

        OnUserIsUnloading(dto => _ = Callback_UserIsUnloading(dto));
        OnUserOffline(dto => _ = Callback_UserOffline(dto));
        OnUserOnline(dto => _ = Callback_UserOnline(dto));
        OnProfileUpdated(dto => _ = Callback_ProfileUpdated(dto));
        OnShowVerification(dto => _ = Callback_ShowVerification(dto));

        // recreate a new health check token
        _hubHealthCTS = _hubHealthCTS.SafeCancelRecreate();
        // Start up our health check loop.
        _ = ClientHealthCheckLoop(_hubHealthCTS!.Token);
        // set us to initialized (yippee!!!)
        _apiHooksInitialized = true;
    }

    public async Task<bool> HealthCheck()
        => await _hubConnection!.InvokeAsync<bool>(nameof(HealthCheck)).ConfigureAwait(false);
    public async Task<ConnectionResponse> GetConnectionResponse()
        => await _hubConnection!.InvokeAsync<ConnectionResponse>(nameof(GetConnectionResponse)).ConfigureAwait(false);

    public async Task<List<OnlineUser>> UserGetOnlinePairs()
        => await _hubConnection!.InvokeAsync<List<OnlineUser>>(nameof(UserGetOnlinePairs)).ConfigureAwait(false);

    public async Task<List<UserPair>> UserGetAllPairs()
        => await _hubConnection!.InvokeAsync<List<UserPair>>(nameof(UserGetAllPairs)).ConfigureAwait(false);

    public async Task<List<SundesmoRequest>> UserGetSundesmoRequests()
        => await _hubConnection!.InvokeAsync<List<SundesmoRequest>>(nameof(UserGetSundesmoRequests)).ConfigureAwait(false);

    public async Task<FullProfileData> UserGetProfileData(UserDto dto, bool allowNsfw)
        => await _hubConnection!.InvokeAsync<FullProfileData>(nameof(UserGetProfileData), dto, allowNsfw).ConfigureAwait(false);

    /// <summary>
    ///     Loads in all our pairs from the Sundouleia server.
    /// </summary>
    private async Task LoadInitialSundesmos()
    {
        var allSundesmo = await UserGetAllPairs().ConfigureAwait(false);
        _sundesmos.AddSundesmos(allSundesmo);
        Logger.LogDebug($"Initial Sundesmos Loaded: [{string.Join(", ", allSundesmo.Select(x => x.User.AliasOrUID))}]", LoggerType.ApiCore);
    }

    /// <summary>
    ///     Retrieves the OnlineUser objects from the pairs that are online.
    /// </summary>
    /// <returns></returns>
    private async Task LoadOnlineSundesmos()
    {
        var onlineUsers = await UserGetOnlinePairs().ConfigureAwait(false);
        foreach (var entry in onlineUsers)
            _sundesmos.MarkSundesmoOnline(entry, false);

        Logger.LogDebug($"Online Users: [{string.Join(", ", onlineUsers.Select(x => x.User.AliasOrUID))}]", LoggerType.ApiCore);
    }

    /// <summary>
    ///     Load in any requests that we have pending. Can be either sent or received.
    /// </summary>
    private async Task LoadRequests()
    {
        var requests = await UserGetSundesmoRequests().ConfigureAwait(false);

#if DEBUG
        // Generate some dummy entries.
        var dummyRequests = new List<SundesmoRequest>();
        for (int i = 0; i < 5; i++)
        {
            dummyRequests.Add(new SundesmoRequest(new($"Dummy Sender {i}"), OwnUserData, new(false, "Wawa", "Blah Blah", (ushort)i, (ushort)(i * 10)), DateTime.Now));
            dummyRequests.Add(new SundesmoRequest(OwnUserData, new($"Dummy Recipient {i}"), new(false, "Wawa", "Blah Blah", (ushort)(i * 5), (ushort)(i * 15)), DateTime.Now));
        }
        requests.AddRange(dummyRequests);
#endif

        _requests.AddNewRequest(requests);
    }

    /// <summary>
    ///     Awaits for the player to be present, ensuring that they are 
    ///     logged in before this fires. <para/>
    ///     
    ///     There is a possibility we wont need this anymore with the new system,
    ///     so attempt it without it once this works!
    /// </summary>
    private async Task WaitForWhenPlayerIsPresent(CancellationToken token)
    {
        while (!PlayerData.AvailableThreadSafe && !token.IsCancellationRequested)
        {
            Logger.LogDebug("Player not loaded in yet, waiting", LoggerType.ApiCore);
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        }
    }
}
