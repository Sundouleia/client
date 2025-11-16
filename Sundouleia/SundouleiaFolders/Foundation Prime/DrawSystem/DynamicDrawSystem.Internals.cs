namespace Sundouleia.DrawSystem;

// the internal, private operations for the dynamic file system.
public partial class DynamicDrawSystem<T> where T : class
{
    private enum Result
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
        leaf.Parent.SortChildren(_nameComparer);
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

    /// <summary>
    ///     Try to move a leaf to a new parent Folder, and renaming it if requested. <para />
    /// </summary>
    /// <remarks>
    ///     <see cref="Result.ItemExists"/> is returned if moving a <see cref="Leaf"/> with 
    ///     <see cref="Leaf.Data"/> that exists in any child of <paramref name="newParent"/>.
    /// </remarks>
    private Result MoveLeaf(DynamicLeaf<T> leaf, DynamicFolder<T> newParent, out DynamicFolder<T> oldParent, string? newName = null)
    {
        // store the current old parent of the leaf.
        oldParent = leaf.Parent;
        // If the parents are the same, return either that nothing we done, or perform a rename.
        if (newParent == oldParent)
            return newName == null ? Result.SuccessNothingDone : RenameLeaf(leaf, newName);

        // obtain the true newName.
        var actualNewName = newName?.FixName() ?? leaf.Name;
        // prevent the move if anything under the new folder contains a leaf with the same name.
        if (Search(newParent, actualNewName) >= 0)
            return Result.ItemExists;

        // Second Pass, ensure no duplicate data if the writeLeaf is a mapped Leaf
        if (newParent.Children.Any(c => c.Data == leaf.Data))
            return Result.ItemExists;

        // Otherwise the move operation is valid, so remove the leaf from the old parent, and set it in the new one.
        RemoveLeaf(leaf);
        leaf.SetName(actualNewName, false);
        AssignLeafInternal(newParent, leaf);
        return Result.Success;
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
    private Result CreateAllFolders(string fullPath, out IDynamicCollection<T> topFolder)
        => CreateAllFoldersInternal(ParseSegments(fullPath), out topFolder);

    private Result CreateAllFoldersInternal(List<(string Name, SegmentType Type)> segments, out IDynamicCollection<T> topFolder)
    {
        topFolder = Root;
        Result res = Result.SuccessNothingDone;

        foreach (var segment in segments)
        {
            // If the segment type if leaf, or the last entry is [folder], we know we hit the end, so break.
            if (segment.Type is SegmentType.Leaf || topFolder is not DynamicFolderGroup<T> group)
                break;

            IDynamicCollection<T> folder = segment.Type is SegmentType.FolderCollection
                ? new DynamicFolderGroup<T>(group, FAI.FolderTree, segment.Name, _idCounter + 1u)
                : new DynamicFolder<T>(group, FAI.Folder, segment.Name, _idCounter + 1u);

            // Attempt to assign the folder.
            var assignRet = AssignFolder(group, folder, out int idx);
            // Sanity check against existing entries to early break if necessary.
            if (assignRet is Result.ItemExists)
            {
                // If there was a child with the same name, stop here.
                if (group.Children[idx] is not IDynamicCollection<T> f)
                    return Result.ItemExists;

                // Otherwise, update the top folder to this folder (which already existed, so dont inc ID)
                topFolder = f;
            }
            // Otherwise, it is a new folder to be added, so inc ID, update res, and set topFolder.
            else
            {
                ++_idCounter;
                res = Result.Success;
                topFolder = folder;
            }
        }

        // Return the final result.
        return res;
    }

    private Result CreateAllFoldersAndFile(string path, out IDynamicCollection<T> topFolder, out string fileName)
    {
        topFolder = Root;
        fileName = string.Empty;
        // Exit if no path.
        if (path.Length is 0)
            return Result.SuccessNothingDone;

        // If the final path was in root, just return that
        // (or maybe dont since we dont want to allow a leaf there. IDK)
        var segments = ParseSegments(path);
        if (segments.Count is 1)
        {
            fileName = segments[0].Name;
            return Result.SuccessNothingDone;
        }

        // Otherwise, create all folders for these segments. (May need to adjust this in cases where it
        // identifies segments[^1] as a non-leaf or something. IDK.
        fileName = segments[^1].Name;
        return CreateAllFoldersInternal(segments, out topFolder);
    }

    /// <summary>
    ///     Adds a leaf to a parent folder.
    /// </summary>
    private Result AssignLeaf(DynamicFolder<T> parent, DynamicLeaf<T> child)
    {
        // Ensure the item does not exist before calling the inner Method.
        if (Search(parent, child.Name) >= 0)
            return Result.ItemExists;
        // Assign it.
        AssignLeafInternal(parent, child);
        return Result.Success;
    }

    /// <summary>
    ///     Add leaf and update that leaf's parent, then sort the parents children. <para />
    ///     This is all done without search checks.
    /// </summary>
    internal void AssignLeafInternal(DynamicFolder<T> parent, DynamicLeaf<T> entity)
    {
        parent.Children.Add(entity);
        entity.Parent = parent;
        entity.UpdateFullPath();
        parent.SortChildren(_nameComparer);
        // Can re-add updating the parent's parent up to root here if needed.
    }

    internal void AssignFolder(DynamicFolderGroup<T> parent, IDynamicCollection<T> folder)
    {
        if (folder is DynamicFolderGroup<T> fc)
        {
            parent.Children.Add(fc);
            fc.Parent = parent;
            fc.UpdateFullPath();
            parent.SortChildren(_nameComparer);
        }
        else if (folder is DynamicFolder<T> f)
        {
            parent.Children.Add(f);
            f.Parent = parent;
            f.UpdateFullPath();
            parent.SortChildren(_nameComparer);
        }
    }

    // Add a folder to its parent FolderCollection, and out the new idx.
    // Aim to phase out IDX
    private Result AssignFolder(DynamicFolderGroup<T> parent, IDynamicCollection<T> folder, out int idx)
    {
        if (folder is DynamicFolderGroup<T> fc)
        {
            idx = Search(parent, fc.Name);
            if (idx >= 0)
                return Result.ItemExists;
            idx = ~idx;
            AssignFolder(parent, fc);
            return Result.Success;
        }
        else if (folder is DynamicFolder<T> f)
        {
            idx = Search(parent, f.Name);
            if (idx >= 0)
                return Result.ItemExists;
            idx = ~idx;
            AssignFolder(parent, f);
            return Result.Success;
        }

        idx = Search(parent, folder.Name);
        if (idx >= 0)
            return Result.ItemExists;
        idx = ~idx;
        AssignFolder(parent, folder);
        return Result.Success;
    }

    /// <summary>
    ///     Removes a leaf from its parent folder. <see cref="DynamicLeaf{T}.Parent"/> remains unchanged.
    /// </summary>
    private Result RemoveLeaf(DynamicLeaf<T> leaf)
    {
        // If the leaf doesn't exist in the folder, the operation was valid, but nothing happened.
        var idx = Search(leaf.Parent, leaf.Name);
        if (idx < 0)
            return Result.SuccessNothingDone;
        // Otherwise, remove it at the found index.
        leaf.Parent.Children.RemoveAt(idx);
        // something about updating parent folder location should go here.
        return Result.Success;
    }

    private Result RemoveFolder(IDynamicCollection<T> folder)
    {
        if (folder is DynamicFolderGroup<T> fc && !fc.IsRoot)
        {
            var idx = Search(fc.Parent, fc.Name);
            if (idx < 0)
                return Result.SuccessNothingDone;

            fc.Parent.Children.RemoveAt(idx);
            return Result.Success;
        }
        else if (folder is DynamicFolder<T> f)
        {
            var idx = Search(f.Parent, f.Name);
            if (idx < 0)
                return Result.SuccessNothingDone;
            f.Parent.Children.RemoveAt(idx);
            return Result.Success;
        }
        else
        {
            return Result.InvalidOperation;
        }
    }

    /// <summary> 
    ///     Attempts to merge all leaves in <paramref name="from"/> into <paramref name="to"/>. <para />
    ///     <b> NOTICE: </b>
    ///     Because mapped <typeparamref name="T"/> objects can associate with one or more leaves, leaves
    ///     containing data that <paramref name="to"/>'s leaves already map are ignored.
    /// </summary>
    private Result MergeFolders(DynamicFolder<T> from, DynamicFolder<T> to)
    {
        // if the collections are the same, fail.
        if (from == to)
            return Result.SuccessNothingDone;
        // If we are moving from root, fail.
        if (from.Name.Length is 0)
            return Result.InvalidOperation;

        // Otherwise we can proceed to merge.
        var result = from.TotalChildren is 0 ? Result.Success : Result.NoSuccess;
        // iterate through the children and move them.
        for (var i = 0; i < from.TotalChildren;)
        {
            var moveRet = MoveLeaf(from.Children[i], to, out _);
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