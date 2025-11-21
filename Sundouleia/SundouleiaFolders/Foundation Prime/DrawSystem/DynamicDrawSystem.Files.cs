using CkCommons;

namespace Sundouleia.DrawSystem;

// Process Config Saving & Loading.
// ------------------------
// DDS Only stores the folder hierarchy, and the folders opened states.
// Update this overtime as we fix things.
public partial class DynamicDrawSystem<T>
{
    /// <summary>
    ///     Stores the FolderGroup Hierarchy and the paths of all opened folders in dictionary format. <para />
    ///     All of this is written into the streamWriter.
    /// </summary>
    protected void SaveToFile(StreamWriter writer)
    {
        using var j = new JsonTextWriter(writer);
        j.Formatting = Formatting.Indented;

        // Track which folders are currently opened at the time of saving.
        var rootFolders = root.GetAllFolderDescendants();
        var opened = new List<IDynamicCollection<T>>();

        j.WriteStartObject();
        // Dictionary for the DynamicCollection<T> => FullPath.
        // Dictates the hierarchy structure of the draw system.
        j.WritePropertyName("GroupHierarchy");
        j.WriteStartObject();
        foreach(var group in rootFolders.OfType<IDynamicFolderGroup<T>>())
        {
            j.WritePropertyName(group.Name);
            j.WriteValue(group.FullPath);
            if (group.IsOpen)
                opened.Add(group);
        }
        j.WriteEndObject();

        // Dictionary for the DynamicCollection<T> => FullPath.
        // Determines which groups the folders are attached to.
        j.WritePropertyName("FolderParents");
        j.WriteStartObject();
        foreach (var folder in rootFolders.OfType<IDynamicFolder<T>>())
        {
            j.WritePropertyName(folder.Name);
            j.WriteValue(folder.Parent.Name);
            if (folder.IsOpen)
                opened.Add(folder);
        }
        j.WriteEndObject();

        // Write out all folders that were opened.
        // Separately, write the FullPaths of the opened folders.
        j.WritePropertyName("OpenedCollections");
        j.WriteStartArray();
        foreach (var collection in opened)
            j.WriteValue(collection.Name);
        j.WriteEndArray();

        // End of the main object.
        j.WriteEndObject();
    }

    ///// <summary>
    /////     Helper function to specify the file location that we want to load our JObject from. <para />
    /////     The folders that are created reflect the passed in <paramref name="folderObjects"/>.
    ///// </summary>
    //protected bool LoadFile(FileInfo file, IEnumerable<T> folderObjects, Func<T, string> toFolderLabel)
    //{
    //    JObject? jObj = null;
    //    // Safely load the JObject if it exists.
    //    if (File.Exists(file.FullName))
    //        Generic.Safe(() => jObj = JObject.Parse(File.ReadAllText(file.FullName)));
    //    // Then perform the internal load function.
    //    return LoadObject(jObj, folderObjects, toFolderLabel);
    //}

    /// <summary>
    ///     Helper function to specify the file location that we want to load our JObject from. <para />
    ///     Returns the dictionary mapping all Folders to their location in the FolderHierarchy, 
    ///     and also all opened folder paths. (may change overtime)
    /// </summary>
    protected bool LoadFile(FileInfo file, out Dictionary<string, string> folderMap, out List<string> openedCollections)
    {
        JObject? jObj = null;
        // Safely load the JObject if it exists.
        if (File.Exists(file.FullName))
            Generic.Safe(() => jObj = JObject.Parse(File.ReadAllText(file.FullName)));
        // Then perform the internal load function.
        return LoadObject(jObj, out folderMap, out openedCollections);
    }

    ///// <summary>
    /////     Generates the DynamicDrawSystem from the contents of the JObject. <para />
    /////     The folders that load in and generate are dependent on <paramref name="folderObjects"/>.
    ///// </summary>
    //protected bool LoadObject(JObject? jObject, IEnumerable<T> folderObjects, Func<T, string> toFolderLabel)
    //{
    //    // Reset all data, completely.
    //    idCounter = 1;
    //    root.Children.Clear();
    //    // We cleared, so assume changes occurred.
    //    var changes = true;
    //    if (jObject != null)
    //    {
    //        // We are loading in new data, so now assume changes did not occur.
    //        changes = false;
    //        try
    //        {
    //            // If the file doesn't have this structure it should honestly be failing it anyways.
    //            var hierarchyData = jObject["FolderHierarchy"]?.ToObject<Dictionary<string, string>>() ?? [];
    //            var openedFolders = jObject["OpenedFolders"]?.ToObject<string[]>() ?? [];

    //            // Generate the Folder Hierarchy via the filtered folder objects.
    //            // Any defined through the hierarchy data that are not in folderObjects are ignored.
    //            foreach (var value in folderObjects)
    //            {
    //                var label = toFolderLabel(value);
    //                // If this label exists within the hierarchy, generate all folders and folderCollections for it.
    //                if (hierarchyData.Remove(label, out var fullPath))
    //                {
    //                    // If any folders in this structure are created, then _idCounter is incremented for us.
    //                    if (CreateAllFolders(fullPath, out _) is (Result.Success or Result.SuccessNothingDone))
    //                        changes = true;
    //                }
    //                // There is a folder that was present in folderObjects, but not in the config, so create a new folder.
    //                else
    //                {
    //                    var folder = new DynamicFolder<T>(root, FAI.Folder, label, idCounter + 1u);
    //                    // Attempt to assign the folder, and if it was created, then increment the id counter.
    //                    if (AssignFolder(root, folder) is not Result.ItemExists)
    //                    {
    //                        idCounter++;
    //                        changes = true;
    //                    }
    //                }
    //            }

    //            // Set the open state for all opened folders.
    //            foreach (var openedPath in openedFolders)
    //            {
    //                if (FindFolder(openedPath, out var folder))
    //                {
    //                    if (folder is DynamicFolderGroup<T> fc) fc.SetIsOpen(true);
    //                    else if (folder is DynamicFolder<T> f) f.SetIsOpen(true);
    //                }
    //                else
    //                    changes = true;
    //            }
    //        }
    //        catch
    //        {
    //            changes = true;
    //        }
    //    }

    //    // TODO: Mediator Invoke of file system change for Root.
    //    return changes;
    //}

    /// <summary>
    ///     Generates the DynamicDrawSystem from the contents of the JObject.
    /// </summary>
    protected bool LoadObject(JObject? jObject, out Dictionary<string, string> folderMap, out List<string> openedCollections)
    {
        // Assume initial output data is blank.
        folderMap = new Dictionary<string, string>();
        openedCollections = new List<string>();

        // Reset all data, completely.
        idCounter = 1;
        _folderMap.Clear();
        root.Children.Clear();
        // We cleared, so assume changes occurred.
        var changes = true;
        if (jObject != null)
        {
            // We are loading in new data, so now assume changes did not occur.
            changes = false;
            try
            {
                // Obtain all relevent data from the folder.
                var groupHierarchy = jObject["GroupHierarchy"]?.ToObject<Dictionary<string, string>>() ?? [];
                folderMap = jObject["FolderParents"]?.ToObject<Dictionary<string, string>>() ?? [];
                openedCollections = [ ..jObject["OpenedFolders"]?.ToObject<string[]>() ?? [] ];

                // Construct all Groups that do not already exist.
                foreach (var (groupName, groupPath) in groupHierarchy)
                {
                    // If we created any groups in this process, mark the changes are true.
                    if (CreateAllGroups(groupPath, out _) is (Result.Success or Result.SuccessNothingDone))
                        changes = true;
                }

                // Ensure all existing folders in the list are opened.
                // TODO: Add some ensureOpened here, the remainder is for unopened folders likely.
            }
            catch
            {
                changes = true;
            }
        }

        // Revise later.
        Changed?.Invoke(DDSChangeType.Reload, root, null, null);
        return changes;
    }
}