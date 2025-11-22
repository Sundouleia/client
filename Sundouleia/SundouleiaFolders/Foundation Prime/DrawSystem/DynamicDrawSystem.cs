using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Sundouleia.DrawSystem;

// Definitely revise this.
public enum DDSChangeType
{
    ObjectRenamed,
    ObjectRemoved,
    FolderAdded,
    ObjectMoved,
    FolderMerged,
    PartialMerge,
    FlagChange,
    FolderUpdated,
    Reload,
}

/// <summary>
///     A DynamicDrawSystem works in a similar fashion to CkFileSystem / OtterGui.FileSystem,
///     except all leaves are managed internally through generators. <para />
///     Every created folder is assigned with a Generator, which updates the respective leaves in it. <para />
///     As such, only folders and folder collections can be moved, renamed, or have other actions performed on it. <para />
///     Update this overtime, or potentially make the dynamic draw system abstract for additional forced implementations.
/// </summary>
public abstract partial class DynamicDrawSystem<T> where T : class
{
    // An internal change action event for a selector to link to without overloading
    // the mediator when multiple file systems are active.
    public delegate void ChangeDelegate(DDSChangeType type, IDynamicNode<T> obj, IDynamicCollection<T>? prevParent, IDynamicCollection<T>? newParent);
    public event ChangeDelegate? Changed;

    // Internal folder mapping for quick access by name.
    private readonly Dictionary<string, IDynamicCollection<T>> _folderMap = [];
    // could do a leaf map as well but do not see any need to.

    /// <summary> For comparing Entities by name only. </summary>
    private readonly NameComparer _nameComparer;

    /// <summary> The private, incrementing ID counter for this dynamic folder system. </summary>
    protected uint idCounter { get; private set; } = 1;

    /// <summary> The root folder collection of this dynamic folder system. </summary>
    protected DynamicFolderGroup<T> root = DynamicFolderGroup<T>.CreateRoot();

    public DynamicDrawSystem(IComparer<ReadOnlySpan<char>>? comparer = null)
    {
        _nameComparer = new NameComparer(comparer ?? new OrdinalSpanComparer());
    }

    /// <summary>
    ///     Read-only Accessor for root via classes desiring inspection while preventing edits.
    ///     (This is technically already dont via internal setters but whatever).
    /// </summary>
    public IDynamicFolderGroup<T> Root
        => root;

    // Temporary for debugger assistance.
    public IReadOnlyDictionary<string, IDynamicCollection<T>> FolderMap 
        => _folderMap;

    public bool TryGetFolder(string name, [NotNullWhen(true)] out IDynamicCollection<T>? folder)
        => _folderMap.TryGetValue(name, out folder);

    private int Search(DynamicFolder<T> parent, ReadOnlySpan<char> name)
        => CollectionsMarshal.AsSpan(parent.Children).BinarySearch(new SearchNode(_nameComparer, name));

    private int Search(DynamicFolderGroup<T> parent, ReadOnlySpan<char> name)
        => CollectionsMarshal.AsSpan(parent.Children).BinarySearch(new SearchNode(_nameComparer, name));

    public bool Equal(ReadOnlySpan<char> lhs, ReadOnlySpan<char> rhs)
        => _nameComparer.BaseComparer.Compare(lhs, rhs) is 0;

    // Useful for dynamic folders wanting to contain their own nested folders.
    protected DynamicFolderGroup<T> GetGroupByName(string parentName)
       => _folderMap.TryGetValue(parentName, out var f) && f is DynamicFolderGroup<T> fg
        ? fg : root;

    protected void AddFolder(DynamicFolder<T> folder)
    {
        // Ensure Validity
        if (folder.Parent is null)
            folder.Parent = root;
        // Ensure Validity
        if (folder.ID != idCounter + 1u)
            folder.ID = idCounter + 1u;

        // If a folder with the same name already exists, return.
        if (_folderMap.ContainsKey(folder.Name))
            return;

        // Attempt to assign the folder. If it fails, throw an exception.
        if (AssignFolder(folder.Parent, folder) is Result.ItemExists)
            throw new Exception($"Could not add folder [{folder.Name}] to group [{folder.Parent.Name}]: Folder with the same name exists.");

        // Successful, so increment the ID counter.
        ++idCounter;

        // Could add the folder to a mapping if we wanted but dont really know right now.
        _folderMap[folder.Name] = folder;

        // Revise later.
        Changed?.Invoke(DDSChangeType.FolderAdded, folder, null, folder.Parent);
    }


    // Retrieve the updated state of all folders.
    public void UpdateFolders()
    {
        IEnumerable<IDynamicLeaf<T>> removed = [];

        foreach (var folder in _folderMap.Values.OfType<DynamicFolder<T>>())
            if (folder.Update(_nameComparer, out var rem))
                removed = removed.Concat(rem);

        // Notify of removed leaves.

        // Notify of the updated state.
        Changed?.Invoke(DDSChangeType.FolderUpdated, root, null, null);
    }

    public void UpdateFolder(DynamicFolder<T> folder)
    {
        if (folder.Update(_nameComparer, out var removed))
        {
            // Notify of removed leaves.
        }

        // Revise later.
        Changed?.Invoke(DDSChangeType.FolderUpdated, folder, null, null);
    }



    // Sets the expanded state of a folder to a new value.
    public bool SetOpenState(IDynamicCollection<T> folder, bool isOpen)
    {
        if (folder.IsOpen == isOpen)
            return false;

        if (folder is DynamicFolderGroup<T> fc)
            fc.SetIsOpen(isOpen);
        else if (folder is DynamicFolder<T> f)
            f.SetIsOpen(isOpen);
        else
            return false;

        // Revise later.
        Changed?.Invoke(DDSChangeType.FolderUpdated, folder, null, null);
        return true;
    }

    /// <summary>
    ///     Internally rename a defined folder in the DDS.
    /// </summary>
    public void Rename(IDynamicCollection<T> node, string newName)
    {
        switch (RenameFolder(node, newName))
        {
            case Result.ItemExists: throw new Exception($"Can't rename {node.Name} to {newName}: another entity in {node.Name}'s Parent has the same name.");
            case Result.Success:
                // Revise later.
                Changed?.Invoke(DDSChangeType.ObjectRenamed, node, null, null);
                return;
        }
    }

    /// <summary>
    ///     Splits path into successive subfolders of root and finds or creates the topmost folder. <para />
    ///     <b> WARNING: Very unstable, not tested with non-FolderGroup paths. Could break things. </b>
    /// </summary>
    /// <returns> The topmost folder. </returns>
    /// <exception cref="Exception"> If a folder can't be found or created due to an existing non-folder child with the same name. </exception>
    public IDynamicCollection<T> FindOrCreateAllGroups(string path)
    {
        // Respond based on the resulting action.
        switch (CreateAllGroups(path, out DynamicFolderGroup<T> topFolder))
        {
            case Result.Success:
                // Revise later.
                Changed?.Invoke(DDSChangeType.FolderAdded, topFolder, null, topFolder.Parent);
                break;

            case Result.ItemExists:
                // Throw Exception.
                throw new Exception($"Could not create new folder for {path}: {topFolder.FullPath} already contains an object with a required name.");
            case Result.PartialSuccess:
                // Revise this invoker later.
                Changed?.Invoke(DDSChangeType.FolderAdded, topFolder, null, topFolder.Parent);
                // Throw exception due to partial failure, since it expects to create all, but failed before finishing.
                throw new Exception($"Could not create all new folders for {path}: {topFolder.FullPath} already contains an object with a required name.");
        }

        return topFolder;
    }

    /// <summary>
    ///     Delete a folder from it's Parent. Locates the folder by name first. <para />
    ///     An Exception is thrown if the entity is root.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public void Delete(string folderName)
    {
        if (!_folderMap.TryGetValue(folderName, out var match))
            return;
        // Perform the actual deletion.
        Delete(match);
    }

    /// <summary>
    ///     Delete a folder from it's Parent. <para />
    ///     An Exception is thrown if the entity is root.
    /// </summary>
    /// <exception cref="Exception"></exception>
    public void Delete(IDynamicCollection<T> folder)
    {
        switch (RemoveFolder(folder))
        {
            case Result.InvalidOperation:
                throw new Exception("Can't delete the root folder.");
            case Result.Success:
                // Revise later.
                Changed?.Invoke(DDSChangeType.ObjectRemoved, folder, folder.Parent, null);
                return;
        }
    }

    public void Move(IDynamicCollection<T> folder, DynamicFolderGroup<T> newParent)
    {
        switch (MoveFolder(folder, newParent, out var oldParent))
        {
            case Result.Success:
                // Revise later.
                Changed?.Invoke(DDSChangeType.ObjectMoved, folder, oldParent, newParent);
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

    // This would technically be something called in an abstract method or whatever so it can be handled externally.
    public void Merge(DynamicFolder<T> from, DynamicFolder<T> to)
    {
        switch (MergeFolders(from, to))
        {
            case Result.SuccessNothingDone:
                return;

            case Result.InvalidOperation:
                throw new Exception($"Can not merge root directory into {to.FullPath}.");

            case Result.Success:
                // Revise later.
                Changed?.Invoke(DDSChangeType.FolderMerged, from, from, to);
                return;

            case Result.PartialSuccess:
                // Revise later.
                Changed?.Invoke(DDSChangeType.PartialMerge, from, from, to);
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
                // Revise later.
                Changed?.Invoke(DDSChangeType.FolderMerged, from, from, to);
                return;
            
            case Result.PartialSuccess:
                // Revise later.
                Changed?.Invoke(DDSChangeType.PartialMerge, from, from, to);
                return;

            case Result.NoSuccess:
                throw new Exception($"Could not merge {from.FullPath} into {to.FullPath}. All children already existed in the target.");
        }
    }
}