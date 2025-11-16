using CkCommons.Gui;
using CkCommons.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;

namespace Sundouleia.DrawSystem.Selector;

[Flags]
public enum DynamicFlags : short
{
    None                = 0 << 0, // No behaviors are set for this draw.
    FolderToggle        = 1 << 0, // Folder Expand/Collapse is allowed.
    SelectableFolders   = 1 << 1, // Folder Single-Select is allowed.
    SelectableLeaves    = 1 << 2, // Leaf Single-Select is allowed.
    MultiSelect         = 1 << 3, // Multi-Selection (anchoring / CTRL) is allowed.
    RangeSelect         = 1 << 4, // Range Selection (SHIFT) is allowed.
    DragDropFolders     = 1 << 5, // Folder Drag-Drop is allowed.
    DragDropLeaves      = 1 << 6, // Leaf Drag-Drop is allowed.
    CopyDrag            = 1 << 7, // Drag-Drop performs copy on the dragged items over a move.
    TrashDrop           = 1 << 8, // Drag-Drop removes the source items on drop into another target, instead of moving.

    // Masks
    BasicViewFolder = FolderToggle, // Maybe add multi-select for bulk permission setting?
    RequestsList = FolderToggle | SelectableLeaves | MultiSelect | RangeSelect,
    GroupArranger = FolderToggle | SelectableFolders | MultiSelect | RangeSelect | DragDropFolders,
    GroupEditor = FolderToggle | SelectableLeaves | MultiSelect | RangeSelect | DragDropLeaves,
    AllEditor = FolderToggle | SelectableLeaves | MultiSelect | RangeSelect | DragDropLeaves | CopyDrag | TrashDrop,

}

// Handles the current draw state, and inner functions for drawing entities.
// This is also where a majority of customization points are exposed.
public partial class DynamicDrawer<T>
{
    /// <summary> If the actual Folder part of Root should be visible. (Children show regardless) </summary>
    protected virtual bool ShowRootFolder => false;
 
    // Generic drawer, used across all of sundouleia's needs.
    public void DrawAll(DynamicFlags flags = DynamicFlags.BasicViewFolder)
    {
        // Note that, it is very, very possible that all of this could go horribly wrong with nested clipping,
        // so we will definitely need to experiment a bit with optimizing where to place our clippers,
        // and how to deal with them appropriately.

        // Definitely Rework this:
        if (_nodeCache.Children.Count is 0)
            return;

        // Worry about clipping later. For now just get the damn thing to display.
        DrawCachedFolderNode(_nodeCache, flags);
    }

    private void DrawCachedFolderNode(ICachedFolderNode<T> cachedNode, DynamicFlags flags)
    {
        if (cachedNode is CachedFolderGroup<T> cfg)
            DrawCachedFolderNode(cfg, flags);
        else if (cachedNode is CachedFolder<T> cf)
            DrawCachedFolderNode(cf, flags);
    }

    // Cached shells define the structure for how items are to be drawn, in which order.
    // This part of the dynamic display cannot be overridden.
    protected void DrawCachedFolderNode(CachedFolderGroup<T> cfg, DynamicFlags flags)
    {
        DrawFolderGroupBanner(cfg.Folder, flags, _hoveredNode == cfg.Folder || _selected.Contains(cfg.Folder));
        if (!cfg.Folder.IsOpen)
            return;
        // Draw the children objects.
        using var indent = ImRaii.PushIndent(ImUtf8.FrameHeight);
        DrawFolderGroupFolders(cfg, flags);
    }

    protected void DrawCachedFolderNode(CachedFolder<T> cf, DynamicFlags flags)
    {
        DrawFolderBanner(cf.Folder, flags, _hoveredNode == cf.Folder || _selected.Contains(cf.Folder));
        if (!cf.Folder.IsOpen)
            return;
        // Draw the children objects.
        using var _ = ImRaii.PushIndent(ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X);
        DrawFolderLeaves(cf, flags);
    }

    protected virtual void DrawFolderGroupBanner(IDynamicFolderGroup<T> fg, DynamicFlags flags, bool selected)
    {
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = _hoveredNode == fg ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : fg.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"dfg_{Label}_{fg.ID}", width, ImUtf8.FrameHeight, bgCol, fg.BorderColor, 5f, 1f);
            DrawFolderGroupBanner(fg, _.InnerRegion, flags);
    }

    // Where we draw the interactions area and the responses to said items, can be customized.
    protected virtual void DrawFolderGroupBanner(IDynamicFolderGroup<T> fg, Vector2 innerRegion, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{fg.ID}", innerRegion);
        HandleInteraction(fg, flags);

        // Back to the start of the line, then draw the folder display contents.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(fg.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(fg.Icon, fg.IconColor);
        CkGui.ColorTextFrameAlignedInline(fg.Name, fg.NameColor);
    }

    // By default, no distinct stylization is applied, but this can be customized.
    protected virtual void DrawFolderGroupFolders(CachedFolderGroup<T> cfg, DynamicFlags flags)
    {
        // Could do a clipped list, but for now will just do a for-each loop.
        foreach (var child in cfg.Children)
            DrawCachedFolderNode(child, flags);
    }

    // Might merge outer and inner if too difficult to navigate internals later.
    protected virtual void DrawFolderBanner(IDynamicFolder<T> f, DynamicFlags flags, bool selected)
    {
        // We could likely reduce this by a lot if we had a override for this clipped draw within the dynamic draw system.
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : f.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"df_{Label}_{f.ID}", width, ImUtf8.FrameHeight, bgCol, f.BorderColor, 5f, 1f);
            DrawFolderBannerInner(f, _.InnerRegion, flags);
    }

    protected virtual void DrawFolderBannerInner(IDynamicFolder<T> f, Vector2 innerRegion, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{f.ID}", innerRegion);
        HandleInteraction(f, flags);
        // Back to the start of the line, then draw the folder display contents.
        ImGui.SameLine(pos.X);
        CkGui.FramedIconText(f.IsOpen ? FAI.CaretDown : FAI.CaretRight);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        CkGui.IconText(f.Icon, f.IconColor);
        CkGui.ColorTextFrameAlignedInline(f.Name, f.NameColor);
    }

    // Outer, customization point for styling.
    protected virtual void DrawFolderLeaves(CachedFolder<T> cf, DynamicFlags flags)
    {
        var folderMin = ImGui.GetItemRectMin();
        var folderMax = ImGui.GetItemRectMax();
        var wdl = ImGui.GetWindowDrawList();
        wdl.ChannelsSplit(2);
        wdl.ChannelsSetCurrent(1);

        // Should make this have variable heights later.
        ImGuiClip.ClippedDraw(cf.Children, (leaf) => DrawLeaf(leaf, flags, _hoveredNode == leaf || _selected.Contains(leaf)), ImUtf8.FrameHeightSpacing);

        wdl.ChannelsSetCurrent(0); // Background.
        var gradientTL = new Vector2(folderMin.X, folderMax.Y);
        var gradientTR = new Vector2(folderMax.X, ImGui.GetItemRectMax().Y);
        wdl.AddRectFilledMultiColor(gradientTL, gradientTR, ColorHelpers.Fade(cf.Folder.BorderColor, .9f), ColorHelpers.Fade(cf.Folder.BorderColor, .9f), 0, 0);
        wdl.ChannelsMerge();
    }

    protected virtual void DrawLeaf(IDynamicLeaf<T> leaf, DynamicFlags flags, bool selected)
    {
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;

        using (var _ = CkRaii.Child(Label + leaf.Name, size, bgCol, 5f))
        {
            var pos = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"{Label}_node_{leaf.ID}", _.InnerRegion);
            HandleInteraction(leaf, flags);

            ImGui.SameLine(pos.X);
            CkGui.TextFrameAligned(leaf.Name);
        }
    }

    private void HandleMainContextActions()
    {
        //const string mainContext = "MainContext";
        //if (!ImGui.IsAnyItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
        //{
        //    if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        //        ImGui.SetWindowFocus(Label);
        //    ImGui.OpenPopup(mainContext);
        //}

        //using var pop = ImRaii.Popup(mainContext);
        //if (!pop)
        //    return;

        //RightClickMainContext();
    }

    // Interaction Handling.
    protected void HandleInteraction(IDynamicCollection<T> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _hoveredNode = node;
        var clicked = ImGui.IsItemClicked();
        // Handle Folder Toggle.
        if (flags.HasAny(DynamicFlags.FolderToggle) && clicked)
        {
            DrawSystem.SetFolderOpenState(node, !node.IsOpen);
            // Might want to append / remove the descendants here after changing the state.
        }
        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableFolders) && clicked)
            SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));

        // Handle Drag and Drop.
        if (flags.HasAny(DynamicFlags.DragDropFolders))
        {
            AsDragDropSource(node);
            AsDragDropTarget(node);
        }
    }

    protected void HandleInteraction(IDynamicLeaf<T> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _hoveredNode = node;
        // Handle Selection.
        if (flags.HasAny(DynamicFlags.SelectableLeaves) && ImGui.IsItemClicked())
            SelectItem(node, flags.HasFlag(DynamicFlags.MultiSelect), flags.HasFlag(DynamicFlags.RangeSelect));
        // Handle Drag and Drop.
        if (flags.HasAny(DynamicFlags.DragDropLeaves))
        {
            AsDragDropSource(node);
            AsDragDropTarget(node);
        }
    }
}
