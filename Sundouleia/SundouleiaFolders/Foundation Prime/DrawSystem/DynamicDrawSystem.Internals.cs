namespace Sundouleia.DrawSystem;

// the internal, private operations for the dynamic file system.
public partial class DynamicDrawSystem<T>
{
    protected enum Result
    {
        Success,
        SuccessNothingDone,
        InvalidOperation,
        ItemExists,
        PartialSuccess,
        CircularReference,
        NoSuccess,
    }

    /// <summary>
    ///     Try to rename an entity in the dynamic file system, 
    ///     while also fixing any invalid symbols in the new name.
    /// </summary>
    /// <param name="leaf"> The entity to rename. </param>
    /// <param name="newName"> The new name to assign to the leaf. </param>
    private Result RenameLeaf(DynamicLeaf<T> leaf, string newName)
    {
        // Prevent allowing a Leaf to behave as Root.
        if (string.IsNullOrEmpty(leaf.Name) || string.IsNullOrEmpty(newName))
            return Result.InvalidOperation;

        // correct the newName to work with the dynamic file system.
        newName = newName.FixName();
        // if the names are identical just return that nothing was done.
        if (newName == leaf.Name)
            return Result.SuccessNothingDone;

        // an entity with the same name already exists, so fail it.
        // (if we run into edge cases with this we can just mimic OtterGui FileSystem)
        if (Search(leaf.Parent, newName) >= 0)
            return Result.ItemExists;

        // Otherwise, rename the child and return success.
        leaf.SetName(newName, false);
        return Result.Success;
    }

    private Result RenameFolder(IDynamicCollection<T> folder, string newName)
    {
        // Prevent allowing a folder to behave as Root.
        if (string.IsNullOrEmpty(folder.Name) || string.IsNullOrEmpty(newName))
            return Result.InvalidOperation;

        // correct the newName to work with the DFS.
        newName = newName.FixName();

        // Return early if names are identical.
        if (newName == folder.Name)
            return Result.SuccessNothingDone;

        if (folder is DynamicFolderGroup<T> fc)
        {
            if (Search(fc.Parent, newName) >= 0)
                return Result.ItemExists;
            fc.SetName(newName, false);
            fc.Parent.SortChildren(_nameComparer);
            return Result.Success;
        }
        else if (folder is DynamicFolder<T> f)
        {
            if (Search(f.Parent, newName) >= 0)
                return Result.ItemExists;
            f.SetName(newName, false);
            f.Parent.SortChildren(_nameComparer);
            return Result.Success;
        }
        else
        {
            return Result.InvalidOperation;
        }
    }

    // Try to move a leaf to a new parent Folder, and renaming it if requested.
    // Concrete to folders because leaf entities can only exist under Folder entities.
    // Try to phase out the idx.
    private Result MoveFolder(IDynamicCollection<T> folder, DynamicFolderGroup<T> newParent, out DynamicFolderGroup<T> oldParent, string? newName = null)
    {
        // store the current old parent of the leaf.
        oldParent = folder.Parent;
        // If the parents are the same, return either that nothing we done, or perform a rename.
        if (newParent == oldParent)
            return newName == null ? Result.SuccessNothingDone : RenameFolder(folder, newName);

        // obtain the true newName.
        var actualNewName = newName?.FixName() ?? folder.Name;
        // prevent the move if anything under the new folder contains a leaf with the same name.
        if (Search(newParent, actualNewName) >= 0)
            return Result.ItemExists;

        // Otherwise the move operation is valid, so remove the folder from the old FolderCollection, and assign it to the new one.
        oldParent.Children.Remove(folder);
        if (folder is DynamicFolderGroup<T> fc)
        {
            fc.SetName(actualNewName, false);
            AssignFolder(newParent, fc);
            return Result.Success;
        }
        else if (folder is DynamicFolder<T> f)
        {
            f.SetName(actualNewName, false);
            AssignFolder(newParent, f);
            return Result.Success;
        }
        else
        {
            return Result.InvalidOperation;
        }
    }


    /// <summary>
    ///     Creates all folders to the end, automatically excluding leaf paths from assignment.
    /// </summary>
    /// <param name="fullPath"> the entity's FullPath variable, excluding root. </param>
    /// <param name="topFolder"> the topmost available folder or folder collection in the path.</param>
    /// <returns> The result of this function. </returns>
    private Result CreateAllGroups(string fullPath, out DynamicFolderGroup<T> topFolder)
    {
        topFolder = root;
        Result res = Result.SuccessNothingDone;

        string[] parts = fullPath.Split("//", StringSplitOptions.RemoveEmptyEntries);
        // Process each part of the path, creating a folder group for each, branching out from root.
        foreach (var segment in parts)
        {
            var folder = new DynamicFolderGroup<T>(topFolder, FAI.FolderTree, segment, idCounter + 1u);
            // If we were able to successfully assign the folder, then update topFolder and res.
            if (AssignFolder(topFolder, folder) is Result.ItemExists)
            {
                // If it already exists, then we can skip incrementing the id and just update topFolder.
                topFolder = folder;
            }
            // Otherwise, it is a new folder to be added, so inc ID, update res, and set topFolder.
            else
            {
                ++idCounter;
                res = Result.Success;
                topFolder = folder;
            }
        }

        // Return the final result.
        return res;
    }

    private Result AssignFolder(DynamicFolderGroup<T> parent, DynamicFolder<T> folder)
    {
        if (Search(parent, folder.Name) >= 0)
            return Result.ItemExists;
        // Otherwise, assign it.
        parent.Children.Add(folder);
        folder.Parent = parent;
        folder.UpdateFullPath();
        parent.SortChildren(_nameComparer);
        return Result.Success;
    }

    private Result AssignFolder(DynamicFolderGroup<T> parent, DynamicFolderGroup<T> folder)
    {
        if (Search(parent, folder.Name) >= 0)
            return Result.ItemExists;
        // Otherwise, assign it.
        parent.Children.Add(folder);
        folder.Parent = parent;
        folder.UpdateFullPath();
        parent.SortChildren(_nameComparer);
        return Result.Success;
    }

    private Result RemoveFolder(IDynamicCollection<T> folder)
        => folder switch
        {
            DynamicFolderGroup<T> fg => RemoveFolder(fg),
            DynamicFolder<T> f => RemoveFolder(f),
            _ => Result.InvalidOperation
        };

    private Result RemoveFolder(DynamicFolder<T> folder)
    {
        var idx = Search(folder.Parent, folder.Name);
        if (idx < 0)
            return Result.SuccessNothingDone;
        folder.Parent.Children.RemoveAt(idx);
        // Remove it from the mapping.
        _folderMap.Remove(folder.Name);
        // Return successful removal.
        return Result.Success;
    }

    private Result RemoveFolder(DynamicFolderGroup<T> folder)
    {
        if (folder.IsRoot)
            return Result.InvalidOperation;

        var idx = Search(folder.Parent, folder.Name);
        if (idx < 0)
            return Result.SuccessNothingDone;
        // Remove the folder from the parent & map.
        folder.Parent.Children.RemoveAt(idx);
        _folderMap.Remove(folder.Name);
        // Maybe move all child folders up to the current folder
        // location, (TODO)
        return Result.Success;
    }

    /// <summary>
    ///     Abstract method for folder merging used typically by drag-drop or dissolve operations. <para />
    ///     Because the folders are abstract, their merge method must also be defined in the 
    ///     DrawSystem parent, so that by the time MergeFolders is finished, the next refresh 
    ///     of <paramref name="to"/> will contain the items in <paramref name="from"/>
    /// </summary>
    /// <param name="from"> the folder being merged. </param>
    /// <param name="to"> the folder all items should be in after the function call. </param>
    protected virtual Result MergeFolders(DynamicFolder<T> from, DynamicFolder<T> to)
        => Result.Success; // Make abstract after initial testing.

    //private Result MergeFolders(DynamicFolder<T> from, DynamicFolder<T> to)
    //{
    //    // if the collections are the same, fail.
    //    if (from == to)
    //        return Result.SuccessNothingDone;
    //    // If we are moving from root, fail.
    //    if (from.Name.Length is 0)
    //        return Result.InvalidOperation;

    //    // Otherwise we can proceed to merge.
    //    var result = from.TotalChildren is 0 ? Result.Success : Result.NoSuccess;
    //    // iterate through the children and move them.
    //    for (var i = 0; i < from.TotalChildren;)
    //    {
    //        var moveRet = MoveLeaf(from.Children[i], to, out _);
    //        // If the move was successful,
    //        if (moveRet is Result.Success)
    //        {
    //            // If previous result was NoSuccess, check if this is the first item
    //            if (result == Result.NoSuccess)
    //                result = (i == 0) ? Result.Success : Result.PartialSuccess;
    //            // Otherwise keep the existing result if not (should be partial success)

    //            // We moved a child folder out of the list we are iterating through, so dont inc i.
    //            // Next child is at current index
    //        }
    //        else
    //        {
    //            // Move failed, increment index
    //            i++;
    //            // Bump success down to partial success, if we were currently set to success.
    //            if (result is Result.Success)
    //                result = Result.PartialSuccess;
    //        }
    //    }

    //    // return the final result. If it was successful, remove the old folder.
    //    if (result is Result.Success)
    //        RemoveFolder(from);

    //    return result;
    //}


    /// <summary> 
    ///     Try to merge all children <paramref name="from"/> into <paramref name="to"/>.
    /// </summary>
    private Result MergeFolders(DynamicFolderGroup<T> from, DynamicFolderGroup<T> to)
    {
        // if the collections are the same, fail.
        if (from == to)
            return Result.SuccessNothingDone;
        // If we are moving from root, fail.
        if (from.Name.Length is 0)
            return Result.InvalidOperation;
        // If a circular reference exists, fail.
        if (!HasValidHeritage(to, from))
            return Result.CircularReference;

        // Otherwise we can proceed to merge.
        var result = from.TotalChildren is 0 ? Result.Success : Result.NoSuccess;
        // iterate through the children and move them.
        for (var i = 0; i < from.TotalChildren;)
        {
            var moveRet = MoveFolder(from.Children[i], to, out _);
            // If the move was successful,
            if (moveRet is Result.Success)
            {
                // If previous result was NoSuccess, check if this is the first item
                if (result == Result.NoSuccess)
                    result = (i == 0) ? Result.Success : Result.PartialSuccess;
                // Otherwise keep the existing result if not (should be partial success)

                // We moved a child folder out of the list we are iterating through, so dont inc i.
                // Next child is at current index
            }
            else
            {
                // Move failed, increment index
                i++;
                // Bump success down to partial success, if we were currently set to success.
                if (result is Result.Success)
                    result = Result.PartialSuccess;
            }
        }

        // return the final result. If it was successful, remove the old folder.
        if (result is Result.Success)
            RemoveFolder(from);

        return result;
    }

    // Catch potential circular references in relationships.
    // Returns true if potentialParent is not anywhere up the tree from child, false otherwise.
    private static bool HasValidHeritage(DynamicFolderGroup<T> potentialParent, IDynamicCollection<T> folder)
    {
        var parent = potentialParent;
        // Root is an empty string.
        while (parent.Name.Length > 0)
        {
            if (parent == folder)
                return false;

            parent = parent.Parent;
        }

        return true;
    }

    public enum SegmentType
    {
        FolderCollection,
        Folder,
        Leaf
    }

    public static List<(string Name, SegmentType Type)> ParseSegments(string path)
    {
        var segments = new List<(string Name, SegmentType Type)>();
        if (path.Length is 0)
            return segments;

        SegmentType lastType = SegmentType.FolderCollection; // Root is always FolderCollection

        while (path.Length > 0)
        {
            int idxSingle = path.IndexOf('/');
            bool isDouble = idxSingle >= 0 && idxSingle + 1 < path.Length && path[idxSingle + 1] == '/';
            int sepIndex = idxSingle;

            string segment;
            if (sepIndex < 0)
            {
                segment = path;
                path = string.Empty;
            }
            else
            {
                segment = path[..sepIndex];
                path = path[(sepIndex + (isDouble ? 2 : 1))..].TrimStart();
            }

            segment = segment.Trim();
            if (segment.Length is 0)
                continue;

            // Determine type based on previous segment
            SegmentType type = lastType switch
            {
                SegmentType.FolderCollection => isDouble ? SegmentType.FolderCollection : SegmentType.Folder,
                SegmentType.Folder => SegmentType.Folder,
                _ => SegmentType.FolderCollection
            };

            segments.Add((segment, type));
            lastType = type;

            if (isDouble && segments.Count >= 1)
                segments[^1] = (segments[^1].Name, SegmentType.FolderCollection);
        }

        // Mark last as Leaf if parent is Folder
        if (segments.Count >= 2)
        {
            var parent = segments[^2];
            var last = segments[^1];
            if (parent.Type == SegmentType.Folder && last.Type != SegmentType.FolderCollection)
                segments[^1] = (last.Name, SegmentType.Leaf);
        }

        return segments;
    }
}