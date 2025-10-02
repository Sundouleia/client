using CkCommons;
using CkCommons.Gui;
using CkCommons.Helpers;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Services.Mediator;
using Dalamud.Bindings.ImGui;
using Microsoft.IdentityModel.Tokens;
using OtterGui;
using OtterGui.Extensions;
using Sundouleia.Utils;
using SundouleiaAPI.Network;

namespace Sundouleia.Gui;

public class DebugStorageUI : WindowMediatorSubscriberBase
{
    // Will need to add debuggers for all of our new storage container types in sundouleia here eventually.
    public DebugStorageUI(ILogger<DebugStorageUI> logger, SundouleiaMediator mediator)
        : base(logger, mediator, "Config / Storage Debugger")
    {
        
        IsOpen = false;
        this.SetBoundaries(new(380, 400), ImGui.GetIO().DisplaySize);
    }

    protected override void PreDrawInternal()
    { }

    protected override void PostDrawInternal()
    { }

    protected override void DrawInternal()
    {
        // Draw Requests
        // DrawUserRequests("Incoming Requests", _clientData.ReqPairIncoming);
        // DrawUserRequests("Outgoing Requests", _clientData.ReqPairOutgoing);

        ImGui.Separator();
        // Draw loaded config storage data ext.
        CkGui.CenterColorTextAligned("STORAGE DATA DEBUG WIP", ImGuiColors.DalamudViolet);
    }

    private void DrawUserRequests(string treeLabel, IEnumerable<PendingRequest> requests)
    {
        using var node = ImRaii.TreeNode(treeLabel);
        if (!node) return;

        using (ImRaii.Table(treeLabel + "table", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGuiUtil.DrawTableColumn("User");
            ImGuiUtil.DrawTableColumn("Recipient User");
            ImGuiUtil.DrawTableColumn("Creation Time");
            ImGuiUtil.DrawTableColumn("Attached Message");
            foreach (var req in requests)
            {
                ImGui.TableNextRow();
                ImGuiUtil.DrawTableColumn(req.User.UID.ToString());
                ImGuiUtil.DrawTableColumn(req.Target.UID.ToString());
                ImGuiUtil.DrawTableColumn(req.CreatedAt.ToString());
                ImGuiUtil.DrawTableColumn(req.Message.ToString());
            }
        }
        ImGui.Spacing();
    }
}
