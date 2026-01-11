using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using OtterGui.Text;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI;
using SundouleiaAPI.Hub;
using SundouleiaAPI.Network;
using LuminaWorld = Lumina.Excel.Sheets.World;

namespace Sundouleia.Services;

/// <summary>
///     Determines how a location entry will match with another.
/// </summary>
public enum LocationScope : sbyte
{
    None            = 0,
    DataCenter      = 1,
    World           = 2,
    IntendedUse     = 3,
    Territory       = 4,
    HousingDistrict = 5,
    HousingWard     = 6,
    HousingPlot     = 7,
    Indoor          = 8,
}

// Can make interchangable with AddressBookEntry maybe, or maybe not...
public class LocationEntry
{
    // DataCenter
    public byte DataCenterId = 0;
    public RowRef<WorldDCGroupType> DataCenter => PlayerData.CreateRef<WorldDCGroupType>(DataCenterId);
    public string DataCenterName => DataCenter.ValueNullable?.Name.ToString() ?? "Unkown DC";

    // World
    public ushort WorldId = 0;
    public RowRef<LuminaWorld> World => PlayerData.CreateRef<LuminaWorld>(WorldId);
    public string WorldName => World.ValueNullable?.Name.ToString() ?? "Unknown World";

    // Area
    public IntendedUseEnum IntendedUse = (IntendedUseEnum)byte.MaxValue;
    public ushort TerritoryId = 0;
    public RowRef<TerritoryType> Territory => PlayerData.CreateRef<TerritoryType>(TerritoryId);
    public string TerritoryName => PlayerContent.GetTerritoryName(TerritoryId);


    // Housing
    public HousingTerritoryType HousingType = HousingTerritoryType.None;
    public sbyte Ward = -1; // Always -1 the actual plot value. (0 == ward 1)
    public sbyte Plot = -1; // Always -1 the actual plot value. (0 == plot 1)
    public bool InHousingDistrict => HousingType != HousingTerritoryType.None;

    // Indoor Preverence.
    public bool Indoors = false;
    // public short RoomNumber = -1;
    public byte ApartmentDivision = 0;
}

/// <summary>
///     Resolvers for current radar users and radar zone changing.
/// </summary>
public class LocationSvc : DisposableMediatorSubscriberBase
{
    public LocationSvc(ILogger<LocationSvc> logger, SundouleiaMediator mediator)
        : base(logger, mediator)
    {
        // Listen to zone changes.
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        Svc.ClientState.Login += SetInitialData;
        Svc.ClientState.Logout += OnLogout;

        if (Svc.ClientState.IsLoggedIn)
            SetInitialData();
    }

    // Maybe an IsInitialized here to make sure that we have valid data.

    internal static LocationEntry Previous { get; private set; } = new LocationEntry();
    internal static LocationEntry Current { get; private set; } = new LocationEntry();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.ClientState.Login -= SetInitialData;
        Svc.ClientState.Logout -= OnLogout;
    }

    private async void OnLogout(int type, int code)
    {
        // Clear location data.
        Previous = new LocationEntry();
        Current = new LocationEntry();
    }

    // Set a slightly delayed territory changed update to make sure all data is correctly loaded in.
    private async void OnTerritoryChanged(ushort newTerritory)
    {
        // Ignore territories from login zone / title screen (if any even exist)
        if (!Svc.ClientState.IsLoggedIn)
            return;

        Logger.LogDebug($"Territory changed to: {newTerritory} ({PlayerContent.GetTerritoryName(newTerritory)})");
        var prevData = Current;
        // Await for the player to be loaded
        await SundouleiaEx.WaitForPlayerLoading();
        Logger.LogDebug("Player Finished Loading, updating location data.");
        Current = GetEntryForArea();
        Mediator.Publish(new TerritoryChanged(prevData.TerritoryId, Current.TerritoryId));
    }

    private async void SetInitialData()
    {
        await SundouleiaEx.WaitForPlayerLoading();
        Current = GetEntryForArea();
        Mediator.Publish(new TerritoryChanged(0, Current.TerritoryId));
    }

    public unsafe LocationEntry GetEntryForArea()
    {
        var entry = new LocationEntry()
        {
            DataCenterId = (byte)PlayerData.CurrentDataCenter.RowId,
            WorldId = PlayerData.CurrentWorldId,
            IntendedUse = PlayerContent.TerritoryIntendedUse,
            TerritoryId = PlayerContent.TerritoryIdInstanced,
        };
        try
        {
            var houseMgr = HousingManager.Instance();
            var housingType = houseMgr->GetCurrentHousingTerritoryType();
            entry.HousingType = housingType;
            if (housingType != HousingTerritoryType.None)
            {
                entry.Ward = houseMgr->GetCurrentWard();
                entry.Plot = houseMgr->GetCurrentPlot();
                entry.ApartmentDivision = houseMgr->GetCurrentDivision();
                entry.Indoors = houseMgr->IsInside();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get location entry: {ex}");
        }
        return entry;
    }

    public static unsafe void DebugArea(LocationEntry entry)
    {
        ImGui.Text("DataCenter:");
        CkGui.ColorTextInline($"{entry.DataCenterName} ({entry.DataCenterId})", ImGuiColors.DalamudGrey);
        ImGui.Text("World:");
        CkGui.ColorTextInline($"{entry.WorldName} ({entry.WorldId})", ImGuiColors.DalamudGrey);

        ImGui.Text("Territory Intended Use:");
        CkGui.ColorTextInline($"{entry.IntendedUse} ({(byte)entry.IntendedUse})", ImGuiColors.DalamudGrey);

        ImGui.Text("Territory:");
        CkGui.ColorTextInline($"{entry.TerritoryName} ({entry.TerritoryId})", ImGuiColors.DalamudGrey);

        ImGui.Text("In Housing District:");
        ImUtf8.SameLineInner();
        CkGui.ColorTextBool(entry.InHousingDistrict.ToString(), entry.InHousingDistrict);

        ImGui.Text("Housing Type:");
        CkGui.ColorTextInline($"{entry.HousingType} ({(byte)entry.HousingType})", ImGuiColors.DalamudGrey);

        if (entry.InHousingDistrict)
        {
            ImGui.Text("Ward:");
            CkGui.ColorTextInline($"{entry.Ward + 1}", ImGuiColors.DalamudGrey);
            ImGui.Text("Plot:");
            CkGui.ColorTextInline($"{entry.Plot + 1}", ImGuiColors.DalamudGrey);
            ImGui.Text("Indoors:");
            ImUtf8.SameLineInner();
            CkGui.ColorTextBool(entry.Indoors.ToString(), entry.Indoors);
            if (entry.Indoors)
            {
                ImGui.Text("Apartment Division:");
                CkGui.ColorTextInline($"{entry.ApartmentDivision}", ImGuiColors.TankBlue);
            }
        }
    }

}