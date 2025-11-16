using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sundouleia.DrawSystem;

// Maybe phase this out as things evolve, possibly make things abstract
// and force implementation for actions as overrides.
public enum DDSChangeType
{
    ObjectRenamed,
    ObjectRemoved,
    FolderAdded,
    LeafAdded,
    ObjectMoved,
    FolderMerged,
    PartialMerge,
    Reload,
}

/// <summary>
///     A class intended to be used via inheritance for defined implementations,
///     and composition for the selectors. <para />
///     
///     Designed to improve upon the conceptual DynamicFolder, while
///     providing maximum flexibility and customization. <para />
///     
///     This is an effort to fuse the benefits of both Moon's Folder Framework 
///     and OtterGui/Luna's FileSystem together. <para />
/// 
///     The goal is to allow a file system of similar structure, with more robust 
///     customization points, and allowing a <typeparamref name="T"/> to have multiple associated leaves.
/// </summary>
public partial class DynamicDrawSystem<T> where T : class
{
    // An internal change action event for a selector to link to without overloading
    // the mediator when multiple file systems are active.
    public delegate void ChangeDelegate(DDSChangeType type, IDynamicNode<T> obj, IDynamicCollection<T>? prevParent, IDynamicCollection<T>? newParent);
    public event ChangeDelegate? Changed;

    private readonly Dictionary<T, HashSet<DynamicLeaf<T>>> _leafMap = [];

    /// <summary> For comparing Entities by name only. </summary>
    private readonly NameComparer _nameComparer;

    /// <summary> The private, incrementing ID counter for this dynamic folder system. </summary>
    private uint _idCounter = 1;

    public DynamicDrawSystem(IComparer<ReadOnlySpan<char>>? comparer = null)
    {
        _nameComparer = new NameComparer(comparer ?? new OrdinalSpanComparer());
    }

    /// <summary> The root folder collection of this dynamic folder system. </summary>
    public DynamicFolderGroup<T> Root = DynamicFolderGroup<T>.CreateRoot();

    public bool TryGetValue(T key, [NotNullWhen(true)] out HashSet<DynamicLeaf<T>>? values)
        => _leafMap.TryGetValue(key, out values);

    /// <summary>
    ///     Find a leaf inside a folder using the given comparer.
    /// </summary>
    private int Search(DynamicFolder<T> parent, ReadOnlySpan<char> name)
        => CollectionsMarshal.AsSpan(parent.Children).BinarySearch(new SearchNode(_nameComparer, name));

    /// <summary>
    ///     Find a leaf inside a folder using the given comparer.
    /// </summary>
    private int Search(DynamicFolderGroup<T> parent, ReadOnlySpan<char> name)
        => CollectionsMarshal.AsSpan(parent.Children).BinarySearch(new SearchNode(_nameComparer, name));

    /// <summary>
    ///     Check whether two strings are equal according to the DFS name comparer.
    /// </summary>
    public bool Equal(ReadOnlySpan<char> lhs, ReadOnlySpan<char> rhs)
        => _nameComparer.BaseComparer.Compare(lhs, rhs) is 0;

    // Sets the expanded state of a folder to a new value.
    public bool SetFolderOpenState(IDynamicCollection<T> folder, bool isOpen)
    {
        if (folder.IsOpen == isOpen)
            return false;

        if (folder is DynamicFolderGroup<T> fc)
        {
            fc.SetIsOpen(isOpen);
            // TODO : Mediator Invoke here.
            return true;
        }
        else if (folder is DynamicFolder<T> f)
        {
            f.SetIsOpen(isOpen);
            // TODO : Mediator Invoke here.
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Attempt to locate a defined folder from its folder path.
    /// </summary>
    public bool FindFolder(string fullFolderPath, out IDynamicCollection<T> folder)
    {
        folder = Root;
        var segments = ParseSegments(fullFolderPath);
        foreach (var segment in segments)
        {
            if (segment.Type is not (SegmentType.Folder or SegmentType.FolderCollection))
                return false;

            // If the type is a folder, we found the end segment.
            if (folder is not DynamicFolderGroup<T> fc)
            {
                // we found the end for folder condition.
                return true;
            }

            // Search the current segment name.
            var idx = Search(fc, segment.Name);
            if (idx < 0)
            {
                // Nothing was found, meaning that it doesn't exist, so fail.
                Svc.Logger.Warning($"Failed to find folder [{segment.Name}] in path [{fullFolderPath}].");
                return false;
            }

            // Otherwise it was found so update the child.
            folder = fc.Children[idx];
        }

        // If we made it all the way return true.
        return true;
    }

    /// <summary>
    ///     Associates a created leaf with its respective data in the dictionary.
    /// </summary>
    internal void AddLeafToMap(T data, DynamicLeaf<T> entity)
    {
        if (!_leafMap.TryGetValue(data, out var leaves))
        {
            leaves = new HashSet<DynamicLeaf<T>>();
            _leafMap[data] = leaves;
        }
        leaves.Add(entity);
    }

    /// <summary>
    ///     Removes association of a leaf entity from it's data. (usually upon removal)
    /// </summary>
    internal void RemoveLeafFromMap(DynamicLeaf<T> entity)
    {
        // Locate the leaf via its value.
        if (!_leafMap.TryGetValue(entity.Data, out var leaves))
            return;
        // Remove the entity from the HashSet.
        leaves.Remove(entity);
        // If no more leaves are present, remove the key.
        if (leaves.Count is 0)
            _leafMap.Remove(entity.Data);
    }

    /// <summary>
    ///     Creates a new Leaf item under the specified parent folder.
    /// </summary>
    /// <typeparam name="T"> The type of data for the node. </typeparam>
    /// <param name="parent"> The parent folder to create the data node in. </param>
    /// <param name="name"> The name to assign the data node. </param>
    /// <param name="data"> The data object associated with the node. </param>
    /// <returns> The newly created data node in <paramref name="parent"/>. </returns>
    /// <exception cref="Exception"> Throws if a leaf of the name already exists in parent. </exception>
    public DynamicLeaf<T> CreateEntity(DynamicFolder<T> parent, string name, T data)
    {
        var entity = new DynamicLeaf<T>(parent, name, data, _idCounter + 1u);
        // Attempt to set the entity in the parent folder.
        if (AssignLeaf(parent, entity) is Result.ItemExists)
            throw new Exception($"Could not add entity [{entity.Name}] to folder [{parent.Name}]: Node with the same name exists.");

        // Successful, so increment the ID counter and add to the data-leaf map.
        ++_idCounter;
        AddLeafToMap(data, entity);
        // TODO: Invoke mediator change here.
        return entity;
    }

    /// <summary>
    ///     Creates a new folder labeled <paramref name="name"/> under the specified <paramref name="parent"/> folder collection. <para />
    ///     If any folder throughout the entire hierarchy already exists with the same name, an exception is thrown.
    /// </summary>
    /// <param name="folderGroup"> The parent folder to create the data node in. </param>
    /// <param name="name"> The name to assign the data node. </param>
    /// <param name="icon"> The icon to assign to the folder. </param>
    /// <returns> The newly created folder in <paramref name="folderGroup"/>. </returns>
    /// <exception cref="Exception"> Throws if a folder of the name already exists in parent. </exception>
    public DynamicFolder<T> CreateFolder(DynamicFolderGroup<T> folderGroup, string name, FAI icon)
    {
        var folder = new DynamicFolder<T>(folderGroup, icon, name, _idCounter + 1u);
        // Attempt to set the folder in the parent folder collection.
        if (AssignFolder(folderGroup, folder, out _) is Result.ItemExists)
            throw new Exception($"Could not add folder [{folder.Name}] to folder collection [{folderGroup.Name}]: Folder with the same name exists.");
        
        // Successful, so increment the ID counter.
        ++_idCounter;

        // Could add the folder to a mapping if we wanted but dont really know right now.

        // TODO: Invoke mediator change here.
        return folder;
    }

    /// <summary>
    ///     Splits path into successive subfolders of root and finds or creates the topmost folder.
    /// </summary>
    /// <returns> The topmost folder. </returns>
    /// <exception cref="Exception"> If a folder can't be found or created due to an existing non-folder child with the same name. </exception>
    public IDynamicCollection<T> FindOrCreateAllFolders(string path)
    {
        var retCode = CreateAllFolders(path, out IDynamicCollection<T> topFolder);
        // Respond based on the resulting action.
        switch (retCode)
        {
            case Result.Success:
                // Mediator invoke that a folder was added, providing the folder, and its parent.
                break;

            case Result.ItemExists:
                // Throw Exception.
                throw new Exception($"Could not create new folder for {path}: {topFolder.FullPath} already contains an object with a required name.");
            case Result.PartialSuccess:
                // Invoke it was added, but also throw an exception.
                // TODO: Mediator invoke here.
                throw new Exception($"Could not create all new folders for {path}: {topFolder.FullPath} already contains an object with a required name.");
        }

        return topFolder;
    }

    /// <summary>
    ///     Can rename and move any entity type in the DFS.
    /// </summary>
    /// <param name="entity"> The entity to rename and move. </param>
    /// <param name="newPath"> The new path to move the entity to. </param>
    /// <exception cref="Exception"> Throws if the move could not be completed. Which can occur for a multitude of reasons. </exception>
    public void RenameAndMove(IDynamicNode entity, string newPath)
    {
        if (newPath.Length == 0)
            throw new Exception($"Could not change path of {entity.FullPath} to an empty path.");

        // Store the old entity FullPath.
        var oldPath = entity.FullPath;
        // Exit if old and new paths are the same.
        if (newPath == oldPath)
            return;
        // If any folders to the new path are missing, we should create them.
        var retCode = CreateAllFoldersAndFile(newPath, out IDynamicCollection<T> folder, out string fileName);

        // Handle case where entity is a leaf and output is [Folder]
        if (entity is DynamicLeaf<T> l && folder is DynamicFolder<T> f)
        {
            switch (retCode)
            {
                case Result.Success:
                    // Move the leaf.
                    MoveLeaf(l, f, out _, fileName);
                    // TODO: Invoke Mediator of change.
                    break;
                case Result.SuccessNothingDone:
                    // Update the retCode to this move. If the item exists, throw an exception.
                    retCode = MoveLeaf(l, f, out _, fileName);
                    if (retCode is Result.ItemExists)
                        throw new Bagagwa($"Could not move {oldPath} to {newPath}: An object of name {fileName} already exists.");
                    // Otherwise it worked, so invoke change.
                    // TODO: Invoke Mediator Change.
                    return;
                case Result.ItemExists:
                    throw new Exception($"Could not create {newPath} for {oldPath}: A pre-existing folder contained an entity with the same name.");
            }
        }
        else if (entity is IDynamicCollection<T> fn && folder is DynamicFolderGroup<T> fc)
        {
            // Handle case where entity is a folder and output is [FolderCollection]
            switch (retCode)
            {
                case Result.Success:
                    // Move the folder.
                    MoveFolder(fn, fc, out _, fileName);
                    // TODO: Invoke Mediator of change.
                    break;
                case Result.SuccessNothingDone:
                    // Update the retCode to this move. If the item exists, throw an exception.
                    retCode = MoveFolder(fn, fc, out _, fileName);
                    if (retCode is Result.ItemExists)
                        throw new Bagagwa($"Could not move {oldPath} to {newPath}: An object of name {fileName} already exists.");
                    // Otherwise it worked, so invoke change.
                    // TODO: Invoke Mediator Change.
                    return;
                case Result.ItemExists:
                    throw new Exception($"Could not create {newPath} for {oldPath}: A pre-existing folder contained an entity with the same name.");
            }
        }
        else
            throw new Bagagwa($"Tried to perform an invalid move operation! Entity Type and Expected topmost folder type did not match expected!");
    }

    /// <summary>
    ///     Renames an entity to <paramref name="newName"/>. <para />
    ///     Throws if an item of the same name exists in the Children of <paramref name="entity"/>'s Parent folder.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public void Rename(IDynamicNode entity, string newName)
    {
        var retCode = entity switch
        {
            DynamicLeaf<T> l => RenameLeaf(l, newName),
            IDynamicCollection<T> fn => RenameFolder(fn, newName),
            _ => throw new Exception($"Attempted to rename {entity.Name}, but it had an invalid type!")
        };

        switch (retCode)
        {
            case Result.ItemExists: throw new Exception($"Can't rename {entity.Name} to {newName}: another entity in {entity.Name}'s Parent has the same name.");
            case Result.Success:
                // TODO: Mediator invoke.
                return;
        }
    }

    /// <summary>
    ///     Delete an entity from it's Parent. <para />
    ///     An Exception is thrown if the entity is root.
    /// </summary>
    /// <param name="entity"> The entity to delete </param>
    /// <exception cref="Exception"></exception>
    public void Delete(IDynamicNode entity)
    {
        var retCode = entity switch
        {
            DynamicLeaf<T> l => RemoveLeaf(l),
            IDynamicCollection<T> fn => RemoveFolder(fn),
            _ => throw new Exception($"Attempted to remove {entity.Name}, but it had an invalid type!")
        };

        switch (retCode)
        {
            case Result.InvalidOperation: throw new Exception("Can't delete the root entity.");
            case Result.Success:
                if (entity is DynamicLeaf<T> l)
                    RemoveLeafFromMap(l);
                // TODO: Invoke Mediator Change.
                return;
        }
    }

    /// <summary>
    ///     Move a <paramref name="leaf"/> to <paramref name="newParent"/>. <para />
    /// </summary>
    /// <param name="leaf"> The leaf to move. </param>
    /// <param name="newParent"> the new parent the leaf will be under. </param>
    /// <exception cref="Exception"> Throws if the leaf name already exists in newParent. </exception>
    public void Move(DynamicLeaf<T> leaf, DynamicFolder<T> newParent)
    {
        switch(MoveLeaf(leaf, newParent, out var oldParent))
        {
            case Result.Success:
                // TODO: Invoke Mediator change.
                break;
            case Result.SuccessNothingDone:
                return;
            case Result.InvalidOperation:
                throw new Exception("Can not move root directory.");
            case Result.ItemExists:
                throw new Exception($"Can't move {leaf.Name} into {newParent.FullPath} because an identical child in the parent already exists.");
        }
    }

    public void Move(IDynamicCollection<T> folder, DynamicFolderGroup<T> newParent)
    {
        switch (MoveFolder(folder, newParent, out var oldParent))
        {
            case Result.Success:
                // TODO: Invoke Mediator change.
                break;
            case Result.SuccessNothingDone:
                return;
            case Result.InvalidOperation:
                throw new Exception("Can not move root directory.");
            case Result.CircularReference:
                throw new Exception($"Can not move {folder.FullPath} into {newParent.FullPath} since folders can not contain themselves.");
            case Result.ItemExists:
                // if and only if both folders are FolderCollections, should we allow a merge to occur.
                var matchingIdx = Search(newParent, folder.Name);
                // If we meet criteria to merge, do so.
                if (folder is DynamicFolderGroup<T> fc && newParent.Children[matchingIdx] is DynamicFolderGroup<T> destFolder)
                {
                    Merge(fc, destFolder);
                    return;
                }

                throw new Exception($"Can't move {folder.Name} into {newParent.FullPath}, another folder inside it already exists.");
        }
    }

    public void Merge(DynamicFolder<T> from, DynamicFolder<T> to)
    {
        switch (MergeFolders(from, to))
        {
            case Result.SuccessNothingDone:
                return;

            case Result.InvalidOperation:
                throw new Exception($"Can not merge root directory into {to.FullPath}.");

            case Result.Success:
                // TODO: Invoke Mediator.
                return;

            case Result.PartialSuccess:
                // TODO: Invoke Mediator.
                return;

            case Result.NoSuccess:
                throw new Exception($"Could not merge {from.FullPath} into {to.FullPath}. All children already existed in the target.");
        }
    }

    public void Merge(DynamicFolderGroup<T> from, DynamicFolderGroup<T> to)
    {
        switch(MergeFolders(from, to))
        {
            case Result.SuccessNothingDone:
                return;

            case Result.InvalidOperation:
                throw new Exception($"Can not merge root directory into {to.FullPath}.");
            
            case Result.Success:
                // TODO: Invoke Mediator.
                return;
            
            case Result.PartialSuccess:
                // TODO: Invoke Mediator.
                return;

            case Result.NoSuccess:
                throw new Exception($"Could not merge {from.FullPath} into {to.FullPath}. All children already existed in the target.");
        }
    }
}