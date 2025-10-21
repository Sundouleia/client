using CkCommons.Gui;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Pairs;
using Sundouleia.Services.Configs;
using Dalamud.Bindings.ImGui;
using System.Collections.Immutable;
using OtterGui.Text;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.Components;

// Will be reworked later as we introduce groups and things.
public class DrawFolderTag : DrawFolderBase
{
    public DrawFolderTag(string id, IImmutableList<DrawUserPair> drawPairs, IImmutableList<Sundesmo> allPairs,
        GroupsConfig config) : base(id, drawPairs, allPairs, config)
    { }

    protected override bool RenderIfEmpty => _id switch
    {
        Constants.CustomOnlineTag => false,
        Constants.CustomOfflineTag => false,
        Constants.CustomVisibleTag => false,
        Constants.CustomAllTag => true,
        _ => true,
    };

    private bool RenderCount => _id switch
    {
        Constants.CustomOnlineTag => false,
        Constants.CustomOfflineTag => false,
        Constants.CustomVisibleTag => false,
        Constants.CustomAllTag => false,
        _ => true
    };

    protected override float DrawIcon()
    {
        var icon = _id switch
        {
            Constants.CustomOnlineTag => FAI.Link,
            Constants.CustomOfflineTag => FAI.Unlink,
            Constants.CustomVisibleTag => FAI.Eye,
            Constants.CustomAllTag => FAI.User,
            _ => FAI.Folder
        };

        ImGui.AlignTextToFramePadding();
        CkGui.IconText(icon);

        if (RenderCount)
        {
            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing with { X = ImGui.GetStyle().ItemSpacing.X / 2f }))
            {
                ImGui.SameLine();
                ImGui.AlignTextToFramePadding();

                ImGui.TextUnformatted($"[{OnlinePairs}]");
            }
            CkGui.AttachToolTip($"{OnlinePairs} online\n{TotalPairs} total");
        }
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    /// <summary> The label for each dropdown folder in the list. </summary>
    protected override void DrawName(float width)
    {
        ImUtf8.TextFrameAligned(_id switch
        {
            Constants.CustomOnlineTag => "Online",
            Constants.CustomOfflineTag => "Offline",
            Constants.CustomVisibleTag => "Visible",
            Constants.CustomAllTag => "Sundesmos",
            _ => _id
        });
    }
}
