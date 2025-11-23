using System.Diagnostics.CodeAnalysis;

namespace Sundouleia.DrawSystem.Selector;


public class DynamicFilterCache<T> where T : class
{
    // For obtaining root.
    private readonly DynamicDrawer<T> _drawer;

    // TODO;

}


// Can pivot to an abstract base if necessary, but abstracts dont play nice with rendering performance.
// (Specifically if you interact with the abstract variables during draw frames.
//  Accessing only inherited parent variables seems to work fine)

    // Normally I would not use this cheap empty interface cache trick to merge both types, however, 
public interface ICachedFolderNode<T> where T : class
{
    IDynamicCollection<T> Folder { get; }
    public bool IsEmpty { get; }

    public IEnumerable<IDynamicNode<T>> GetChildren();
}

public class CachedFolder<T>(IDynamicFolder<T> folder) : ICachedFolderNode<T> where T : class
{
    public IDynamicFolder<T> Folder { get; } = folder;
    public IReadOnlyList<IDynamicLeaf<T>> Children { get; set; } = [];

    public bool IsEmpty
        => Folder is null;
    public IEnumerable<IDynamicNode<T>> GetChildren()
        => Children;

    IDynamicCollection<T> ICachedFolderNode<T>.Folder => Folder;
}

public class CachedFolderGroup<T>(IDynamicFolderGroup<T> folder) : ICachedFolderNode<T> where T : class
{
    public IDynamicFolderGroup<T> Folder { get; } = folder;
    public IReadOnlyList<ICachedFolderNode<T>> Children { get; set; } = [];

    public bool IsEmpty
        => Folder is null;
    public IEnumerable<IDynamicNode<T>> GetChildren()
        => Children.SelectMany(c => c.GetChildren());

    IDynamicCollection<T> ICachedFolderNode<T>.Folder => Folder;
}