namespace Sundouleia.ModFiles;

/// <summary>
///     Defines a collected modded state for all owned client actors. <para />
///     Primarily used as a helper structure for character data parsing. <para />
///     This is a mutable object and should not be publically exposed where undesired.
/// </summary>
public class ModdedState
{
    public ModdedState()
    { }

    public HashSet<ModdedFile> AllFiles { get; private set; } = new(ModdedFileComparer.Instance);
    public Dictionary<OwnedObject, HashSet<ModdedFile>> FilesByObject { get; private set; } = new();

    // Provide which ownedObjects have files, and which dont.
    public IEnumerable<OwnedObject> ModdedActors => FilesByObject.Keys;

    // Assign a set of modded files to an owned object.
    public void SetOwnedFiles(OwnedObject obj, HashSet<ModdedFile> files)
    {
        // If there is no files to add, ret early.
        if (files.Count is 0)
            return;

        FilesByObject[obj] = files;
        AllFiles.UnionWith(files);
    }

    public void ClearForObject(OwnedObject obj)
    {
        if (FilesByObject.Remove(obj, out var removedFiles))
        {
            // Rebuild the all files set.
            AllFiles.Clear();
            foreach (var fileSet in FilesByObject.Values)
                AllFiles.UnionWith(fileSet);
        }
    }

    public void Clear()
    {
        FilesByObject.Clear();
        AllFiles.Clear();
    }
}