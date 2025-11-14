
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

internal class DynamicLeaf<T> : IDynamicLeaf<T> where T : class
{
    public DynamicFolder<T> Parent { get; internal set; }
    public uint   ID        { get; }
    public T      Data      { get; internal set; }
    public string Name      { get; private set; }
    public string FullPath  { get; private set; } = string.Empty;

    public DynamicLeaf(DynamicFolder<T> parent, string name, T data, uint id)
    {
        Parent = parent;
        Data = data;
        Name = name.FixName();
        ID = id;
        UpdateFullPath();
    }

    public bool IsRoot => false;

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

    internal void SetParent(DynamicFolder<T> parent)
        => Parent = parent;

    internal void SetName(string name, bool fix)
        => Name = fix ? name.FixName() : name;

    internal void UpdateFullPath()
        => FullPath = $"{Parent.FullPath}/{Name}";

    // Interface Satisfiers.
    IDynamicFolder<T> IDynamicLeaf<T>.Parent => Parent;
}