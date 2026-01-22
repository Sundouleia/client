using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using Sundesmo items.
public class WhitelistCache(DynamicDrawSystem<Sundesmo> dds) : DynamicFilterCache<Sundesmo>(dds)
{
    /// <summary>
    ///     If the config options under the filter bar should show.
    /// </summary>
    public bool FilterConfigOpen = false;

    /// <summary>
    ///     The Nodes that display MonoFont UIDs instead of DisplayName.
    /// </summary>
    public HashSet<IDynamicNode<Sundesmo>> ShowingUID = new();

    /// <summary>
    ///     The node currently being renamed, if any.
    /// </summary>
    public IDynamicNode<Sundesmo>? RenamingNode = null;

    /// <summary>
    ///     Temp nick text.
    /// </summary>
    public string NameEditStr = string.Empty;

    /// <summary>
    ///     A folder or folderGroup currently being created or edited.
    /// </summary>
    public GroupFolder? GroupInEditor = null;

    public void ChangeParentNode(IDynamicFolderGroup<Sundesmo>? newParent)
    {
        if (GroupInEditor is null)
            return;

        if (newParent is null || newParent.IsRoot)
            dds.Move(GroupInEditor, (DynamicFolderGroup<Sundesmo>)dds.Root);
        else
            dds.Move(GroupInEditor!, (DynamicFolderGroup<Sundesmo>)newParent);
    }

    public void UpdateEditorGroupStyle() => GroupInEditor?.ApplyLatestStyle();

    public void UpdateEditorGroupState()
    {
        if (GroupInEditor is null)
            return;

        dds.UpdateFolder(GroupInEditor);
        MarkForReload(GroupInEditor, true);
    }

    public bool TryRenameNode(GroupsManager groups, string newName)
    {
        if (GroupInEditor is null)
            return false;

        // Clear renaming node regardless.
        RenamingNode = null;
        // Do nothing for empty names.
        if (string.IsNullOrWhiteSpace(newName))
            return false;
        // If this is caught, then another item with the same name exists, and we should not process it.
        try
        {
            dds.Rename(GroupInEditor, newName);
        }
        catch (Bagagwa)
        {
            Svc.Logger.Warning($"Another Group or Folder already has the name '{newName}'");
        }
        // Was successful, so rename the group, and mark the filtercache for reload.
        groups.TryRename(GroupInEditor.Group, newName);
        MarkForReload(GroupInEditor, true);
        return true;
    }

    protected override bool IsVisible(IDynamicNode<Sundesmo> node)
    {
        if (Filter.Length is 0)
            return true;

        // If a folder, sort by name, but also run through a second kind of
        // filter if show preferred folders is active or something.

        if (node is DynamicLeaf<Sundesmo> leaf)
            return leaf.Data.UserData.AliasOrUID.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || (leaf.Data.GetNickname()?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (leaf.Data.PlayerName?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false);

        return base.IsVisible(node);
    }
}