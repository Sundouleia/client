using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

/// <summary>
///     Publicly accessible Folder for a DynamicDrawSystem node. <para />
///     All functions and setters are only accessible internally to ensure integrity.
/// </summary>
public class DynamicFolder<T> : IDynamicFolder<T> where T : class
{
    public DynamicFolderGroup<T> Parent { get; internal set; }
    public uint ID { get; }
    public string Name { get; internal set; }
    public string FullPath { get; internal set; } = string.Empty;
    public FolderFlags Flags { get; internal set; } = FolderFlags.None;

    // Stylizations.
    public uint NameColor { get; internal set; } = uint.MaxValue;
    public FAI Icon { get; internal set; } = FAI.Folder;
    public uint IconColor { get; internal set; } = uint.MaxValue;
    public uint BgColor { get; internal set; } = uint.MinValue;
    public uint BorderColor { get; internal set; } = uint.MaxValue;
    public uint GradientColor { get; internal set; } = ColorHelpers.Fade(uint.MaxValue, .9f);

    // maybe make readonly.
    internal List<ISortMethod<T>> Sorter = [];
    internal List<DynamicLeaf<T>> Children = [];

    public DynamicFolder(DynamicFolderGroup<T> parent, FAI icon, string name, uint id)
    {
        Parent = parent;
        Icon = icon;
        Name = name.FixName();
        ID = id;
        UpdateFullPath();
    }

    IReadOnlyList<DynamicLeaf<T>> IDynamicFolder<T>.Children => Children;
    IReadOnlyList<ISortMethod<T>> IDynamicFolder<T>.Sorter => Sorter;

    public int TotalChildren
        => Children.Count;

    public bool IsRoot
        => ID == 0;

    public bool IsOpen
        => Flags.HasAny(FolderFlags.Expanded);

    public bool ShowIfEmpty
        => Flags.HasAny(FolderFlags.ShowIfEmpty);

    public override string ToString()
        => Name;

    public IReadOnlyList<IDynamicCollection<T>> GetAncestors()
    {
        if (Parent is null || Parent.IsRoot)
            return Array.Empty<IDynamicCollection<T>>();

        var ancestors = new List<IDynamicCollection<T>>();
        for (IDynamicCollection<T> cur = Parent; !cur.IsRoot; cur = cur.Parent)
            ancestors.Add(cur);

        return ancestors;
    }

    internal void SetParent(DynamicFolderGroup<T> parent)
    => Parent = parent;

    internal void SetName(string name, bool fix)
        => Name = fix ? name.FixName() : name;

    internal void SortChildren(NameComparer comparer)
        => Children.Sort(comparer);

    internal void SetIsOpen(bool value)
        => Flags = value ? Flags | FolderFlags.Expanded : Flags & ~FolderFlags.Expanded;

    internal void SetShowEmpty(bool value)
        => Flags = value ? Flags | FolderFlags.ShowIfEmpty : Flags & ~FolderFlags.ShowIfEmpty;

    internal void UpdateFullPath()
    {
        // construct the string builder and begin concatenation.
        var sb = new StringBuilder();
        // call recursive concatenation across ancestors.
        IDynamicCollection<T>.Concat(this, sb, "/");
        // build the string and update it.
        FullPath = sb.ToString();
    }
}