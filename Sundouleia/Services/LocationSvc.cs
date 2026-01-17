using CkCommons;
using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using OtterGui.Text;
using Sundouleia.Services.Mediator;
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

public enum ResidentialArea : sbyte
{
    None = 0,
    LavenderBeds = 1,
    Mist = 2,
    Goblet = 3,
    Shirogane = 4,
    Empyreum = 5,
}

// Can make interchangable with AddressBookEntry maybe, or maybe not...
public class LocationEntry
{
    public byte DataCenterId = 0;
    public ushort WorldId = 0;
    public IntendedUseEnum IntendedUse = (IntendedUseEnum)byte.MaxValue;
    public ushort TerritoryId = 0;
    // Housing (This would be indoors if the scope was indoors)
    public HousingTerritoryType HousingType = HousingTerritoryType.None;
    public ResidentialArea HousingArea = ResidentialArea.None;
    public sbyte Ward = 0; // Always -1 the actual plot value. (0 == ward 1)
    public sbyte Plot = 0; // Always -1 the actual plot value. (0 == plot 1)
    // public short RoomNumber = -1;
    public byte ApartmentDivision = 0;

    // Helpers.
    [JsonIgnore] public RowRef<WorldDCGroupType> DataCenter => PlayerData.CreateRef<WorldDCGroupType>(DataCenterId);
    [JsonIgnore] public string DataCenterName => DataCenter.ValueNullable?.Name.ToString() ?? "Unkown DC";
    [JsonIgnore] public RowRef<LuminaWorld> World => PlayerData.CreateRef<LuminaWorld>(WorldId);
    [JsonIgnore] public string WorldName => World.ValueNullable?.Name.ToString() ?? "Unknown World";
    [JsonIgnore] public RowRef<TerritoryType> Territory => PlayerData.CreateRef<TerritoryType>(TerritoryId);
    [JsonIgnore] public string TerritoryName => PlayerContent.GetTerritoryName(TerritoryId);
    [JsonIgnore] public bool IsInHousing => HousingType != HousingTerritoryType.None;
    [JsonIgnore] public bool IsIndoors => HousingType is HousingTerritoryType.Indoor;

    public LocationEntry Clone()
        => new LocationEntry()
        {
            DataCenterId = DataCenterId,
            WorldId = WorldId,
            IntendedUse = IntendedUse,
            TerritoryId = TerritoryId,
            HousingType = HousingType,
            HousingArea = HousingArea,
            Ward = Ward,
            Plot = Plot,
            ApartmentDivision = ApartmentDivision,
        };
}

/// <summary>
///     Resolvers for current radar users and radar zone changing.
/// </summary>
public class LocationSvc : DisposableMediatorSubscriberBase
{
    public static readonly Dictionary<ResidentialArea, string> ResidentialNames = new()
    {
        [ResidentialArea.None] = "None",
        [ResidentialArea.LavenderBeds] = "Lavender Beds",
        [ResidentialArea.Mist] = "Mist",
        [ResidentialArea.Goblet] = "Goblet",
        [ResidentialArea.Shirogane] = "Shirogane",
        [ResidentialArea.Empyreum] = "Empyreum",
    };

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

    public static byte DataCenterId { get; private set; } = 0;
    public static ushort WorldId { get; private set; } = 0;

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
        Previous = Current;
        // Await for the player to be loaded.
        // This also ensures that by the time this is fired, all visible users will also be visible.
        await SundouleiaEx.WaitForPlayerLoading();
        Logger.LogDebug("Player Finished Loading, updating location data.");
        Current = GetEntryForArea();
        Mediator.Publish(new TerritoryChanged(Previous.TerritoryId, Current.TerritoryId));
    }

    private async void SetInitialData()
    {
        await SundouleiaEx.WaitForPlayerLoading();
        // Initialize the DC & World to avoid unessisary extra wait on future zone changes.
        DataCenterId = (byte)PlayerData.CurrentDataCenter.RowId;
        WorldId = PlayerData.CurrentWorldId;
        // Then update the current zone for the area.
        Current = GetEntryForArea();
        Mediator.Publish(new TerritoryChanged(0, Current.TerritoryId));
    }

    private ResidentialArea GetAreaByTerritory(uint id)
        => Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(id) is { } territory ? GetAreaByRowRef(territory) : ResidentialArea.None;

    private ResidentialArea GetAreaByRowRef(TerritoryType territory)
        => territory.PlaceNameRegion is { } placeRegion
        ? placeRegion.RowId switch
        {
            2402 => ResidentialArea.Shirogane,
            25 => ResidentialArea.Empyreum,
            23 => ResidentialArea.LavenderBeds,
            24 => ResidentialArea.Goblet,
            22 => ResidentialArea.Mist,
            _ => ResidentialArea.None,
        } : ResidentialArea.None;


    public unsafe LocationEntry GetEntryForArea()
    {
        var entry = new LocationEntry()
        {
            DataCenterId = DataCenterId,
            WorldId = WorldId,
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
                // Get the housing area.
                entry.HousingArea = GetAreaByTerritory(entry.TerritoryId);

                // Get the housing details.
                entry.Ward = houseMgr->GetCurrentWard();
                entry.Plot = houseMgr->GetCurrentPlot();
                entry.ApartmentDivision = houseMgr->GetCurrentDivision();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to get location entry: {ex}");
        }
        return entry;
    }

    /// <summary>
    ///     Checks to see if another Location entry matches the current area's Location by scope.
    /// </summary>
    /// <param name="entry"> The entry to compare against the current area. </param>
    /// <param name="scope"> What scope is a required for a match. </param>
    /// <returns> If <paramref name="entry"/> matches the current area by <paramref name="scope"/>. </returns>
    public static bool IsMatch(LocationEntry entry, LocationScope scope)
        => scope switch
        {
            LocationScope.DataCenter
                => entry.DataCenterId == Current.DataCenterId,

            LocationScope.World 
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId,

            LocationScope.IntendedUse
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IntendedUse  == Current.IntendedUse,

            LocationScope.Territory
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IntendedUse  == Current.IntendedUse
                && entry.TerritoryId  == Current.TerritoryId,

            LocationScope.HousingDistrict
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IsInHousing  && Current.IsInHousing
                && entry.HousingArea  == Current.HousingArea,
            
            LocationScope.HousingWard
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IsInHousing  && Current.IsInHousing
                && entry.HousingArea  == Current.HousingArea
                && entry.Ward         == Current.Ward,

            LocationScope.HousingPlot
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IsInHousing  && Current.IsInHousing
                && entry.HousingArea  == Current.HousingArea
                && entry.Ward         == Current.Ward
                && entry.Plot         == Current.Plot,

            LocationScope.Indoor
                => entry.DataCenterId == Current.DataCenterId
                && entry.WorldId      == Current.WorldId
                && entry.IsInHousing  && Current.IsInHousing
                && entry.HousingArea  == Current.HousingArea
                && entry.Ward         == Current.Ward
                && entry.Plot         == Current.Plot
                && entry.IsIndoors    && Current.IsIndoors,

            _ => false,
        };

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
        CkGui.ColorTextBool(entry.IsInHousing.ToString(), entry.IsInHousing);
        if (entry.IsInHousing)
        {
            ImGui.Text("Housing Area:");
            CkGui.ColorTextInline($"{ResidentialNames[entry.HousingArea]}", ImGuiColors.DalamudGrey);
            ImGui.Text("Housing Type:");
            CkGui.ColorTextInline($"{entry.HousingType} ({(byte)entry.HousingType})", ImGuiColors.DalamudGrey);
            ImGui.Text("Ward:");
            CkGui.ColorTextInline($"{entry.Ward + 1}", ImGuiColors.DalamudGrey);
            ImGui.Text("Plot:");
            CkGui.ColorTextInline($"{entry.Plot + 1}", ImGuiColors.DalamudGrey);
            ImGui.Text("Indoors:");
            ImUtf8.SameLineInner();
            CkGui.ColorTextBool(entry.IsIndoors.ToString(), entry.IsIndoors);
            if (entry.IsIndoors)
            {
                ImGui.Text("Apartment Division:");
                CkGui.ColorTextInline($"{entry.ApartmentDivision}", ImGuiColors.TankBlue);
            }
        }
    }

}