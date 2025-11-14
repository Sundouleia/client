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
    private Result RenameLeaf(IDynamicWriteLeaf leaf, string newName)
    {
        // Prevent allowing a Leaf to behave as Root.
        if (leaf.Name.Length == 0 || leaf.Name == FolderCollection.RootLabel)
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
        // Some internal sort operation here.
        return Result.Success;
    }

    private Result RenameFolder(IDynamicWriteFolder folder, string newName)
    {
        // Prevent allowing a folder to behave as Root.
        if (folder.Name.Length is 0 || folder.Name == FolderCollection.RootLabel)
            return Result.InvalidOperation;

        // correct the newName to work with the DFS.
        newName = newName.FixName();

        // Return early if names are identical.
        if (newName == folder.Name)
            return Result.SuccessNothingDone;

        // If we locate the new name in the parent folder's children, decline the operation.
        if (Search(folder.Parent, newName) >= 0)
            return Result.ItemExists;

        // Otherwise, rename the folder and return success.
        folder.SetName(newName, false);
        return Result.Success;
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
    private Result MoveFolder(IDynamicFolderNode folder, DynamicFolderCollection<T> newParent, out DynamicFolderCollection<T> oldParent, string? newName = null)
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
        RemoveFolder(oldParent, folder);
        folder.SetName(actualNewName, false);
        AssignFolder(newParent, folder);
        return Result.Success;
    }


    /// <summary>
    ///     Creates all folders to the end, automatically excluding leaf paths from assignment.
    /// </summary>
    /// <param name="fullPath"> the entity's FullPath variable, excluding root. </param>
    /// <param name="topFolder"> the topmost available folder or folder collection in the path.</param>
    /// <returns> The result of this function. </returns>
    private Result CreateAllFolders(string fullPath, out IDynamicFolder topFolder)
        => CreateAllFoldersInternal(ParseSegments(fullPath), out topFolder);

    private Result CreateAllFoldersInternal(List<(string Name, SegmentType Type)> segments, out IDynamicFolder topFolder)
    {
        topFolder = Root;
        Result res = Result.SuccessNothingDone;

        foreach (var segment in segments)
        {
            // If the segment type if leaf, or the last entry is [folder], we know we hit the end, so break.
            if (segment.Type is SegmentType.Leaf || topFolder is not FolderCollection group)
                break;

            IDynamicWriteFolder folder = segment.Type is SegmentType.FolderCollection
                ? new FolderCollection(group, FAI.FolderTree, segment.Name, _idCounter + 1u)
                : new Folder(group, FAI.Folder, segment.Name, _idCounter + 1u);

            // Attempt to assign the folder.
            var assignRet = AssignFolder(group, folder, out int idx);
            // Sanity check against existing entries to early break if necessary.
            if (assignRet is Result.ItemExists)
            {
                // If there was a child with the same name, stop here.
                if (group.Children[idx] is not IDynamicWriteFolder f)
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

    private Result CreateAllFoldersAndFile(string path, out IDynamicFolderNode topFolder, out string fileName)
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


    private static void RemoveFolder(DynamicFolderCollection<T> parent, IDynamicFolderNode folder)
        => parent.Children.Remove(folder); // used to do this by index, see if we can handle it by ref.


    /// <summary>
    ///     Adds a leaf to a parent folder.
    /// </summary>
    private Result AssignLeaf( DynamicFolder<T> parent, DynamicLeaf<T> child)
    {
        // Ensure the item does not exist before calling the inner Method.
        if (Search(parent, child.Name) >= 0)
            return Result.ItemExists;
        // Assign it.
        AssignLeafInternal(parent, child);
        return Result.Success;
    }

    /// <summary>
    ///     Add a leaf to its new parent folder directly without search checks.
    /// </summary>
    /// <remarks>
    ///     You are expected when calling this to be certain a leaf of the same name 
    ///     does not already exist in the parent folder.
    /// </remarks>
    internal void AssignLeafInternal(DynamicFolder<T> parent, IDynamicLeaf<T> entity)
    {
        // Add the child into the folder. (I pray casting works here)
        parent.Children.Add((Leaf)entity);
        // update the entity's parent.
        entity.SetParent(parent);
        // resort the folder's children to be sorted for binary search.
        ((IDynamicWriteFolder)parent).SortChildren(_nameComparer);
        // Can re-add updating the parent's parent up to root here if needed.
    }


    private void AssignFolder(FolderCollection parent, IDynamicWriteFolder folder)
    {
        // Add the child into the folder collection.
        parent.Children.Add(folder);
        // update the folder's parent.
        folder.SetParent(parent);
        // Update the path.
        folder.UpdateFullPath();
        // resort the folder collection's children to be sorted for binary search. (maybe change this down the line to be linked to sort order.
        ((IDynamicWriteFolder)parent).SortChildren(_nameComparer);
    }

    // Add a folder to its parent folder collection, and out the new idx.
    // Aim to phase out IDX
    private Result AssignFolder(FolderCollection parent, IDynamicWriteFolder folder, out int idx)
    {
        idx = Search(parent, folder.Name);
        if (idx >= 0)
            return Result.ItemExists;

        idx = ~idx;
        AssignFolder(parent, folder);
        return Result.Success;
    }

    /// <summary>
    ///     Removes a leaf from its parent folder. <see cref="Leaf.Parent"/> remains unchanged.
    /// </summary>
    private Result RemoveLeaf(IDynamicWriteLeaf leaf)
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

    private Result RemoveFolder(IDynamicWriteFolder folder)
    {
        // if the folder does not exist in the parent FolderCollection, it was valid, but nothing occurred.
        var idx = Search(folder.Parent, folder.Name);
        if (idx < 0)
            return Result.SuccessNothingDone;
        // Otherwise remove it at the found index.
        folder.Parent.Children.RemoveAt(idx);
        // something about updating parent folder location should go here.
        return Result.Success;
    }

    /// <summary> 
    ///     Attempts to merge all leaves in <paramref name="from"/> into <paramref name="to"/>. <para />
    ///     <b> NOTICE: </b>
    ///     Because mapped <typeparamref name="T"/> objects can associate with one or more leaves, leaves
    ///     containing data that <paramref name="to"/>'s leaves already map are ignored.
    /// </summary>
    private Result MergeFolders(Folder from, Folder to)
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
    private Result MergeFolders(FolderCollection from, FolderCollection to)
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
    private static bool HasValidHeritage(FolderCollection potentialParent, IDynamicFolder folder)
    {
        var parent = potentialParent;
        // Root is 
        while (parent.Name.Length > 0)
        {
            if (parent == folder)
                return false;

            parent = parent.Parent;
        }

        return true;
    }
}