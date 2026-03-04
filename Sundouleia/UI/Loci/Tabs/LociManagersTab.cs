using CkCommons;
using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.RichText;
using CkCommons.Textures;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using OtterGui.Extensions;
using OtterGui.Text;
using Sundouleia.Interop;
using Sundouleia.Loci;
using Sundouleia.Pairs;
using Sundouleia.Services;
using Sundouleia.Services.Mediator;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace Sundouleia.Gui.Loci;

public class LociManagersTab
{
    private readonly ILogger<LociManagersTab> _logger;
    private readonly SundouleiaMediator _mediator;
    private readonly IpcProviderLoci _ipc;
    private readonly LociManager _manager;
    private static float SELECTOR_WIDTH => 250f * ImGuiHelpers.GlobalScale;
    public LociManagersTab(ILogger<LociManagersTab> logger, SundouleiaMediator mediator,
        IpcProviderLoci ipc, LociManager manager)
    {
        _logger = logger;
        _mediator = mediator;
        _ipc = ipc;
        _manager = manager;
    }

    private (string NameWorld, LociSM Manager)? _selected;

    public void DrawSection(Vector2 region)
    {
        using (ImRaii.Child("selector", new Vector2(SELECTOR_WIDTH, ImGui.GetContentRegionAvail().Y), true))
        {
            var size = new Vector2(ImGui.GetContentRegionAvail().X, ImUtf8.FrameHeight);
            foreach (var (name, manager) in LociManager.StatusManagers)
            {
                var isSelected = name.Equals(_selected?.NameWorld);
                if (ImGui.Selectable(name, isSelected, size: size))
                    _selected = (name, manager);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && isSelected)
                    _selected = null;
                CkGui.AttachToolTip($"Manage {name}'s Status Manager!");
            }
        }

        ImGui.SameLine();
        using var _ = CkRaii.Child("manager editor", ImGui.GetContentRegionAvail());
        if (_selected is not { } selected)
        {
            CkGui.FontTextCentered("Select an Actor to view their Status Manager!", Fonts.Default150Percent);
            return;
        }

        CkGui.FontText(selected.NameWorld, Fonts.Default150Percent);
        CkGui.IconTextAligned(FAI.Eye);
        CkGui.TextFrameAlignedInline("Is Owner Valid (Present)");
        ImGui.SameLine();
        CkGui.ColorTextBool(selected.Manager.OwnerValid ? "Valid" : "Invalid", selected.Manager.OwnerValid);

        CkGui.IconTextAligned(FAI.Link);
        CkGui.TextFrameAlignedInline("Managed by External Plugins (Ephemeral):");
        CkGui.BoolIconFramed(selected.Manager.Ephemeral, true);
        if (selected.Manager.Ephemeral)
        {
            foreach (var hostKey in selected.Manager.EphemeralHosts)
                CkGui.BulletText(hostKey, ImGuiColors.DalamudGrey2.ToUint());
        }

        DrawStatuses(selected.Manager);
    }

    private void DrawStatuses(LociSM manager)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ScrollbarSize, 10f);
        using var _ = CkRaii.FramedChildPaddedWH("active-statuses", ImGui.GetContentRegionAvail(), 0, SundCol.Gold.Uint());
        if (!_) return;

        if (manager.Statuses.Count is 0)
        {
            CkGui.FontTextCentered("No Statuses Applied", Fonts.Default150Percent, ImGuiColors.DalamudGrey2);
            return;
        }

        // Push the font first so the height is correct.
        var rowSize = new Vector2(_.InnerRegion.X, LociIcon.Size.Y);
        foreach (var (status, idx) in manager.Statuses.ToList().WithIndex())
        {
            ImGui.TableNextColumn();
            using var id = ImRaii.PushId(status.ID);
            using var entry = ImRaii.Group();
            LociIcon.Draw((uint)status.IconID, status.Stacks, LociIcon.Size);
            LociEx.AttachTooltip(status, _manager.SavedStatuses);

            ImGui.SameLine();
            using (Fonts.Default150Percent.Push())
            {
                var adjust = (rowSize.Y - ImUtf8.TextHeight) * 0.5f;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + adjust);
                CkRichText.Text(status.Title, 10);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - adjust);
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - CkGui.IconButtonSize(FAI.TimesCircle).X);
            }
            if (CkGui.IconButton(FAI.Minus, disabled: manager.Ephemeral, inPopup: true))
                manager.Cancel(status.GUID);
            CkGui.AttachToolTip("Remove from manager.");

            if (idx > 1 && idx < manager.Statuses.Count)
                ImGui.Separator();
        }
    }
}
