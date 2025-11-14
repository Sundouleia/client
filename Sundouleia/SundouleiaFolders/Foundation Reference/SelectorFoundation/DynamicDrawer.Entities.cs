using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using OtterGui.Text;

namespace Sundouleia.DrawSystem.Selector;

// Handles the current draw state, and inner functions for drawing entities.
// This is also where a majority of customization points are exposed.
public partial class DynamicDrawer<T>
{
    // Managed throughout every draw frame (and why Luna's structure is nicer
    // for non-recursive draws, but again, look into later)
    private int _currentDepth;
    private int _currentIndex;
    private int _currentEnd;

    // I want to unleash hell on this list clipper function.
    private void DrawEntities()
    {
        using var clipper = ImUtf8.ListClipper(_cachedState.Count);
        // Note: I don't fully understand how reliable this is without any defined height,
        // so test as we go. Additionally try and learn what DrawPseudoFolders does, as
        // outside of line drawing assistance I don't see its purpose, and would rather
        // prefer Luna's approach to drawing this kind of hierarchy.
        while (clipper.Step())
        {
            // Set the monitored idx and end idx for the state cache.
            _currentIndex = clipper.DisplayStart;
            // Get the clippers expected end based on our clipped content region.
            _currentEnd = Math.Min(_cachedState.Count, clipper.DisplayEnd);

            // If we are past the expected endpoint, do not draw. (might be a mistake?)
            if (_currentIndex >= _currentEnd)
                continue;

            // For nested elements, draw the pseudo folder lines.
            if (_cachedState[_currentIndex].Depth != 0)
                DrawPseudoFolders();

            // Update the current end.
            _currentEnd = Math.Min(_cachedState.Count, _currentEnd);

            // Draw the hierarchy of this element, and all subsequent elements.
            // (I assume this is for all non-nested elements?... idk this is all turbo confusing lol)
            for (; _currentIndex < _currentEnd; ++_currentIndex)
                DrawEntity(_cachedState[_currentIndex]);
        }
    }

    private (Vector2, Vector2) DrawEntity(EntityState state)
    {
        return state.Entity switch
        {
            DynamicDrawSystem<T>.FolderCollection fc => DrawFolderGroup(fc),
            DynamicDrawSystem<T>.Folder f            => DrawFolder(f),
            DynamicDrawSystem<T>.Leaf l              => DrawLeaf(l),
            _                                        => (Vector2.Zero, Vector2.Zero),
        };
    }


    private void DrawCollectionOuter(DynamicDrawSystem<T>.FolderCollection fc)
    {
        // This WILL break the drawn items in the current implementation.
        // That being said, as we get functional builds, modify this to work correctly with it in place.
        if (!fc.ShowIfEmpty && fc.TotalChildren is 0)
            return;

        // Maybe mark the FolderCollection with an id here?

        // This will not work because it depends on knowing the filtered children of a folder.
    }

    private (Vector2, Vector2) DrawFolderGroup(DynamicDrawSystem<T>.FolderCollection group)
    {
        // blah.
        return (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

    }

    private void DrawFolderGroup(DynamicDrawSystem<T>.FolderCollection group, bool hovered, bool selected
    {

    }



    // Again, I hate this, restructure it later.
    private (Vector2, Vector2) DrawLeaf(DynamicDrawSystem<T>.Leaf leaf)
    {
        var clicked = DrawLeaf(leaf, leaf == SelectedLeaf || Selected.Contains(leaf));
        if (clicked)
            Select(leaf, state);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(leaf.ID.ToString());

        DragDropSource(leaf);
        DragDropTarget(leaf);
        RightClickContext(leaf);
        return (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    // Please find a way to bomb this out of existence I swear to god i hate it so much.
    private void DrawPseudoFolders()
    {
        var first   = _cachedState[_currentIndex]; // The first object drawn during this iteration
        var parents = 3; // had a calculation here, but i dont care, i dont like this structure, i just want to see how it works.
        // Push IDs in order and indent.
        ImGui.Indent(ImGui.GetStyle().IndentSpacing * parents);

        // Get start point for the lines (top of the selector).
        var lineStart = ImGui.GetCursorScreenPos();

        // For each pseudo-parent in reverse order draw its children as usual, starting from _currentIndex.
        for (_currentDepth = parents; _currentDepth > 0; --_currentDepth)
        {
            DrawChildren(lineStart);
            lineStart.X -= ImGui.GetStyle().IndentSpacing;
            ImGui.Unindent();
        }
    }

    /// <summary> Used for clipping. </summary>
    /// <remarks> If we end not on depth 0, check whether to terminate the folder lines or continue them to the screen end. </remarks>
    /// <returns> The adjusted line end. </returns>
    private Vector2 AdjustedLineEnd(Vector2 lineEnd)
    {
        if (_currentIndex != _currentEnd)
            return lineEnd;

        var y = ImGui.GetWindowHeight() + ImGui.GetWindowPos().Y;
        if (y > lineEnd.Y + ImGui.GetTextLineHeight())
            return lineEnd;

        // Continue iterating from the current end.
        for (var idx = _currentEnd; idx < _state.Count; ++idx)
        {
            var state = _state[idx];

            // If we find an object at the same depth, the current folder continues
            // and the line has to go out of the screen.
            if (state.Depth == _currentDepth)
                return lineEnd with { Y = y };

            // If we find an object at a lower depth before reaching current depth,
            // the current folder stops and the line should stop at the last drawn child, too.
            if (state.Depth < _currentDepth)
                return lineEnd;
        }

        // All children are in subfolders of this one, but this folder has no further children on its own.
        return lineEnd;
    }

    /// <summary>
    ///     Draw the children of a folder collection with a given line start using the current index and end. <para />
    ///     Ideally we could phase this out with some better future structure once we have a further
    ///     understanding of Luna but for now this will do.
    /// </summary>
    /// <param name="lineStart"> The start of the folder line. </param>
    private void DrawChildren(Vector2 lineStart)
    {
        // Folder line stuff.
        var offsetX  = -ImGui.GetStyle().IndentSpacing + ImGui.GetTreeNodeToLabelSpacing() / 2;
        var drawList = ImGui.GetWindowDrawList();
        lineStart.X += offsetX;
        lineStart.Y -= 2 * ImGuiHelpers.GlobalScale;
        var lineEnd = lineStart;

        for (; _currentIndex < _currentEnd; ++_currentIndex)
        {
            // If we leave _currentDepth, its not a child of the current folder anymore.
            var state = _state[_currentIndex];
            if (state.Depth != _currentDepth)
                break;

            var lineSize = Math.Max(0, ImGui.GetStyle().IndentSpacing - 9 * ImGuiHelpers.GlobalScale);
            // Draw the child
            var (minRect, maxRect) = DrawEntity(state);
            if (minRect.X == 0)
                continue;

            // if the item is a folder, draw the indent, otherwise draw the full height.
            if (state.Entity is DynamicDrawSystem<T>.Folder folder)
            {
                var midPoint = (minRect.Y + maxRect.Y) / 2f - 1f;
                drawList.AddLine(lineStart with { Y = midPoint }, new Vector2(lineStart.X + lineSize, midPoint), uint.MaxValue, ImGuiHelpers.GlobalScale);
                lineEnd.Y = midPoint;
            }
            else
            {
                lineEnd.Y = maxRect.Y;
            }
        }
        // Finally, draw the folder line.
        drawList.AddLine(lineStart, AdjustedLineEnd(lineEnd), uint.MaxValue, ImGuiHelpers.GlobalScale);
    }

    /// <summary> Draw a folder. Handles drag'n drop, right-click context menus, expanding/collapsing, and selection. </summary>
    /// <param name="folder"> The Folder to draw. </param>
    /// <remarks> If the folder is expanded, draw its children one tier deeper. </remarks>
    /// <returns> The minimum and maximum points of the drawn item. </returns>
    private (Vector2, Vector2) DrawFolder(DynamicDrawSystem<T>.Folder folder)
    {
        var selected = Selected.Contains(folder);
        var clicked = DrawFolder(folder, selected);
        if (clicked)
        {
            // update the state, then add or remove the descendants.
            folder.SetIsOpen(!folder.IsOpen);
            AddOrRemoveDescendants(folder);
        }

        if (AllowMultiSelection && clicked && ImGui.GetIO().KeyCtrl)
            Select(folder);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(folder.ID.ToString());
        DragDropSource(folder);
        DragDropTarget(folder);
        RightClickContext(folder);

        var rect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

        // If the folder is expanded, draw its children one tier deeper.
        if (!folder.IsOpen)
            return rect;

        ++_currentDepth;
        ++_currentIndex;
        ImGui.Indent();
        DrawChildren(ImGui.GetCursorScreenPos());
        ImGui.Unindent();
        --_currentIndex;
        --_currentDepth;

        return rect;
    }

    /// <summary> 
    ///     When right clicking the content region of the selector, where no entities exist, this menu displays. <para />
    ///     For right now, we do not particularly care about this functionality, but it would be nice to have down the line.
    /// </summary>
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

    private void JumpIfRequested()
    {
        if (_jumpToSelection is null)
            return;

        if (_cachedState.FindIndex(_leafCount, s => s.Entity == _jumpToSelection) is int idx && idx >= 0)
            ImGui.SetScrollFromPosY(ImGui.GetTextLineHeightWithSpacing() * idx - ImGui.GetScrollY());

        _jumpToSelection = null;
    }
}
