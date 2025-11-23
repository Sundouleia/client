using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.DrawSystem;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public partial class DebugStorageUI : WindowMediatorSubscriberBase
{
    // Old format.
    private readonly GroupsManager _groups;
    private readonly RadarManager _radar;
    private readonly RequestsManager _requests;
    // New format.
    private readonly WhitelistDrawSystem _mainDDS;
    private readonly GroupsDrawSystem _groupsDDS;
    private readonly RadarDrawSystem _radarDDS;
    private readonly RequestsDrawSystem _requestsDDS;

    public DebugStorageUI(
        ILogger<DebugStorageUI> logger, 
        SundouleiaMediator mediator,
        GroupsManager groups, 
        RadarManager radar,
        RequestsManager requests,
        WhitelistDrawSystem mainDDS,
        GroupsDrawSystem groupsDDS,
        RadarDrawSystem radarDDS,
        RequestsDrawSystem requestsDDS
        ) : base(logger, mediator, "Storage Debugger")
    {
        _groups = groups;
        _radar = radar;
        _requests = requests;
        // New format.
        _mainDDS = mainDDS;
        _radarDDS = radarDDS;
        _groupsDDS = groupsDDS;
        _requestsDDS = requestsDDS;

        IsOpen = false;
        this.SetBoundaries(new(380, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        if (ImGui.CollapsingHeader("DynamicDrawSystems (NEW)"))
            DrawDDSDebug();

        if (ImGui.CollapsingHeader("Dynamic Folders (OLD)"))
            DrawOldStorages();

        if (ImGui.CollapsingHeader("Radar Users"))
            DrawRadarUsers();
    }

    private void DrawDDSDebug()
    {
        DrawDDSDebug("Main Whitelist DDS", _mainDDS);
        DrawDDSDebug("Groups DDS", _groupsDDS);
        DrawDDSDebug("Radar DDS", _radarDDS);
        DrawDDSDebug("Requests DDS", _requestsDDS);
    }

    private void DrawOldStorages()
    {
        DrawRequests();
        DrawGroups();
        DrawRadarUsers();
    }

    private void DrawIconBoolColumn(bool value)
    {
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
    }

    private void DrawRequests()
    {
        ImGui.Text("Total Requests:");
        CkGui.ColorTextInline(_requests.TotalRequests.ToString(), ImGuiColors.DalamudViolet);

        CkGui.ColorText("Incoming Requests", ImGuiColors.ParsedPink);
        using (var _ = ImRaii.Table("Incoming-requests-table", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Sender");
            ImGui.TableSetupColumn("Is Temp");
            ImGui.TableSetupColumn("Expired");
            ImGui.TableSetupColumn("Time Sent");
            ImGui.TableSetupColumn("Expire Time");
            ImGui.TableSetupColumn("Time To Respond");
            ImGui.TableSetupColumn("Message");
            ImGui.TableSetupColumn("FromWorld");
            ImGui.TableSetupColumn("FromArea");
            ImGui.TableHeadersRow();

            foreach (var incRequest in _requests.Incoming)
            {
                ImGui.TableNextColumn();
                CkGui.ColorText(incRequest.SenderAnonName, ImGuiColors.TankBlue);
                DrawIconBoolColumn(incRequest.IsTemporaryRequest);
                DrawIconBoolColumn(incRequest.HasExpired);
                ImGui.TableNextColumn();
                ImGui.Text(incRequest.SentTime.ToString("g"));
                ImGui.TableNextColumn();
                ImGui.Text(incRequest.ExpireTime.ToString("g"));
                ImGui.TableNextColumn();
                ImGui.Text(incRequest.TimeToRespond.ToTimeSpanStr());
                ImGui.TableNextColumn();
                ImGui.Text(incRequest.AttachedMessage ?? "N/A");
                DrawIconBoolColumn(incRequest.SentFromWorld((ushort)PlayerData.CurrentWorldId));
                DrawIconBoolColumn(incRequest.SentFromCurrentArea((ushort)PlayerData.CurrentWorldId, PlayerContent.TerritoryID));
            }
        }

        ImGui.Spacing();
        CkGui.ColorText("Pending Requests", ImGuiColors.ParsedPink);
        using (var _ = ImRaii.Table("Pending-requests-table", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Recipient");
            ImGui.TableSetupColumn("Is Temp");
            ImGui.TableSetupColumn("Expired");
            ImGui.TableSetupColumn("Time Sent");
            ImGui.TableSetupColumn("Expire Time");
            ImGui.TableSetupColumn("Time To Respond");
            ImGui.TableSetupColumn("Message");
            ImGui.TableSetupColumn("FromWorld");
            ImGui.TableSetupColumn("FromArea");
            ImGui.TableHeadersRow();
            foreach (var penRequest in _requests.Outgoing)
            {
                ImGui.TableNextColumn();
                CkGui.ColorText(penRequest.RecipientAnonName, ImGuiColors.TankBlue);
                DrawIconBoolColumn(penRequest.IsTemporaryRequest);
                DrawIconBoolColumn(penRequest.HasExpired);
                ImGui.TableNextColumn();
                ImGui.Text(penRequest.SentTime.ToString("g"));
                ImGui.TableNextColumn();
                ImGui.Text(penRequest.ExpireTime.ToString("g"));
                ImGui.TableNextColumn();
                ImGui.Text(penRequest.TimeToRespond.ToTimeSpanStr());
                ImGui.TableNextColumn();
                ImGui.Text(penRequest.AttachedMessage ?? "N/A");
                DrawIconBoolColumn(penRequest.SentFromWorld((ushort)PlayerData.CurrentWorldId));
                DrawIconBoolColumn(penRequest.SentFromCurrentArea((ushort)PlayerData.CurrentWorldId, PlayerContent.TerritoryID));
            }
        }
    }

    private void DrawGroups()
    {
        ImGui.Text("Total Groups:");
        CkGui.ColorTextInline(_groups.Config.Groups.Count.ToString(), ImGuiColors.DalamudViolet);

        using (var _ = ImRaii.Table("Groups-table", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Icon");
            ImGui.TableSetupColumn("Label");
            ImGui.TableSetupColumn("Description");
            ImGui.TableSetupColumn("BorderCol");
            ImGui.TableSetupColumn("ShowIfEmpty");
            ImGui.TableSetupColumn("ShowOffline");
            ImGui.TableSetupColumn("SortOrder");
            ImGui.TableSetupColumn("Linked UIDs");
            ImGui.TableHeadersRow();

            foreach (var group in _groups.Config.Groups)
            {
                ImGui.TableNextColumn();
                CkGui.IconText(group.Icon, group.IconColor);
                ImGui.TableNextColumn();
                CkGui.ColorText(group.Label, group.LabelColor);
                ImGui.TableNextColumn();
                ImGui.Text(group.Description);
                ImGui.TableNextColumn();
                var borderCol = ImGui.ColorConvertU32ToFloat4(group.BorderColor);
                ImGui.ColorEdit4($"##BorderCol-{group.Label}", ref borderCol, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoPicker);
                DrawIconBoolColumn(group.ShowIfEmpty);
                DrawIconBoolColumn(group.ShowOffline);
                ImGui.TableNextColumn();
                ImGui.Text(string.Join(", ", group.SortOrder));
                ImGui.TableNextColumn();
                ImGui.Text(group.LinkedUids.Count.ToString());
            }
        }
    }

    private void DrawRadarUsers()
    {
        ImGui.Text("Total Radar Users:");
        CkGui.ColorTextInline(_radar.RadarUsers.Count.ToString(), ImGuiColors.DalamudViolet);

        using (var _ = ImRaii.Table("All-RadarUsers-table", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Anon. Name");
            ImGui.TableSetupColumn("UnmaskedName");
            ImGui.TableSetupColumn("ValidHash");
            ImGui.TableSetupColumn("Rendered");
            ImGui.TableSetupColumn("PcName");
            ImGui.TableSetupColumn("ObjIdx");
            ImGui.TableHeadersRow();
            foreach (var user in _radar.RadarUsers)
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
    }
}
