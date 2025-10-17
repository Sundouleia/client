using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;

namespace Sundouleia.Gui;

public class DebugActiveStateUI : WindowMediatorSubscriberBase
{
    private readonly ModdedStateManager _transients;
    private readonly CharaObjectWatcher _watcher;
    private readonly DistributionService _distributor;

    public DebugActiveStateUI(ILogger<DebugActiveStateUI> logger, SundouleiaMediator mediator,
        ModdedStateManager transients, CharaObjectWatcher watcher, DistributionService distributor)
        : base(logger, mediator, "Active State Debug")
    {
        _transients = transients;
        _watcher = watcher;
        _distributor = distributor;

        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal() { }

    protected override void PostDrawInternal() { }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("Data Distributor"))
            DrawDataDistributor();

        if (ImGui.CollapsingHeader("CharaObjectWatcher"))
            DrawWatcherInternals();

        if (ImGui.CollapsingHeader("Transient Resources"))
            DrawTransients();
    }

    private void DrawDataDistributor()
    {
        ImGui.Text("Updating Data: ");
        ImGui.SameLine();
        CkGui.IconText(_distributor.UpdatingData ? FAI.Check : FAI.Times, _distributor.UpdatingData ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);

        ImGui.Text("Distributing Data: ");
        ImGui.SameLine();
        CkGui.IconText(_distributor.DistributingData ? FAI.Check : FAI.Times, _distributor.DistributingData ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);

        ImGui.Text("NewVisibleUsers: ");
        CkGui.ColorTextInline(string.Join(", ", _distributor.NewVisibleUsers.Select(x => x.AliasOrUID)), ImGuiColors.DalamudViolet);

        ImGui.Text("InLimbo: ");
        CkGui.ColorTextInline(string.Join(", ", _distributor.InLimbo.Select(x => x.AliasOrUID)), ImGuiColors.DalamudViolet);

        ImGui.Text("For Update Push: ");
        CkGui.ColorTextInline(string.Join(", ", _distributor.SundesmosForUpdatePush.Select(x => x.AliasOrUID)), ImGuiColors.DalamudViolet);

        using var node = ImRaii.TreeNode($"Distribution CharaDataCache##chara-data-cache-info");
        if (!node) return;

        var dataCache = _distributor.LastCreatedData;
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

        using var table = ImRaii.Table("chara-data-cache-mods-table", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersOuter);
        if (!table) return;

        ImGui.TableSetupColumn("Hash");
        ImGui.TableSetupColumn("Replaced?");
        ImGui.TableSetupColumn("Swap?");
        ImGui.TableSetupColumn("Game Paths");
        ImGui.TableSetupColumn("Resolved Path");
        ImGui.TableHeadersRow();

        foreach (var (hash, mod) in dataCache.AppliedMods)
        {
            ImGui.TableNextColumn();
            CkGui.ColorText(hash, ImGuiColors.DalamudViolet);

            DrawIconBoolColumn(mod.HasFileReplacement);
            DrawIconBoolColumn(mod.IsFileSwap);

            ImGui.TableNextColumn();
            ImGui.Text(string.Join("\n", mod.GamePaths));

            ImGui.TableNextColumn();
            ImGui.Text(mod.ResolvedPath);
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
        ImGui.Text("Moodles");
        ImGui.TableNextColumn();
        ImGui.Text(dataCache.Moodles);

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
    }

    private void DrawWatcherInternals()
    {
        DrawCurrentOwned();
        DrawStatics();
        DrawRenderedCharas();
        DrawRenderedCompanions();
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

                foreach (var addr in CharaObjectWatcher.RenderedCharas.ToList())
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
            }
            ImGui.Separator();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error drawing rendered charas: {ex}");
        }
    }

    private unsafe void DrawRenderedCompanions()
    {
        using var _ = ImRaii.TreeNode("Rendered Companions##watcher-rendered-companions");
        if (!_) return;

        try
        {
            using (var t = ImRaii.Table($"##unowned-companions-rendered", 7, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
            {
                if (!t) return;
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("ObjIdx");
                ImGui.TableSetupColumn("ObjKind");
                ImGui.TableSetupColumn("Address");
                ImGui.TableSetupColumn("EntityId");
                ImGui.TableSetupColumn("ObjectId");
                ImGui.TableSetupColumn("OwnerId");
                ImGui.TableHeadersRow();

                foreach (var addr in CharaObjectWatcher.RenderedCompanions.ToList())
                {
                    Companion* obj = (Companion*)addr;
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->NameString.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectIndex.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->ObjectKind.ToString());
                    ImGui.TableNextColumn();
                    CkGui.ColorText($"{addr:X}", ImGuiColors.TankBlue); 
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->EntityId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->GetGameObjectId().ObjectId.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->CompanionOwnerId.ToString());
                }
            }
            ImGui.Separator();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error drawing rendered companions: {ex}");
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
