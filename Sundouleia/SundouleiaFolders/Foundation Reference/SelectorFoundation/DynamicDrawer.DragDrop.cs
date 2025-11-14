using CkCommons.Gui;
using Dalamud.Interface.Utility.Raii;
using OtterGui;
using OtterGui.Text;
using OtterGui.Text.EndObjects;

namespace Sundouleia.DrawSystem.Selector;

// Drag-Drop Functionality & Helpers.
public partial class DynamicDrawer<T>
{
    // Internal structure for an active drag-drop cache.
    internal class DragDropOperation
    {
        public string ID { get; set; } = string.Empty;
        public List<DynamicDrawSystem<T>.IDynamicEntity> Entities = [];

        public DragDropOperation(string label)
        {
            ID = label;
        }

        public bool IsActive() => ID.Length > 0 && Entities.Count > 0;

        public void UpdateCache(string id, List<DynamicDrawSystem<T>.IDynamicEntity> entities)
        {
            // Ignore if the same id and list count.
            if (ID == id && Entities.Count == entities.Count)
                return;

            Svc.Logger.Verbose($"[DynamicDrawer] Setting drag-drop for ({id}) with {entities.Count} selections.");
            ID = id;
            Entities = entities;
        }

        public void Clear()
        {
            Svc.Logger.Verbose($"[DynamicDrawer] Clearing drag-drop for ({ID}) with {Entities.Count} selections.");
            ID = string.Empty;
            Entities.Clear();
        }
    }

    private DragDropOperation _dragDropCache;

    /// <summary>
    ///     Attaches a Drag-Drop source to the previously drawn item, with the given label. <para />
    ///     Labels are to be defined by the draw function call method.
    /// </summary>
    /// <param name="path"> The Entity being handled as a drag-drop source. </param>
    private void AsDragDropSource(string opLabel, DynamicDrawSystem<T>.IDynamicEntity entity)
    {
        using var source = ImUtf8.DragDropSource();
        if (!source)
            return;

        string id = $"{Label}_{opLabel}";

        // If we fail to set the payload, it implies we have started to move it, so update the cache.
        if (!DragDropSource.SetPayload(id))
        {
            SelectInternal(entity);
            _dragDropCache.UpdateCache(id, [.. _selected]);
        }

        // Customize display text later, maybe allow a custom virtual func for display text or something.
        CkGui.TextFrameAligned(_dragDropCache.Entities.Count == 1
            ? $"Moving {_dragDropCache.Entities.First().Name}..."
            : $"Moving ...\n\t - {string.Join("\n\t - ", _dragDropCache.Entities.Select(i => i.Name))}");
    }

    private void AsDragDropTarget(string opLabel, DynamicDrawSystem<T>.IDynamicEntity entity)
    {
        using var target = ImRaii.DragDropTarget();
        if (!target)
            return;

        string id = $"{Label}_{opLabel}";
        // If we are not dropping the opLabel, or the cache is not active, ignore this.
        if (!ImGuiUtil.IsDropping(id) || !_dragDropCache.IsActive())
            return;

        // Enqueue after this draw-frame the full transfer of all paths.
        _postDrawActions.Enqueue(() =>
        {
            ProcessTransfer(_dragDropCache.Entities, entity);
            _dragDropCache.Clear();
        });
    }

    private void ProcessTransfer(List<DynamicDrawSystem<T>.IDynamicEntity> entities, DynamicDrawSystem<T>.IDynamicEntity target)
    {
        Log.LogDebug($"[DynamicDrawer] Processing drag-drop transfer of {entities.Count} entities to target {target.FullPath}.");
        // We need to handle transfer logic very carefully here, and need to account for certain cases:
        //  - Moved Leaves MUST resolve to another valid folder, and cannot be moved to a folder collection.
        //  - If the target is a folder, and the selection contains 1 or more folders, they must either be
        //    merged together, or declined.
        //  - If the target folder is a folder collection and the payload contains leaves, all the leaves
        //    parent folders must also exist in the moved selection.

        // if the target is a FolderCollection:
        if (target is DynamicDrawSystem<T>.FolderCollection fc)
        {
            // Then we should ensure that all leaves in the selection have parent folders in the selection.
            // If any do not, the transfer will fail.
            if (entities.OfType<DynamicDrawSystem<T>.Leaf>().Any(leaf => !entities.Contains(leaf.Parent)))
            {
                Log.LogWarning("[DynamicDrawer] Drag-drop transfer failed: Not all parent folders of selected leaves exist in selection.");
                return;
            }

            // All Leaves have valid folders in the selection, so we can safely transfer them over to the new parent.
            // (note that when transfer items, in the final version we will not inject the cache with the full _selected items,
            //  as this causes a lot of overhead during transfer. Instead we only grab the top-level nodes of the items, and add those.
            //  This allows for faster, and more accurate, item transfer.)
            foreach (var movedEntity in entities.OfType<DynamicDrawSystem<T>.IDynamicFolder>())
                DrawSystem.Move(movedEntity, fc);

            // No other conditions to satisfy here.
        }
        // If the target was a folder, but we had a FolderCollection in the entities, fail.
        else if (target is DynamicDrawSystem<T>.Folder && entities.OfType<DynamicDrawSystem<T>.FolderCollection>().Any())
            return;

        // The only concerns left are folders -> Folders, and merging.
        // Get the target folder from either the target, or the target leaf's parent.
        if (target switch
        {
            DynamicDrawSystem<T>.Folder f => f,
            DynamicDrawSystem<T>.Leaf l => l.Parent,
            _ => null
        } is not DynamicDrawSystem<T>.Folder targetFolder)
        {
            Log.LogWarning("[DynamicDrawer] Drag-drop transfer failed: Target folder could not be resolved.");
            return;
        }

        // Split the transfer so that it removes selected leaves belonging to folders also in the selection,
        // and leaves that do not have a parent in the selection.
        var movedFolders = entities.OfType<DynamicDrawSystem<T>.Folder>().ToList();
        var movedLeaves = entities.OfType<DynamicDrawSystem<T>.Leaf>().Where(l => !movedFolders.Contains(l.Parent)).ToList();

        // Move all orphaned leaves.
        foreach (var leaf in movedLeaves)
            DrawSystem.Move(leaf, targetFolder);

        // Now merge all folders into the target folder. (might want to make this configurable lol)
        foreach (var folder in movedFolders)
            DrawSystem.Merge(folder, targetFolder);
    }
}
