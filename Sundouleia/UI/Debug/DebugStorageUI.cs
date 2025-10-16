using CkCommons.Gui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public class DebugStorageUI : WindowMediatorSubscriberBase
{
    private readonly RadarManager _radar;
    public DebugStorageUI(ILogger<DebugStorageUI> logger, SundouleiaMediator mediator,
        RadarManager radar) 
        : base(logger, mediator, "Storage Debugger")
    {
        _radar = radar;

        IsOpen = false;
        this.SetBoundaries(new(380, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("Radar Storages"))
        {
            // All Users.
            DrawRadarUsers("All Users", _radar.AllUsers);
            // Rendered Users.
            DrawRadarUsers("Rendered Users", _radar.RenderedUsers);
        }
    }

    private void DrawIconBoolColumn(bool value)
    {
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
    }

    private void DrawRadarUsers(string label, IReadOnlyCollection<RadarUser> radarUsers)
    {
        using var node = ImRaii.TreeNode($"{label}##debug-{label}");
        if (!node) return;

        using (ImRaii.Table($"{label}-debug-table", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Anon. Name");
            ImGui.TableSetupColumn("UnmaskedName");
            ImGui.TableSetupColumn("ValidHash");
            ImGui.TableSetupColumn("Rendered");
            ImGui.TableSetupColumn("PcName");
            ImGui.TableSetupColumn("ObjIdx");
            ImGui.TableHeadersRow();

            foreach (var user in radarUsers)
            {
                ImGui.TableNextColumn();
                CkGui.ColorTextFrameAligned(user.AnonymousName, ImGuiColors.ParsedBlue);
                ImGui.TableNextColumn();
                CkGui.TextFrameAligned(user.UID);
                DrawIconBoolColumn(!string.IsNullOrEmpty(user.HashedIdent));
                DrawIconBoolColumn(user.IsValid);
                ImGui.TableNextColumn();
                CkGui.TextFrameAligned(user.IsValid ? user.PlayerName : "N/A");
                ImGui.TableNextColumn();
                CkGui.TextFrameAligned(user.IsValid ? user.ObjIndex.ToString() : "N/A");
            }
        }
        ImGui.Spacing();
    }
}
