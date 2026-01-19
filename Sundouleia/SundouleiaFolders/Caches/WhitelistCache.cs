using CkCommons.DrawSystem;
using CkCommons.DrawSystem.Selector;
using Sundouleia.Localization;
using Sundouleia.Pairs;
using Sundouleia.PlayerClient;
using System.Reflection.Metadata;

namespace Sundouleia.DrawSystem;

// Cache for DDS's using Sundesmo items.
public class WhitelistCache(DynamicDrawSystem<Sundesmo> parent) : DynamicFilterCache<Sundesmo>(parent)
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
            parent.Move(GroupInEditor, (DynamicFolderGroup<Sundesmo>)parent.Root);
        else
            parent.Move(GroupInEditor!, (DynamicFolderGroup<Sundesmo>)newParent);
    }

    public void UpdateEditorGroupStyle() => GroupInEditor?.ApplyLatestStyle();

    public bool TryRenameNode(GroupsManager groups, string newName)
    {
        if (GroupInEditor is null)
            return false;

        if (!string.IsNullOrWhiteSpace(newName) && groups.TryRename(GroupInEditor.Group, newName))
        {
            parent.Rename(GroupInEditor, newName);
            MarkForReload(GroupInEditor.Parent);
            return true;
        }

        return false;
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