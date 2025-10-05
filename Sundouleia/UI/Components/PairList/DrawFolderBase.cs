using CkCommons.Gui;
using Dalamud.Interface.Utility.Raii;
using Sundouleia.Pairs;
using Sundouleia.Services.Configs;
using Dalamud.Bindings.ImGui;
using System.Collections.Immutable;
using CkCommons.Raii;
using Sundouleia.PlayerClient;

namespace Sundouleia.Gui.Components;

/// <summary> The base for the draw folder, a dropdown section in the list of paired users </summary>
public abstract class DrawFolderBase : IDrawFolder
{
    public IImmutableList<DrawUserPair> DrawPairs { get; init; }
    protected readonly string _id;
    protected readonly IImmutableList<Sundesmo> _allPairs;
    protected readonly GroupsConfig _config;

    private bool _wasHovered = false;
    public int OnlinePairs => DrawPairs.Count(u => u.Pair.IsOnline);
    public int TotalPairs => _allPairs.Count;
    protected DrawFolderBase(string id, IImmutableList<DrawUserPair> drawPairs,
        IImmutableList<Sundesmo> allPairs, GroupsConfig config)
    {
        _id = id;
        DrawPairs = drawPairs;
        _allPairs = allPairs;
        _config = config;
    }

    protected abstract bool RenderIfEmpty { get; }

    public void Draw()
    {
        if (!RenderIfEmpty && !DrawPairs.Any())
            return;

        var expanded = _config.IsDefaultExpanded(_id);
        using var id = ImRaii.PushId("folder_" + _id);
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImGui.GetFrameHeight());
        using (CkRaii.Child("folder__" + _id, size, _wasHovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0, 0f))
        {
            CkGui.FramedIconText(expanded ? FAI.CaretDown : FAI.CaretRight);

            ImGui.SameLine();
            var leftSideEnd = DrawIcon();

            ImGui.SameLine();
            var rightSideStart = ImGui.GetWindowContentRegionMin().X + CkGui.GetWindowContentRegionWidth() - ImGui.GetStyle().ItemSpacing.X;

            // draw name
            ImGui.SameLine(leftSideEnd);
            DrawName(rightSideStart - leftSideEnd);
        }
        _wasHovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
            _config.ToggleDefaultFolder(_id);

        ImGui.Separator();
        if (!expanded)
            return;
        // if opened draw content
        using var indent = ImRaii.PushIndent(CkGui.IconSize(FAI.EllipsisV).X + ImGui.GetStyle().ItemSpacing.X, false);
        foreach (var item in DrawPairs)
            item.DrawPairedClient();
        ImGui.Separator();
    }

    protected abstract float DrawIcon();

    protected abstract void DrawName(float width);
}
