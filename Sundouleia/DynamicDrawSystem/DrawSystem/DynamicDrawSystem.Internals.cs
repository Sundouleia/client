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

    // Refactor later?...
    //private Result RenameLeaf(DynamicLeaf<T> leaf, string newName)
    //{
    //    // Prevent allowing a Leaf to behave as Root.
    //    if (string.IsNullOrEmpty(leaf.Name) || string.IsNullOrEmpty(newName))
    //        return Result.InvalidOperation;

    //    // correct the newName to work with the dynamic file system.
    //    newName = newName.FixName();
    //    // if the names are identical just return that nothing was done.
    //    if (newName == leaf.Name)
    //        return Result.SuccessNothingDone;

    //    // an entity with the same name already exists, so fail it.
    //    // (if we run into edge cases with this we can just mimic OtterGui FileSystem)
    //    if (Search(leaf.Parent, newName) >= 0)
    //        return Result.ItemExists;

    //    // Otherwise, rename the child and return success.
    //    leaf.SetName(newName, false);
    //    return Result.Success;
    //}

    /// <summary>
    ///     Internally rename a Folder or FolderGroup to a new name. <para />
    ///     Fails if the new name is invalid, or the name exists in the drawSystem already.
    /// </summary>
    /// <param name="folder"> the folder to rename. </param>
    /// <param name="newName"> the new name. </param>
    private Result RenameFolder(IDynamicCollection<T> folder, string newName)
    {
        // Prevent renaming to root.
        if (string.IsNullOrEmpty(newName))
            return Result.InvalidOperation;

        var fixedNewName = newName.FixName();

        // If the name is no different, return success with nothing done.
        if (fixedNewName == folder.Name)
            return Result.SuccessNothingDone;

        // Mark the item as existing if it is present in the drawSystem already.
        if (_folderMap.ContainsKey(fixedNewName))
            return Result.ItemExists;

        // Otherwise, update the name of the folder.
        // (Do not sort, leave that to the folder's AutoSort function)
        if (folder is DynamicFolderGroup<T> fc)
        {
            fc.SetName(newName, false);
            return Result.Success;
        }
        // works on any folder kind becuz abstract yes yes.
        else if (folder is DynamicFolder<T> f)
        {
            f.SetName(newName, false);
            return Result.Success;
        }
        else
        {
            return Result.InvalidOperation;
        }
    }

    // Moves a Folder to a new location within the DDS.
    private Result MoveFolder(IDynamicCollection<T> folder, DynamicFolderGroup<T> newParent, out DynamicFolderGroup<T> oldParent)
    {
        // store the old parent for reference on out param.
        oldParent = folder.Parent;
        // If the old and new parents are the same, do nothing.
        if (oldParent == newParent)
            return Result.SuccessNothingDone;

        // Remove the child from the old parent, and add it to the new parent.
        oldParent.Children.Remove(folder);
        // Assign it to the new one.
        // If it failed, it is because the item exists.
        return newParent.AddChild(folder) ? Result.Success : Result.ItemExists;
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
            if (topFolder.AddChild(folder))
            {
                ++idCounter;
                res = Result.Success;
                topFolder = folder;
            }
            else
            {
                // It already exists, then we can skip incrementing the id and just update topFolder.
                topFolder = folder;
            }
        }

        // Return the final result.
        return res;
    }

    private Result RemoveFolder(IDynamicCollection<T> folder)
    {
        // If the folder is not found, it was technically removed lol.
        if (!_folderMap.TryGetValue(folder.Name, out var match))
            return Result.SuccessNothingDone;

        // Otherwise, it does exist, and we should remove it.
        if (folder is DynamicFolderGroup<T> fg)
        {
            fg.Parent.Children.Remove(match);
            _folderMap.Remove(folder.Name);
            return Result.Success;
        }
        else if (folder is DynamicFolder<T> f)
        {
            f.Parent.Children.Remove(match);
            _folderMap.Remove(folder.Name);
            return Result.Success;
        }

        // Invalid otherwise.
        return Result.InvalidOperation;
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