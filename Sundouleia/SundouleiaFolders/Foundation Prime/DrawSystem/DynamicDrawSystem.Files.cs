using CkCommons;

namespace Sundouleia.DrawSystem;

// Process Config Saving & Loading.
// ------------------------
// In contrast to what is likely assumed, the config does not save or load the entire DrawSystem.
// Instead, only the hierarchy structure of the FolderCollections & Folders is stored.
//
// This is because in a DynamicDrawSystem, leaves can be initialized at any time, and are not always
// present. Instead, other methods in DynamicDrawSystem.Internals & DynamicDrawSystem help quickly
// keep all folder contents up to date.
public partial class DynamicDrawSystem<T> where T : class
{
    /// <summary>
    ///     Stores the FullPath & IsOpen state of all FolderCollections & Folders to the StreamWriter.
    /// </summary>
    protected void SaveToFile(StreamWriter writer)
    {
        using var j = new JsonTextWriter(writer);
        j.Formatting = Formatting.Indented;

        // Track which folders are currently opened at the time of saving.
        var openedFolders = new List<IDynamicCollection<T>>();

        j.WriteStartObject();
        j.WritePropertyName("FolderHierarchy");
        j.WriteStartObject();

        // Iterate through all descendants, writing the paths of all Folders and FolderCollections.
        if (Root.Children.Count > 0)
        {
            // We only care about the folders.
            foreach (var folder in Root.GetAllFolderDescendants())
            {
                // Write out the full path.
                j.WritePropertyName(folder.Name);
                j.WriteValue(folder.FullPath);

                // Track if opened.
                if (folder.IsOpen)
                    openedFolders.Add(folder);
            }
        }
        j.WriteEndObject();

        // Separately, write the FullPaths of the opened folders.
        if (openedFolders.Count > 0)
        {
            j.WritePropertyName("OpenedFolders");
            j.WriteStartArray();
            foreach (var path in openedFolders)
                j.WriteValue(path.FullPath);
            j.WriteEndArray();
        }

        j.WriteEndObject();
    }

    /// <summary>
    ///     Helper function to specify the file location that we want to load our JObject from. <para />
    ///     The folders that are created reflect the passed in <paramref name="folderObjects"/>.
    /// </summary>
    protected bool LoadFile(FileInfo file, IEnumerable<T> folderObjects, Func<T, string> toFolderLabel)
    {
        JObject? jObj = null;
        // Safely load the JObject if it exists.
        if (File.Exists(file.FullName))
            Generic.Safe(() => jObj = JObject.Parse(File.ReadAllText(file.FullName)));
        // Then perform the internal load function.
        return LoadObject(jObj, folderObjects, toFolderLabel);
    }

    /// <summary>
    ///     Helper function to specify the file location that we want to load our JObject from.
    /// </summary>
    protected bool LoadFile(FileInfo file)
    {
        JObject? jObj = null;
        // Safely load the JObject if it exists.
        if (File.Exists(file.FullName))
            Generic.Safe(() => jObj = JObject.Parse(File.ReadAllText(file.FullName)));
        // Then perform the internal load function.
        return LoadObject(jObj);
    }

    /// <summary>
    ///     Generates the DynamicDrawSystem from the contents of the JObject. <para />
    ///     The folders that load in and generate are dependent on <paramref name="folderObjects"/>.
    /// </summary>
    protected bool LoadObject(JObject? jObject, IEnumerable<T> folderObjects, Func<T, string> toFolderLabel)
    {
        // Reset all data, completely.
        _idCounter = 1;
        _leafMap.Clear();
        Root.Children.Clear();
        // We cleared, so assume changes occurred.
        var changes = true;
        if (jObject != null)
        {
            // We are loading in new data, so now assume changes did not occur.
            changes = false;
            try
            {
                // If the file doesn't have this structure it should honestly be failing it anyways.
                var hierarchyData = jObject["FolderHierarchy"]?.ToObject<Dictionary<string, string>>() ?? [];
                var openedFolders = jObject["OpenedFolders"]?.ToObject<string[]>() ?? [];

                // Generate the Folder Hierarchy via the filtered folder objects.
                // Any defined through the hierarchy data that are not in folderObjects are ignored.
                foreach (var value in folderObjects)
                {
                    var label = toFolderLabel(value);
                    // If this label exists within the hierarchy, generate all folders and folderCollections for it.
                    if (hierarchyData.Remove(label, out var fullPath))
                    {
                        // If any folders in this structure are created, then _idCounter is incremented for us.
                        if (CreateAllFolders(fullPath, out _) is (Result.Success or Result.SuccessNothingDone))
                            changes = true;
                    }
                    // There is a folder that was present in folderObjects, but not in the config, so create a new folder.
                    else
                    {
                        var folder = new DynamicFolder<T>(Root, FAI.Folder, label, _idCounter + 1u);
                        // Attempt to assign the folder, and if it was created, then increment the id counter.
                        if (AssignFolder(Root, folder, out _) is not Result.ItemExists)
                        {
                            _idCounter++;
                            changes = true;
                        }
                    }
                }

                // Set the open state for all opened folders.
                foreach (var openedPath in openedFolders)
                {
                    if (FindFolder(openedPath, out var folder))
                    {
                        if (folder is DynamicFolderGroup<T> fc) fc.SetIsOpen(true);
                        else if (folder is DynamicFolder<T> f) f.SetIsOpen(true);
                    }
                    else
                        changes = true;
                }
            }
            catch
            {
                changes = true;
            }
        }

        // TODO: Mediator Invoke of file system change for Root.
        return changes;
    }

    /// <summary>
    ///     Generates the DynamicDrawSystem from the contents of the JObject.
    /// </summary>
    protected bool LoadObject(JObject? jObject)
    {
        // Reset all data, completely.
        _idCounter = 1;
        _leafMap.Clear();
        Root.Children.Clear();
        // We cleared, so assume changes occurred.
        var changes = true;
        if (jObject != null)
        {
            // We are loading in new data, so now assume changes did not occur.
            changes = false;
            try
            {
                // If the file doesn't have this structure it should honestly be failing it anyways.
                var hierarchyData = jObject["FolderHierarchy"]?.ToObject<Dictionary<string, string>>() ?? [];
                var openedFolders = jObject["OpenedFolders"]?.ToObject<string[]>() ?? [];

                // Generate all folders to all FolderPaths within the hierarchy data.
                foreach (var (folder, folderPath) in hierarchyData)
                    if (CreateAllFolders(folderPath, out _) is (Result.Success or Result.SuccessNothingDone))
                        changes = true;

                // Set the open state for all opened folders.
                foreach (var openedPath in openedFolders)
                {
                    if (FindFolder(openedPath, out var folder))
                    {
                        if (folder is DynamicFolderGroup<T> fc) fc.SetIsOpen(true);
                        else if (folder is DynamicFolder<T> f) f.SetIsOpen(true);
                    }
                    else
                        changes = true;
                }
            }
            catch
            {
                changes = true;
            }
        }

        // TODO: Mediator call here.
        return changes;
    }
}