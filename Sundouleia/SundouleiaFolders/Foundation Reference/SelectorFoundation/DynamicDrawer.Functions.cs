using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Sundouleia.DrawSystem.Selector;

public partial class DynamicDrawer<T>
{
    public static bool OpenRenamePopup(string popupName, ref string newName)
    {
        using ImRaii.IEndObject popup = ImRaii.Popup(popupName);
        if (!popup)
            return false;

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();

        ImGui.SetNextItemWidth(300 * ImGuiHelpers.GlobalScale);
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        bool enterPressed = ImGui.InputTextWithHint("##newName", "Enter New Name...", ref newName, 512, ImGuiInputTextFlags.EnterReturnsTrue);

        if (!enterPressed)
            return false;

        ImGui.CloseCurrentPopup();
        return true;
    }

    /// <summary> Used for buttons and context menu entries. </summary>
    private static void RemovePrioritizedDelegate<TDelegate>(List<(TDelegate, int)> list, TDelegate action) where TDelegate : Delegate
    {
        int idxAction = list.FindIndex(p => p.Item1 == action);
        if (idxAction >= 0)
            list.RemoveAt(idxAction);
    }

    /// <summary> Used for buttons and context menu entries. </summary>
    private static void AddPrioritizedDelegate<TDelegate>(List<(TDelegate, int)> list, TDelegate action, int priority)
        where TDelegate : Delegate
    {
        int idxAction = list.FindIndex(p => p.Item1 == action);
        if (idxAction >= 0)
        {
            if (list[idxAction].Item2 == priority)
                return;

            list.RemoveAt(idxAction);
        }

        int idx = list.FindIndex(p => p.Item2 > priority);
        if (idx < 0)
            list.Add((action, priority));
        else
            list.Insert(idx, (action, priority));
    }

    /// <summary>
    ///     Set the folder collection, and all subfolder / subfolder collections to a given open state. <para />
    ///     Must be executed after the main Draw dur to ID Computation conflicts.
    /// </summary>
    private void ToggleDescendants(DynamicDrawSystem<T>.FolderCollection folderGroup, int stateIdx, bool open)
    {
        folderGroup.SetIsOpen(open);
        // Remove any previously stored states.
        RemoveDescendants(stateIdx);
        // For all folder children inside of the folder collection, set the state.
        foreach (DynamicDrawSystem<T>.IDynamicWriteFolder folder in folderGroup.GetAllFolderDescendants())
            folder.SetIsOpen(open);

        // If we wanted to open it, we should add the descendants to the state.
        if (open)
            AddDescendants(folderGroup, stateIdx);
    }

    /// <summary> Expand all ancestors of a given path, used for when new objects are created. </summary>
    /// <param name="path"> The Path to expand all its ancestors from. </param>
    /// <returns> If any state was changed. </returns>
    /// <remarks> Can only be executed from the main selector window due to ID computation. Handles only ImGui-state. </remarks>
    private bool ExpandAncestors(DynamicDrawSystem<T>.IDynamicEntity entity)
    {
        if (entity is not DynamicDrawSystem<T>.IDynamicWriteFolder wf)
            return false;

        // If the folder or the parent folder is root, nothing to do.
        if (wf.IsRoot || wf.Parent.IsRoot)
            return false;

        // Otherwise, expand the ancestors.
        bool changes = false;
        var parent = wf.Parent;
        while (!parent.IsRoot)
        {
            changes |= !parent.IsOpen;
            parent.SetIsOpen(true);
            parent = parent.Parent;
        }

        return changes;
    }

    /// <summary> Adds or removes descendants of the given folder based on the affected change. </summary>
    /// <param name="folder"> The folder we are adding or removing descendants from. </param>
    private void AddOrRemoveDescendants(DynamicDrawSystem<T>.Folder folder)
    {
        if (folder.IsOpen)
        {
            int idx = _currentIndex;
            _postDrawActions.Enqueue(() => AddDescendants(folder, idx));
        }
        else
        {
            RemoveDescendants(_currentIndex);
        }
    }

    /// <summary> Given the cache-index to a folder, remove its descendants from the cache. </summary>
    /// <param name="parentIndex"> The index of the folder in the cache. -1 indicates the root. </param>
    /// <remarks> Used when folders are collapsed. </remarks>
    private void RemoveDescendants(int parentIndex)
    {
        int start = parentIndex + 1;
        int depth = parentIndex < 0 ? -1 : _cachedState[parentIndex].Depth;
        int end = start;
        for (; end < _cachedState.Count; ++end)
        {
            if (_cachedState[end].Depth <= depth)
                break;
        }

        _cachedState.RemoveRange(start, end - start);
        _currentEnd -= end - start;
    }

    private void AddDescendants(DynamicDrawSystem<T>.FolderCollection fc, int parentIndex)
    {
        byte depth = (byte)(parentIndex == -1 ? 0 : _cachedState[parentIndex].Depth + 1);
        foreach (DynamicDrawSystem<T>.IDynamicFolder folderChild in fc.GetChildren())
        {
            ++parentIndex;
            ApplyFiltersAddInternal(folderChild, ref parentIndex, depth);
        }
    }

    /// <summary> Given a folder and its cache-index, add all its expanded and unfiltered descendants to the cache. </summary>
    /// <param name="f"> the folder to add descendants from. </param>
    /// <param name="parentIndex"> the index of the folder in the cache. -1 indicates the root. </param>
    /// <remarks> Used when folders are expanded. </remarks>
    private void AddDescendants(DynamicDrawSystem<T>.Folder f, int parentIndex)
    {
        byte depth = (byte)(parentIndex == -1 ? 0 : _cachedState[parentIndex].Depth + 1);
        foreach (DynamicDrawSystem<T>.IDynamicLeaf leaf in f.GetChildren())
        {
            ++parentIndex;
            ApplyFiltersAddInternal(leaf, ref parentIndex, depth);
        }
    }
}
