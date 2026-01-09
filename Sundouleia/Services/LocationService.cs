using CkCommons;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;

namespace Sundouleia.Services;

/// <summary>
///     The scope of how broad to allow a match to be in.
/// </summary>
[Flags]
public enum LocationScope : short
{
    DataCenter,
    World,
    Territory,
    TerritoryIntendedUse,
    HousingDistrict,
    HousingWard,
    HousingPlot,
    Apartment, // Can be confusing as this can go hand in hand with IndoorLocation
    IndoorLocation // Can be confusing as this can go hand in hand with Apartment
}

// Stored Information about a specific location in the game.
// Flexibly by territory scope, housing wards, housing plots, or spesific indoor homes.
public class LocationData
{
    public uint DataCenter = 0;
    public uint World = 0;
}


/// <summary>
///     Resolvers for current radar users and radar zone changing.
/// </summary>
public class LocationService : DisposableMediatorSubscriberBase
{
    private readonly MainConfig _config;

    public LocationService(ILogger<LocationService> logger, SundouleiaMediator mediator, MainConfig config)
        : base(logger, mediator)
    {
        _config = config;

        // Listen to zone changes.
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        Svc.ClientState.Login += SetInitialData;
        Svc.ClientState.Logout += OnLogout;

        if (Svc.ClientState.IsLoggedIn)
            SetInitialData();
    }

    internal static LocationData PreviousLocation { get; private set; } = new();
    internal static LocationData CurrentLocation { get; private set; } = new();

    public static ushort CurrWorld { get; private set; } = 0;
    public static string CurrWorldName { get; private set; } = string.Empty;
    public static ushort CurrZone { get; private set; } = 0;
    public static string CurrZoneName => PlayerContent.GetTerritoryName(CurrZone);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.ClientState.Login -= SetInitialData;
        Svc.ClientState.Logout -= OnLogout;
    }

    private async void SetInitialData()
    {
        await SundouleiaEx.WaitForPlayerLoading();
        CurrWorld = PlayerData.CurrentWorldId;
        CurrWorldName = PlayerData.CurrentWorldName;
        CurrZone = PlayerContent.TerritoryIdInstanced;
        Mediator.Publish(new TerritoryChanged(0, CurrZone));
    }

    private async void OnLogout(int type, int code)
    {
        CurrWorld = 0;
        CurrWorldName = string.Empty;
        CurrZone = 0;
    }

    private async void OnTerritoryChanged(ushort newTerritory)
    {
        // Ignore territories from login zone / title screen (if any even exist)
        if (!Svc.ClientState.IsLoggedIn)
            return;

        var prevZone = CurrZone;
        CurrZone = newTerritory;
        Mediator.Publish(new TerritoryChanged(prevZone, CurrZone));
    }
}