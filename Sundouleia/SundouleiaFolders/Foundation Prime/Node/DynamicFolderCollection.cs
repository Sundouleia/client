
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

internal sealed class DynamicFolderCollection<T> : IDynamicFolderCollection where T : class
{
    public DynamicFolderCollection<T> Parent { get; internal set; }
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
    public uint GradientColor { get; internal set; } = ColorHelpers.Fade(uint.MaxValue, .9f);

    internal List<ISortMethod<IDynamicFolderNode>> Sorter = [];
    internal List<IDynamicFolderNode> Children = [];

    public DynamicFolderCollection(DynamicFolderCollection<T> parent, FAI icon, string name, uint id)
    {
        Parent = parent;
        Icon = icon;
        Name = name.FixName();
        ID = id;
        UpdateFullPath();
    }

    public int TotalChildren => Children.Count;
    public bool IsRoot => ID == 0;
    public bool IsOpen => Flags.HasAny(FolderFlags.Expanded);
    public bool ShowIfEmpty => Flags.HasAny(FolderFlags.ShowIfEmpty);

    // Can probably be removed.
    internal void SetParent(DynamicFolderCollection<T> parent)
        => Parent = parent;

    internal void SetName(string name, bool fix)
        => Name = fix ? name.FixName() : name;

    // Can probably be removed.
    internal void SortChildren(NameComparer comparer)
        => Children.Sort(comparer);

    internal void SetIsOpen(bool value)
        => Flags = value ? Flags | FolderFlags.Expanded : Flags & ~FolderFlags.Expanded;

    internal void SetShowEmpty(bool value)
        => Flags = value ? Flags | FolderFlags.ShowIfEmpty : Flags & ~FolderFlags.ShowIfEmpty;

    internal IEnumerable<DynamicFolderCollection<T>> GetSubFolderGroups()
        => Children.OfType<DynamicFolderCollection<T>>();

    internal IEnumerable<DynamicFolder<T>> GetSubFolders()
        => Children.OfType<DynamicFolder<T>>();

    internal IEnumerable<IDynamicFolderNode> GetAllFolderDescendants()
    {
        return Children.SelectMany(p =>
        {
            if (p is DynamicFolderCollection<T> fc)
                return fc.GetAllFolderDescendants().Prepend(fc);
            else if (p is DynamicFolder<T> f)
                return [f];
            else
                return Array.Empty<IDynamicFolderNode>();
        });
    }

    // Iterate through all Descendants in sort order, not including the folder itself.
    internal IEnumerable<IDynamicNode> GetAllDescendants()
    {
        return Children.SelectMany(p =>
        {
            if (p is DynamicFolderCollection<T> fc)
                return fc.GetAllDescendants().Prepend(fc);
            else if (p is DynamicFolder<T> f)
                return f.Children.Cast<IDynamicNode>().Prepend(f);
            else
                return Array.Empty<IDynamicNode>().Append(p);
        });
    }

    internal void UpdateFullPath()
    {
        if (IsRoot) return;
        // construct the string builder and begin concatenation.
        var sb = new StringBuilder();
        // call recursive concatenation across ancestors.
        IDynamicFolderNode.Concat(this, sb, "//");
        // build the string and update it.
        FullPath = sb.ToString();
    }

    public IReadOnlyList<IDynamicFolderNode> GetAncestors()
    {
        if (Parent is null || Parent.IsRoot)
            return Array.Empty<IDynamicFolderNode>();

        var ancestors = new List<IDynamicFolderNode>();
        for (IDynamicFolderNode cur = Parent; !cur.IsRoot; cur = cur.Parent)
            ancestors.Add(cur);

        return ancestors;
    }

    public override string ToString()
        => Name;

    // Creates the root folder collection of the dynamic folder system.
    internal static DynamicFolderCollection<T> CreateRoot()
        => new(null!, FAI.Folder, string.Empty, 0);

    // Interface Satisfiers.
    IDynamicFolderCollection IDynamicFolderNode.Parent => Parent;
    IReadOnlyList<ISortMethod<IDynamicFolderNode>> IDynamicFolderCollection.Sorter => Sorter;
    IReadOnlyList<IDynamicFolderNode> IDynamicFolderCollection.Children => Children;
}