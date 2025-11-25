using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Sundouleia.DrawSystem.Selector;
using Sundouleia.Services;

namespace Sundouleia.DrawSystem;

/// <summary>
///     Publicly accessible Folder for a DynamicDrawSystem node. <para />
///     All functions and setters are only accessible internally to ensure integrity.
/// </summary>
public class DynamicFolderGroup<T> : IDynamicFolderGroup<T> where T : class
{
    public DynamicFolderGroup<T> Parent { get; internal set; }
    public uint         ID       { get; }
    public string       Name     { get; internal set; }
    public string       FullPath { get; internal set; } = string.Empty;
    public FolderFlags  Flags    { get; internal set; } = FolderFlags.None;

    // Stylizations.
    public uint NameColor     { get; internal set; } = uint.MaxValue;
    public FAI  Icon          { get; internal set; }
    public uint IconColor     { get; internal set; } = uint.MaxValue;
    public uint BgColor       { get; internal set; } = uint.MinValue;
    public uint BorderColor   { get; internal set; } = uint.MaxValue;
    public uint GradientColor { get; internal set; } = ColorHelpers.Fade(ImGui.GetColorU32(ImGuiCol.TextDisabled), .9f);

    internal DynamicSorter<IDynamicCollection<T>> Sorter;
    internal List<IDynamicCollection<T>> Children = [];

    internal DynamicFolderGroup(DynamicFolderGroup<T> parent, FAI icon, string name, uint id,
        DynamicSorter<IDynamicCollection<T>>? sorter = null, FolderFlags flags = FolderFlags.None)
    {
        Parent = parent;
        Icon = icon;
        Name = name.FixName();
        ID = id;
        Flags = flags;
        Sorter = sorter ?? new();
        UpdateFullPath();
    }

    IReadOnlyDynamicSorter<IDynamicCollection<T>> IDynamicFolderGroup<T>.Sorter => Sorter;
    IReadOnlyList<IDynamicCollection<T>> IDynamicFolderGroup<T>.Children => Children;

    public int TotalChildren => Children.Count;
    public bool IsRoot => ID == 0;
    public bool IsOpen => Flags.HasAny(FolderFlags.Expanded);
    public bool ShowIfEmpty => Flags.HasAny(FolderFlags.ShowIfEmpty);

    public IEnumerable<DynamicFolderGroup<T>> GetSubFolderGroups()
        => Children.OfType<DynamicFolderGroup<T>>();

    public IEnumerable<DynamicFolder<T>> GetSubFolders()
        => Children.OfType<DynamicFolder<T>>();

    public IEnumerable<IDynamicCollection<T>> GetAllFolderDescendants()
    {
        return Children.SelectMany(p =>
        {
            if (p is DynamicFolderGroup<T> fc)
                return fc.GetAllFolderDescendants().Prepend(fc);
            else if (p is DynamicFolder<T> f)
                return [ f ];
            else
                return Array.Empty<IDynamicCollection<T>>();
        });
    }

    // Iterate through all Descendants in sort order, not including the folder itself.
    public IEnumerable<IDynamicNode<T>> GetAllDescendants()
    {
        return Children.SelectMany(p =>
        {
            if (p is DynamicFolderGroup<T> fc)
                return fc.GetAllDescendants().Prepend(fc);
            else if (p is DynamicFolder<T> f)
                return f.Children.Cast<IDynamicNode<T>>().Prepend(f);
            else
                return Array.Empty<IDynamicNode<T>>().Append(p);
        });
    }

    public IReadOnlyList<IDynamicCollection<T>> GetAncestors()
    {
        if (Parent is null || Parent.IsRoot)
            return Array.Empty<IDynamicCollection<T>>();

        var ancestors = new List<IDynamicCollection<T>>();
        for (IDynamicCollection<T> cur = Parent; !cur.IsRoot; cur = cur.Parent)
            ancestors.Add(cur);

        return ancestors;
    }

    public override string ToString()
        => Name;

    /// <summary>
    ///     Appends a child to the folder's children, forcing a sort if desired. <para />
    ///     Parent and FullPath updates are handled for you.
    /// </summary>
    /// <param name="child"> the child to add. </param>
    /// <param name="forceSort"> if the children should be sorted after assignment. </param>
    /// <returns> True if the child was added, false otherwise. </returns>
    internal bool AddChild(IDynamicCollection<T> child, bool forceSort = false)
    {
        if (Children.Contains(child))
            return false;

        Children.Add(child);
        if (child is DynamicFolderGroup<T> fc)
        {
            fc.Parent = this;
            fc.UpdateFullPath();
        }
        else if (child is DynamicFolder<T> f)
        {
            f.Parent = this;
            f.UpdateFullPath();
        }
        // If we have auto-sort enabled, sort the children.
        if (forceSort || Flags.HasAny(FolderFlags.AutoSort))
            SortChildren();

        return true;
    }

    internal void SetName(string name, bool fix, bool forceSort = false)
    {
        Name = fix ? name.FixName() : name;
        UpdateFullPath();
        // Sort the parents children if desired.
        if (forceSort || Parent.Flags.HasAny(FolderFlags.AutoSort))
            Parent.SortChildren();
    }

    /// <summary>
    ///     Sorts all children by name. Be Careful to only call when nessisary.
    /// </summary>
    internal void SortChildren()
        => Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

    internal void SetIsOpen(bool value)
        => Flags = value ? Flags | FolderFlags.Expanded : Flags & ~FolderFlags.Expanded;

    internal void SetShowEmpty(bool value)
        => Flags = value ? Flags | FolderFlags.ShowIfEmpty : Flags & ~FolderFlags.ShowIfEmpty;

    internal void SetAutoSort(bool value)
        => Flags = value ? Flags | FolderFlags.AutoSort : Flags & ~FolderFlags.AutoSort;

    internal void UpdateFullPath()
    {
        if (IsRoot) return;
        // construct the string builder and begin concatenation.
        var sb = new StringBuilder();
        // call recursive concatenation across ancestors.
        IDynamicCollection<T>.Concat(this, sb, "//");
        // build the string and update it.
        FullPath = sb.ToString();
    }

    // Creates the root folder collection of the dynamic folder system.
    internal static DynamicFolderGroup<T> CreateRoot(IEnumerable<ISortMethod<IDynamicCollection<T>>>? sorter = null)
        => new(null!, FAI.Folder, string.Empty, 0, new(sorter ?? []), FolderFlags.Expanded | FolderFlags.ShowIfEmpty);
}