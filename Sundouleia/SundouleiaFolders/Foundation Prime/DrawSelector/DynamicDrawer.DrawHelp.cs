using CkCommons.Gui;
using CkCommons.Raii;
using CkCommons.Widgets;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using TerraFX.Interop.Windows;

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
    DragDrop = DragDropFolders | DragDropLeaves,
}

// Handles the current draw state, and inner functions for drawing entities.
// This is also where a majority of customization points are exposed.
public partial class DynamicDrawer<T>
{
    protected bool ShowRootFolder = false;
    // The below functions will be reworked later.
    // Draws out the entire filter row.
    public void DrawFilterRow(float width, int length)
    {
        using (ImRaii.Group())
            DrawSearchBar(width, length);
        PostSearchBar();
    }

    /// <summary>
    ///     Overridable DynamicDrawer 'Header' Element. (Filter Search) <para />
    ///     By default, no additional options are shown outside of the search filter.
    /// </summary>
    protected virtual void DrawSearchBar(float width, int length)
    {
        var tmp = Filter;
        if (FancySearchBar.Draw("Filter", width, ref tmp, string.Empty, length))
        {
            if (string.Equals(tmp, Filter, StringComparison.Ordinal))
                Filter = tmp;
        }
    }

    protected virtual void PostSearchBar()
    { }

    // Generic drawer, used across all of sundouleia's needs.
    protected void DrawAll(DynamicFlags flags)
    {
        // REMEMBER TO MAKE THIS WRAPPED INSIDE OF A UNIQUE CLIPPER!

        // If there are 0 items to draw, return.
        if (_nodeCacheFlat.Count is 0)
            return;

        // Otherwise, Draw based on if we show the root or not.
        if (ShowRootFolder)
            DrawCachedFolderNode(_nodeCache, flags);
        else
            DrawFolderGroupFolders(_nodeCache, flags);
    }

    public void DrawFolder(string folderName, DynamicFlags flags)
    {
        // Locate the item in the dictionary to draw.
        if (!DrawSystem.TryGetFolder(folderName, out var folder))
            return;
        // Otherwise, Draw the folder found within the cache.
        // (Requires revision of cache to do this, return to later)
    }

    // The shell of the drawn structure. Return to this later.
    private void DrawCachedFolderNode(ICachedFolderNode<T> cachedNode, DynamicFlags flags)
    {
        if (cachedNode is CachedFolderGroup<T> cfg)
            DrawCachedFolderNode(cfg, flags);
        else if (cachedNode is CachedFolder<T> cf)
            DrawCachedFolderNode(cf, flags);
    }

    // The shell of the drawn structure. Return to this later.
    protected void DrawCachedFolderNode(CachedFolderGroup<T> cfg, DynamicFlags flags)
    {
        using var id = ImRaii.PushId($"DDS_{Label}_{cfg.Folder.ID}");
        DrawFolderGroupBanner(cfg.Folder, flags, _hoveredNode == cfg.Folder || _selected.Contains(cfg.Folder));
        if (!cfg.Folder.IsOpen)
            return;
        // Draw the children objects.
        using var indent = ImRaii.PushIndent(ImUtf8.FrameHeight);
        DrawFolderGroupFolders(cfg, flags);
    }

    // The shell of the drawn structure. Return to this later.
    protected void DrawCachedFolderNode(CachedFolder<T> cf, DynamicFlags flags)
    {
        using var id = ImRaii.PushId($"DDS_{Label}_{cf.Folder.ID}");
        DrawFolderBanner(cf.Folder, flags, _hoveredNode == cf.Folder || _selected.Contains(cf.Folder));
        if (!cf.Folder.IsOpen)
            return;
        // Draw the children objects.
        using var _ = ImRaii.PushIndent(ImUtf8.FrameHeight + ImUtf8.ItemInnerSpacing.X);
        DrawFolderLeaves(cf, flags);
    }

    // Overridable draw method for the folder display.
    protected virtual void DrawFolderGroupBanner(IDynamicFolderGroup<T> fg, DynamicFlags flags, bool selected)
    {
        var width = CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX();
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : fg.BgColor;
        // Display a framed child with stylizations based on the folders preferences.
        using var _ = CkRaii.FramedChildPaddedW($"dfg_{Label}_{fg.ID}", width, ImUtf8.FrameHeight, bgCol, fg.BorderColor, 5f, 1f);
            DrawFolderGroupBanner(fg, _.InnerRegion, flags);
    }

    // Where we draw the interactions area and the responses to said items, can be customized.
    protected virtual void DrawFolderGroupBanner(IDynamicFolderGroup<T> fg, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{fg.ID}", region);
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

    protected virtual void DrawFolderBannerInner(IDynamicFolder<T> f, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{f.ID}", region);
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
        ClippedDraw(cf.Children, DrawLeafClipped, ImUtf8.FrameHeightSpacing, flags);

        wdl.ChannelsSetCurrent(0); // Background.
        var gradientTL = new Vector2(folderMin.X, folderMax.Y);
        var gradientTR = new Vector2(folderMax.X, ImGui.GetItemRectMax().Y);
        wdl.AddRectFilledMultiColor(gradientTL, gradientTR, ColorHelpers.Fade(cf.Folder.BorderColor, .9f), ColorHelpers.Fade(cf.Folder.BorderColor, .9f), 0, 0);
        wdl.ChannelsMerge();
    }

    // Adapter used by the clipper so we don't allocate a lambda capturing locals each frame.
    private void DrawLeafClipped(IDynamicLeaf<T> leaf, DynamicFlags flags)
        => DrawLeaf(leaf, flags, leaf.Equals(_hoveredNode) || _selected.Contains(leaf));

    protected virtual void DrawLeaf(IDynamicLeaf<T> leaf, DynamicFlags flags, bool selected)
    {
        var size = new Vector2(CkGui.GetWindowContentRegionWidth() - ImGui.GetCursorPosX(), ImUtf8.FrameHeight);
        var bgCol = selected ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : 0;

        using (var _ = CkRaii.Child(Label + leaf.Name, size, bgCol, 5f))
            DrawLeafInner(leaf, _.InnerRegion, flags);
    }

    protected virtual void DrawLeafInner(IDynamicLeaf<T> leaf, Vector2 region, DynamicFlags flags)
    {
        var pos = ImGui.GetCursorPos();
        ImGui.InvisibleButton($"{Label}_node_{leaf.Name}", region);
        HandleInteraction(leaf, flags);

        ImGui.SameLine(pos.X);
        CkGui.TextFrameAligned(leaf.Name);
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

    /// <summary>
    ///     Interaction Handling for nodes. Directing the flow of most updates
    ///     in the DrawSystem. <para />
    ///     <b> Override with Caution, and only if you know what you're doing. </b>
    /// </summary>
    protected void HandleInteraction(IDynamicCollection<T> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;
        var clicked = ImGui.IsItemClicked();
        // Handle Folder Toggle.
        if (flags.HasAny(DynamicFlags.FolderToggle) && clicked)
        {
            DrawSystem.SetOpenState(node, !node.IsOpen);
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

    /// <summary>
    ///     Interaction Handling for nodes. Directing the flow of most updates
    ///     in the DrawSystem. <para />
    ///     <b> Override with Caution, and only if you know what you're doing. </b>
    /// </summary>
    protected virtual void HandleInteraction(IDynamicLeaf<T> node, DynamicFlags flags)
    {
        if (ImGui.IsItemHovered())
            _newHoveredNode = node;
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

    // Special clipped draw just for the DynamicDrawer.
    private void ClippedDraw<I>(IReadOnlyList<I> data, Action<I, DynamicFlags> draw, float lineHeight, DynamicFlags flags)
    {
        using var clipper = ImUtf8.ListClipper(data.Count, lineHeight);
        while (clipper.Step())
        {
            for (var actualRow = clipper.DisplayStart; actualRow < clipper.DisplayEnd; actualRow++)
            {
                if (actualRow >= data.Count)
                    return;

                if (actualRow < 0)
                    continue;

                draw(data[actualRow], flags);
            }
        }
    }
}
