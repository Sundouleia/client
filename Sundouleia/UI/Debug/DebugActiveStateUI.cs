using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using OtterGui;
using Sundouleia.ModFiles;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;
using Sundouleia.Watchers;
using Sundouleia.WebAPI.Utils;

namespace Sundouleia.Gui;

public class DebugActiveStateUI : WindowMediatorSubscriberBase
{
    private readonly TransientResourceManager _transients;
    private readonly CharaObjectWatcher _watcher;

    public DebugActiveStateUI(ILogger<DebugActiveStateUI> logger, SundouleiaMediator mediator,
        TransientResourceManager transients, CharaObjectWatcher watcher) 
        : base(logger, mediator, "Active State Debug")
    {
        _transients = transients;
        _watcher = watcher;

        IsOpen = true;
        this.SetBoundaries(new Vector2(625, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal() { }

    protected override void PostDrawInternal() { }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("CharaObjectWatcher"))
            DrawWatcherInternals();

        if (ImGui.CollapsingHeader("Transient Resources"))
            DrawTransients();
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
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
                    GameObject* obj = (GameObject*)_watcher.WatchedMinionMountAddr;
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
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
                    GameObject* obj = (GameObject*)_watcher.WatchedCompanionAddr;
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
                }
                else
                {
                    ImGui.TableNextRow();
                }
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
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
                    ImGui.Text(obj->GetGameObjectId().Id.ToString());
                    ImGui.TableNextColumn();
                    ImGui.Text(obj->OwnerId.ToString());
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
        _transients.DrawPersistantTransients();
    }
}
