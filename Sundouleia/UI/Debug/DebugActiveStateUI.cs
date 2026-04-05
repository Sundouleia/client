using CkCommons;
using CkCommons.Gui;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using LuminaTerritoryType = Lumina.Excel.Sheets.TerritoryType;

namespace Sundouleia.Gui;

public class DebugActiveStateUI : WindowMediatorSubscriberBase
{
    private readonly LimboStateManager _limboManager;
    private readonly ModdedStateManager _transients;
    private readonly CharaWatcher _watcher;
    private readonly ClientUpdateService _updater;

    public DebugActiveStateUI(ILogger<DebugActiveStateUI> logger, SundouleiaMediator mediator,
        LimboStateManager limboManager, ModdedStateManager transients, CharaWatcher watcher, 
        ClientUpdateService updater)
        : base(logger, mediator, "Active State Debug")
    {
        _limboManager = limboManager;
        _transients = transients;
        _watcher = watcher;
        _updater = updater;

        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("Loci Data"))
            DrawLociData();

        if (ImGui.CollapsingHeader("Data Distributor"))
            DrawDataDistributor();

        if (ImGui.CollapsingHeader("CharaObjectWatcher"))
            DrawWatcherInternals();

        if (ImGui.CollapsingHeader("Transient Resources"))
            DrawTransients();

        if (ImGui.CollapsingHeader("Location Info"))
        {

            using (var t = ImRaii.Table("Location Data", 2, ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchSame))
            {
                if (!t) return;

                ImGui.TableSetupColumn("Previous");
                ImGui.TableSetupColumn("Current");
                ImGui.TableHeadersRow();

                ImGui.TableNextColumn();
                LocationSvc.DebugArea(LocationSvc.Previous);

                ImGui.TableNextColumn();
                LocationSvc.DebugArea(LocationSvc.Current);
                ImGui.TableNextRow();
            }
        }

        ImGui.Separator();
        DrawLiveLocation();
    }

    private unsafe void DrawLiveLocation()
    {
        try
        {
            var agentMap = AgentMap.Instance();
            if (agentMap != null)
            {
                uint mapId = agentMap == null ? 0 : agentMap->CurrentMapId;
                uint territoryId = agentMap == null ? 0 : agentMap->CurrentTerritoryId;
                ImGui.Text("AgentMap MapID:");
                CkGui.ColorTextInline($"{mapId}", ImGuiColors.DalamudGrey);

                ImGui.Text("AgentMap TerritoryID:");
                CkGui.ColorTextInline($"{territoryId}", ImGuiColors.DalamudGrey);
            }

            ImGui.Text("World:");
            CkGui.ColorTextInline($"{PlayerData.CurrentWorldName} ({PlayerData.CurrentWorldId})", ImGuiColors.DalamudGrey);

            ImGui.Text("Territory:");
            CkGui.ColorTextInline($"{PlayerContent.GetTerritoryName(PlayerContent.TerritoryIdInstanced)} ({PlayerContent.TerritoryIdInstanced})", ImGuiColors.DalamudGrey);

            ImGui.Text("Intended Use:");
            CkGui.ColorTextInline($"{PlayerContent.TerritoryIntendedUse} ({(byte)PlayerContent.TerritoryIntendedUse})", ImGuiColors.DalamudGrey);


            ImGui.Text("HouseType:");
            var houseMgr = HousingManager.Instance();
            var housingType = houseMgr->GetCurrentHousingTerritoryType();
            CkGui.ColorTextInline($"{housingType} ({(ushort)housingType})", ImGuiColors.DalamudGrey);

            ImGui.Text("Ward:");
            CkGui.ColorTextInline($"{houseMgr->GetCurrentWard()}", ImGuiColors.DalamudViolet);

            ImGui.Text("Plot:");
            CkGui.ColorTextInline($"{houseMgr->GetCurrentPlot()}", ImGuiColors.DalamudViolet);

            ImGui.Text("Has House Permissions:");
            CkGui.BoolIcon(houseMgr->HasHousePermissions());

            ImGui.Text("Is Outside:");
            CkGui.BoolIcon(houseMgr->IsOutside());
            CkGui.TextInline("/");
            CkGui.TextInline("Is Inside:");
            CkGui.BoolIcon(houseMgr->IsInside());

            var houseData = houseMgr->IsOutside()
                ? houseMgr->GetCurrentHouseId() : houseMgr->GetCurrentIndoorHouseId();
            ImGui.Text("HouseID:");
            CkGui.ColorTextInline(houseData.Id.ToString(), ImGuiColors.DalamudViolet);
            using (ImRaii.PushIndent())
            {
                ImGui.Text("TerritoryType:");
                CkGui.ColorTextInline($"{PlayerContent.GetTerritoryName(houseData.TerritoryTypeId)} ({houseData.TerritoryTypeId})", ImGuiColors.DalamudGrey);

                ImGui.Text("WorldID:");
                CkGui.ColorTextInline(houseData.WorldId.ToString(), ImGuiColors.DalamudGrey);

                ImGui.Text("Ward:");
                CkGui.ColorTextInline($"{houseData.WardIndex}", ImGuiColors.DalamudGrey);

                ImGui.Text(houseData.IsApartment ? "ApartmentDivision:" : "Plot:");
                CkGui.ColorTextInline($"{houseData.ApartmentDivision}", ImGuiColors.DalamudGrey);

                ImGui.Text("IsApartment");
                CkGui.BoolIcon(houseData.IsApartment);
                if (houseData.IsApartment)
                {
                    CkGui.ColorTextInline($"Room #{houseData.RoomNumber}", ImGuiColors.DalamudGrey);
                }
            }

            ImGui.Text("Personal Ownership Definitions:");
            foreach (var type in Enum.GetValues<EstateType>())
            {
                var shared = type is EstateType.SharedEstate ? 0 : -1;
                var ownedHouse = HousingManager.GetOwnedHouseId(type, shared);
                CkGui.ColorText($"{type}:", ImGuiColors.DalamudViolet);
                ImUtf8.SameLineInner();
                ImGui.SetNextItemWidth(250);
                ImGui.InputULong($"##{type}-owned-house-id", ref ownedHouse.Id, 10, flags: ImGuiInputTextFlags.ReadOnly);
                CkGui.TextInline(" | ");
                CkGui.TextInline("At Location:");
                CkGui.BoolIcon(ownedHouse.Id == houseData.Id);
            }

            ImGui.Separator();
            var fcInfo = Framework.Instance()->GetUIModule()->GetInfoModule()->GetInfoProxyFreeCompany();
            if (fcInfo != null)
            {
                ImGui.Text($"FC ID: {fcInfo->Id}");
                ImGui.Text($"FC HomeworldId: {fcInfo->HomeWorldId}");
                ImGui.Text($"FC GrandCompany: {fcInfo->GrandCompany}");
                ImGui.Text($"FC Rank: {fcInfo->Rank}");
                ImGui.Text($"FC OnlineMembers: {fcInfo->OnlineMembers}");
                ImGui.Text($"FC TotalMembers: {fcInfo->TotalMembers}");
                ImGui.Text($"FC NameString: {fcInfo->NameString}");
                ImGui.Text($"FC MasterString: {fcInfo->MasterString}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error drawing live location: {ex}");
        }

        if (ImGui.Button("Generator TerritoryData"))
        {
            var data = Svc.Data.GetExcelSheet<LuminaTerritoryType>(Svc.ClientState.ClientLanguage)!
                .Where(t => t.RowId != 0 && t.TerritoryIntendedUse.RowId == 1 && !string.IsNullOrWhiteSpace(t.PlaceNameRegion.ValueNullable?.Name.ToString()))
                .Select(t =>
                {
                    var placeRegion = t.PlaceNameRegion.ValueNullable!.Value;
                    return new
                    {
                        Id = (ushort)t.RowId,
                        RegionId = placeRegion.RowId,
                        Region = placeRegion.Name.ToString(),
                        Name = t.PlaceName.ValueNullable?.Name.ToString()
                    };
                })
                .Where(x => x != null)
                .ToDictionary(
                    x => x!.Id,
                    x => (x!.Region, x!.Name, x!.RegionId)
                );
            // output this as a copiable log message string.
            Svc.Logger.Information("TerritoryData:\n " + string.Join("\n ", data.Select(x => $"{x.Key}: {x.Value}")));
        }
    }

    private unsafe void DrawLociData()
    {
        ImGui.Text("Loci IPC Status:");
        CkGui.ColorTextInline(IpcCallerLoci.APIAvailable ? "Available" : "Unavailable", ImGuiColors.ParsedOrange);

        ImUtf8.TextFrameAligned($"Active Loci: {LociData.Cache.DataInfo.Count()}");
        if (LociData.Cache.DataInfo.Count > 0)
        {
            ImGui.SameLine();
            LociHelpers.DrawTuples(LociData.Cache.DataInfoList.ToList(), ImGui.GetContentRegionAvail().X, LociIcon.SizeFramed);
        }

        ImGui.Text($"Total Statuses: {LociData.Cache.StatusList.Count()}");
        ImGui.Text($"Total Presets: {LociData.Cache.PresetList.Count()}");
    }

    private void DrawDataDistributor()
    {
        ImGui.Text("Updating Data: ");
        ImGui.SameLine();
        CkGui.IconText(_updater.UpdatingData ? FAI.Check : FAI.Times, _updater.UpdatingData ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);

        ImGui.Text("Distributing Data: ");
        ImGui.SameLine();
        CkGui.IconText(_updater.Distributing ? FAI.Check : FAI.Times, _updater.Distributing ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);

        ImGui.Text("NewVisibleUsers: ");
        CkGui.ColorTextInline(string.Join(", ", _updater.NewVisibleUsers.Select(x => x.DisplayName)), ImGuiColors.DalamudViolet);

        ImGui.Text("InLimbo: ");
        CkGui.ColorTextInline(string.Join(", ", _limboManager.InLimbo.Select(x => x.DisplayName)), ImGuiColors.DalamudViolet);

        ImGui.Text("For Update Push: ");
        CkGui.ColorTextInline(string.Join(", ", _updater.UsersForUpdatePush.Select(x => x.DisplayName)), ImGuiColors.DalamudViolet);

        using var node = ImRaii.TreeNode($"Distribution CharaDataCache##chara-data-cache-info");
        if (!node) return;

        var dataCache = _updater.LatestData;
        DebugAppliedMods(dataCache);
        DebugDataCachePlayer(dataCache);
        DebugDataCacheNonPlayer(dataCache, OwnedObject.MinionOrMount);
        DebugDataCacheNonPlayer(dataCache, OwnedObject.Pet);
        DebugDataCacheNonPlayer(dataCache, OwnedObject.Companion);
    }

    // In respect to the player for now, might make in respect to OwnedObject later but idk.
    private void DebugAppliedMods(ClientDataCache dataCache)
    {
        using var node = ImRaii.TreeNode($"Applied Mods##chara-data-cache-mods");
        if (!node) return;

        using (var modReps = ImRaii.Table("chara-mod-replacements", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter))
        {
            if (!modReps) return;
            ImGui.TableSetupColumn("Hash");
            ImGui.TableSetupColumn("FileSwap");
            ImGui.TableSetupColumn("Replacement");
            ImGui.TableSetupColumn("Game Paths");
            ImGui.TableSetupColumn("Resolved Path");
            ImGui.TableHeadersRow();

            foreach (var (hash, mod) in dataCache.ModdedFiles)
            {
                ImGui.TableNextColumn();
                CkGui.HoverIconText(FAI.Hashtag, ImGuiColors.DalamudViolet.ToUint());
                CkGui.AttachTooltip(hash);

                DrawIconBoolColumn(mod.IsFileSwap);
                DrawIconBoolColumn(mod.HasFileReplacement);

                ImGui.TableNextColumn();
                ImGui.Text(string.Join("\n", mod.GamePaths));

                ImGui.TableNextColumn();
                ImGui.Text(mod.ResolvedPath);
            }

            foreach (var (hash, mod) in dataCache.SwappedFiles)
            {
                ImGui.TableNextColumn();

                DrawIconBoolColumn(mod.IsFileSwap);
                DrawIconBoolColumn(mod.HasFileReplacement);
                
                ImGui.TableNextColumn();
                ImGui.Text(string.Join("\n", mod.GamePaths));
                
                ImGui.TableNextColumn();
                ImGui.Text(mod.ResolvedPath);
            }
        }
    }

    private void DebugDataCachePlayer(ClientDataCache dataCache)
    {
        using var node = ImRaii.TreeNode($"Player Data##chara-data-cache-player");
        if (!node) return;

        using var table = ImRaii.Table("chara-data-cache-playerdata", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Data Type");
        ImGui.TableSetupColumn("Data Value", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        ImGui.Text("Glamourer");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.GlamourerState[OwnedObject.Player]);

        ImGui.TableNextColumn();
        ImGui.Text("CPlus");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.CPlusState[OwnedObject.Player]);

        ImGui.TableNextColumn();
        ImGui.Text("LociData");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.LociState[OwnedObject.Player]);

        ImGui.TableNextColumn();
        ImGui.Text("ModManips");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.ModManips);

        ImGui.TableNextColumn();
        ImGui.Text("HeelsOffset");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.HeelsOffset);

        ImGui.TableNextColumn();
        ImGui.Text("TitleData");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.TitleData);

        ImGui.TableNextColumn();
        ImGui.Text("PetNames");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.PetNames);
    }

    private void DebugDataCacheNonPlayer(ClientDataCache dataCache, OwnedObject obj)
    {
        using var node = ImRaii.TreeNode($"{obj} Data##chara-data-cache-{obj}");
        if (!node) return;

        using var table = ImRaii.Table($"chara-data-cache-{obj}data", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Data Type");
        ImGui.TableSetupColumn("Data Value");
        ImGui.TableHeadersRow();

        ImGui.TableNextColumn();
        ImGui.Text("Glamourer");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.GlamourerState[obj]);

        ImGui.TableNextColumn();
        ImGui.Text("CPlus");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.CPlusState[obj]);

        ImGui.TableNextColumn();
        ImGui.Text("Loci");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.LociState[obj]);
    }

    private void DrawWatcherInternals()
    {
        DrawCurrentOwned();
        DrawStatics();
        DrawRenderedCharas();
    }

    private void DrawCurrentOwned()
    {
        using var _ = ImRaii.TreeNode("Owned Addresses");
        if (!_) return;

        using (var t = ImRaii.Table("##OwnedAddr", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            if (!t) return;
            ImGui.TableSetupColumn("Address");
            ImGui.TableSetupColumn("Associated Type");
            ImGui.TableHeadersRow();

            foreach (var addr in _watcher.CurrentOwned)
            {
                ImGui.TableNextColumn();
                CkGui.ColorText($"{addr:X}", ImGuiColors.TankBlue);
                ImGui.TableNextColumn();
                if (_watcher.WatchedTypes.TryGetValue(addr, out var obj))
                    ImGui.Text(obj.ToString());
            }
        }
        ImGui.Separator();
    }

    private unsafe void DrawStatics()
    {
        using var _ = ImRaii.TreeNode("Statics##watcher-statics");
        if (!_) return;

        try
        {
            using (var t = ImRaii.Table($"##watcher-static-infos", 9, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                if (!t) return;
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Address");
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ObjIdx");
                ImGui.TableSetupColumn("ObjKind");
                ImGui.TableSetupColumn("ContentId");
                ImGui.TableSetupColumn("EntityId");
                ImGui.TableSetupColumn("ObjectId");
                ImGui.TableSetupColumn("OwnerId");
                ImGui.TableHeadersRow();

                // Handle Static Player.
                ImGui.TableNextColumn();
                ImGui.Text("Player");
                if (_watcher.WatchedPlayerAddr != IntPtr.Zero)
                {
                    Character* obj = (Character*)_watcher.WatchedPlayerAddr;
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{_watcher.WatchedPlayerAddr:X}", ImGuiColors.TankBlue);
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ContentId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
                }
                else
                {
                    ImGui.TableNextRow();
                }
                // Handle Static Minion Mount.
                ImGui.TableNextColumn();
                ImGui.Text("Minion/Mount");
                if (_watcher.WatchedMinionMountAddr != IntPtr.Zero)
                {
                    Character* obj = (Character*)_watcher.WatchedMinionMountAddr;
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{_watcher.WatchedMinionMountAddr:X}", ImGuiColors.TankBlue);
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->CompanionOwnerId.ToString());
                }
                else
                {
                    ImGui.TableNextRow();
                }
                // Handle Static Pet.
                ImGui.TableNextColumn();
                ImGui.Text("Pet");
                if (_watcher.WatchedPetAddr != IntPtr.Zero)
                {
                    GameObject* obj = (GameObject*)_watcher.WatchedPetAddr;
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{_watcher.WatchedPetAddr:X}", ImGuiColors.TankBlue);
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
                }
                else
                {
                    ImGui.TableNextRow();
                }
                // Handle Static Companion.
                ImGui.TableNextColumn();
                ImGui.Text("Companion");
                if (_watcher.WatchedCompanionAddr != IntPtr.Zero)
                {
                    Companion* obj = (Companion*)_watcher.WatchedCompanionAddr;
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{_watcher.WatchedCompanionAddr:X}", ImGuiColors.TankBlue);
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->CompanionOwnerId.ToString());
                }
                else
                {
                    ImGui.TableNextRow();
                }
                // Supposed Companion?
                //ImGui.TableNextColumn();
                //ImGui.Text("Supposed Companion");
                //Companion* maybeObj = (Companion*)OwnedObjects.CompanionAddress;
                //if (maybeObj != null)
                //{
                //    ImGui.TableNextColumn();
                //    CkGui.ColorText($"{(nint)maybeObj:X}", ImGuiColors.TankBlue);
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->NameString.ToString());
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->ObjectIndex.ToString());
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->ObjectKind.ToString());
                //    ImGui.TableNextColumn();
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->EntityId.ToString());
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->GetGameObjectId().ObjectId.ToString());
                //    ImGui.TableNextColumn();
                //    ImGui.Text(maybeObj->OwnerId.ToString());
                //}
            }
            ImGui.Separator();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error drawing current owned objects: {ex}");
        }
    }

    private unsafe void DrawRenderedCharas()
    {
        using var _ = ImRaii.TreeNode("Rendered Charas##watcher-rendered-charas");
        if (!_) return;

        try
        {
            using (var t = ImRaii.Table($"##unowned-charas-rendered", 8, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
            {
                if (!t) return;
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ObjIdx");
                ImGui.TableSetupColumn("ObjKind");
                ImGui.TableSetupColumn("Address");
                ImGui.TableSetupColumn("ContentId");
                ImGui.TableSetupColumn("EntityId");
                ImGui.TableSetupColumn("ObjectId");
                ImGui.TableSetupColumn("OwnerId");
                ImGui.TableHeadersRow();

                foreach (var addr in CharaWatcher.RenderedCharas.ToList())
                {
                    Character* obj = (Character*)addr;
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{addr:X}", ImGuiColors.TankBlue);
                    ImGui.TableNextColumn();
                    CkGui.ColorText(obj->ContentId.ToString(), ImGuiColors.DalamudViolet);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(SundouleiaSecurity.GetIdentHashByCharacterPtr(addr));
                        ImGui.EndTooltip();
                    }
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->CompanionOwnerId.ToString());
                }

                if (GameMain.IsInGPose())
                {
                    foreach (var addr in CharaWatcher.GPoseActors.ToList())
                    {
                        GameObject* obj = (GameObject*)addr;
                        ImGui.TableNextColumn();
                        ImGui.Text(obj->NameString.ToString());
                        ImGui.TableNextColumn();
                        ImGui.Text(obj->ObjectIndex.ToString());
                        ImGui.TableNextColumn();
                        ImGui.Text(obj->ObjectKind.ToString());
                        ImGui.TableNextColumn();
                        CkGui.ColorText($"{addr:X}", ImGuiColors.TankBlue);
                        ImGui.TableNextColumn();
                        ImGui.TableNextColumn();
                        ImGui.Text(obj->EntityId.ToString());
                        ImGui.TableNextColumn();
                        ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                        ImGui.TableNextColumn();
                    }
                }
            }
            ImGui.Separator();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error drawing rendered charas: {ex}");
        }
    }

    private void DrawTransients()
    {
        // Transient Resolurce Monitoring
        _transients.DrawTransientResources();
        // Semi-Transient Resource Monitoring
        _transients.DrawPersistentTransients();
    }

    private void DrawIconBoolColumn(bool value)
    {
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
    }
}
