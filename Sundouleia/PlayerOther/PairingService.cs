using CkCommons;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.Pairs.Factories;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using SundouleiaAPI.Data;
using SundouleiaAPI.Data.Comparer;
using SundouleiaAPI.Data.Permissions;
using SundouleiaAPI.Network;
using SundouleiaAPI.Util;
using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.Pairs;

/// <summary>
///   Help exchange proper communication between the various managers for pairing.
/// </summary>
public sealed class PairingService : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;
    private readonly SundesmoManager _sundesmos;
    private readonly RadarManager _radar;
    private readonly RequestsManager _requests;

    public PairingService(ILogger<PairingService> logger, SundouleiaMediator mediator,
        MainConfig config, SundesmoManager sundesmos, RadarManager radar, RequestsManager requests)
        : base(logger, mediator)
    {
        _config = config;
        _sundesmos = sundesmos;
        _radar = radar;
        _requests = requests;
    }


}