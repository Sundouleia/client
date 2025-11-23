using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.DrawSystem;
using Sundouleia.Gui.Components;
using Sundouleia.Gui.MainWindow;
using Sundouleia.PlayerClient;
using Sundouleia.Radar;
using Sundouleia.Services.Mediator;
using Sundouleia.Utils;

namespace Sundouleia.Gui;

public partial class DebugStorageUI : WindowMediatorSubscriberBase
{
    // Old format.
    private readonly WhitelistTab _whitelistFolders;
    private readonly RadarTab _radarFolders;
    private readonly RequestsTab _requestFolders;
    private readonly GroupsUI _groupEditorFolders;
    private readonly GroupsManager _groups;
    private readonly RadarManager _radar;
    private readonly RequestsManager _requests;
    // New format.
    private readonly WhitelistDrawSystem _mainDDS;
    private readonly RadarDrawSystem _radarDDS;

    public DebugStorageUI(
        ILogger<DebugStorageUI> logger, 
        SundouleiaMediator mediator,
        WhitelistTab whitelistFolders, 
        RadarTab radarFolders, 
        RequestsTab requestFolders,
        GroupsUI groupEditorFolders, 
        GroupsManager groups, 
        RadarManager radar,
        RequestsManager requests,
        WhitelistDrawSystem mainDDS,
        RadarDrawSystem radarDDS
        ) : base(logger, mediator, "Storage Debugger")
    {
        _whitelistFolders = whitelistFolders;
        _radarFolders = radarFolders;
        _requestFolders = requestFolders;
        _groupEditorFolders = groupEditorFolders;
        _groups = groups;
        _radar = radar;
        _requests = requests;
        // New format.
        _mainDDS = mainDDS;
        _radarDDS = radarDDS;

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
        DrawDDSDebug("Radar DDS", _radarDDS);
    }

    private void DrawOldStorages()
    {
        DrawWhitelistFolders();
        DrawRadarFolders();
        DrawRequestFolders();
        DrawGroupEditorFolders();
        DrawRequests();
        DrawGroups();
        DrawRadarUsers();
    }

    private void DrawIconBoolColumn(bool value)
    {
        ImGui.TableNextColumn();
        CkGui.IconText(value ? FAI.Check : FAI.Times, value ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed);
    }

    private void DrawWhitelistFolders()
    {
        using var _ = ImRaii.TreeNode("Whitelist Folders");
        if (!_) return;

        using (var main = ImRaii.TreeNode("Default Folders"))
        {
            if (main)
            {
                foreach (var folder in _whitelistFolders.MainFolders)
                {
                    if (folder is not DynamicPairFolder pairFolder)
                        continue;
                    // Draw the node.
                    DrawPairFolderNode(pairFolder);
                }
                CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
            }
        }

        using (var group = ImRaii.TreeNode("Group Folders"))
        {
            if (group)
            {
                foreach (var grp in _whitelistFolders.GroupFolders)
                {
                    if (grp is not DynamicPairFolder groupFolder)
                        continue;
                    // Draw the node.
                    DrawPairFolderNode(groupFolder);
                }
                CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
            }
        }

        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
    }

    private void DrawRadarFolders()
    {
        using var _ = ImRaii.TreeNode("Radar Folders");
        if (!_)
            return;

        DrawRadarFolderNode(_radarFolders.Paired);
        DrawRadarFolderNode(_radarFolders.Unpaired);

        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
    }
     
    private void DrawRequestFolders()
    {
        using var _ = ImRaii.TreeNode("Request Folders");
        if (!_)
            return;

        DrawRequestFolderNode(_requestFolders.Incoming);
        DrawRequestFolderNode(_requestFolders.Pending);
    }

    private void DrawGroupEditorFolders()
    {
        using var _ = ImRaii.TreeNode("Group Editor Folders");
        if (!_)
            return;

        foreach (var grp in _groupEditorFolders.Groups)
        {
            if (grp is not DynamicPairFolder groupFolder)
                continue;
            // Draw the node.
            DrawPairFolderNode(groupFolder);
        }

        CkGui.SeparatorSpaced(CkColor.VibrantPink.Uint());
    }

    private void DrawPairFolderNode(DynamicPairFolder folder)
    {
        using var _ = ImRaii.TreeNode($"{folder.Label}##{folder.DistinctId}");
        if (!_)
            return;

        folder.DrawFolder();
        using (var t = ImRaii.Table($"TableOverview-{folder.DistinctId}", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("ID");
            ImGui.TableSetupColumn("Total");
            ImGui.TableSetupColumn("Rendered");
            ImGui.TableSetupColumn("Online");
            ImGui.TableSetupColumn("ShowIfEmpty");
            ImGui.TableSetupColumn("DragDropTarget");
            ImGui.TableSetupColumn("DragDropItems");
            ImGui.TableSetupColumn("MultiSelect");
            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            ImGui.Text(folder.DistinctId);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Total.ToString(), ImGuiColors.TankBlue);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Rendered.ToString(), ImGuiColors.TankBlue);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Online.ToString(), ImGuiColors.TankBlue);
            DrawIconBoolColumn(folder.Options.ShowIfEmpty);
            DrawIconBoolColumn(folder.Options.IsDropTarget);
            DrawIconBoolColumn(folder.Options.DragDropItems);
            DrawIconBoolColumn(folder.Options.MultiSelect);
        }
        using (var t = ImRaii.Table($"DrawEntities-{folder.DistinctId}-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("DisplayName");
            ImGui.TableSetupColumn("Distinct ID");
            ImGui.TableHeadersRow();

            foreach (var item in folder.DrawEntities)
            {
                ImGui.TableNextColumn();
                ImGui.Text(item.EntityId);
                ImGui.TableNextColumn();
                ImGui.Text(item.DisplayName);
                ImGui.TableNextColumn();
                ImGui.Text(item.DistinctId);
                ImGui.TableNextRow();
            }
        }
        ImGui.Separator();
    }

    private void DrawRadarFolderNode(DynamicRadarFolder folder)
    {
        using var _ = ImRaii.TreeNode($"{folder.Label}##{folder.DistinctId}");
        if (!_)
            return;

        folder.DrawFolder();
        using (var t = ImRaii.Table($"TableOverview-{folder.DistinctId}", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("ID");
            ImGui.TableSetupColumn("Total");
            ImGui.TableSetupColumn("Rendered");
            ImGui.TableSetupColumn("Lurkers");
            ImGui.TableSetupColumn("ShowIfEmpty");
            ImGui.TableSetupColumn("DragDropTarget");
            ImGui.TableSetupColumn("DragDropItems");
            ImGui.TableSetupColumn("MultiSelect");
            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            ImGui.Text(folder.DistinctId);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Total.ToString(), ImGuiColors.TankBlue);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Rendered.ToString(), ImGuiColors.TankBlue);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Lurkers.ToString(), ImGuiColors.TankBlue);
            DrawIconBoolColumn(folder.Options.ShowIfEmpty);
            DrawIconBoolColumn(folder.Options.IsDropTarget);
            DrawIconBoolColumn(folder.Options.DragDropItems);
            DrawIconBoolColumn(folder.Options.MultiSelect);
        }

        ImGui.Separator();
        using (var t = ImRaii.Table($"DrawEntities-{folder.DistinctId}-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Distinct ID");
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("DisplayName");
            ImGui.TableHeadersRow();

            foreach (var item in folder.DrawEntities)
            {
                ImGui.TableNextColumn();
                ImGui.Text(item.DistinctId);
                ImGui.TableNextColumn();
                ImGui.Text(item.EntityId);
                ImGui.TableNextColumn();
                ImGui.Text(item.DisplayName);
                ImGui.TableNextRow();
            }
        }
    }

    private void DrawRequestFolderNode(DynamicRequestFolder folder)
    {
        using var _ = ImRaii.TreeNode($"{folder.Label}##{folder.DistinctId}");
        if (!_)
            return;

        folder.DrawFolder();
        using (var t = ImRaii.Table($"TableOverview-{folder.DistinctId}", 8, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("ID");
            ImGui.TableSetupColumn("Total");
            ImGui.TableSetupColumn("ShowIfEmpty");
            ImGui.TableSetupColumn("DragDropTarget");
            ImGui.TableSetupColumn("DragDropItems");
            ImGui.TableSetupColumn("MultiSelect");
            ImGui.TableHeadersRow();

            ImGui.TableNextColumn();
            ImGui.Text(folder.DistinctId);
            ImGui.TableNextColumn();
            CkGui.ColorText(folder.Total.ToString(), ImGuiColors.TankBlue);
            DrawIconBoolColumn(folder.Options.ShowIfEmpty);
            DrawIconBoolColumn(folder.Options.IsDropTarget);
            DrawIconBoolColumn(folder.Options.DragDropItems);
            DrawIconBoolColumn(folder.Options.MultiSelect);
        }

        using (var t = ImRaii.Table($"DrawEntities-{folder.DistinctId}-table", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Distinct ID");
            ImGui.TableSetupColumn("UID");
            ImGui.TableSetupColumn("DisplayName");
            ImGui.TableHeadersRow();

            foreach (var item in folder.DrawEntities)
            {
                ImGui.TableNextColumn();
                ImGui.Text(item.DistinctId);
                ImGui.TableNextColumn();
                ImGui.Text(item.EntityId);
                ImGui.TableNextColumn();
                ImGui.Text(item.DisplayName);
                ImGui.TableNextRow();
            }
        }
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
