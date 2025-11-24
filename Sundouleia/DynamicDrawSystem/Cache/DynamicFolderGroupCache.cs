namespace Sundouleia.DrawSystem.Selector;

/// <summary>
///     Inherits <see cref="IDynamicCache{T}"/> 
///     for a standard <see cref="IDynamicFolderGroup{T}"/>.
/// </summary>
public class DynamicFolderGroupCache<T>(IDynamicFolderGroup<T> folder) : IDynamicCache<T> where T : class
{
    public IDynamicFolderGroup<T> Folder { get; } = folder;
    public IReadOnlyList<IDynamicCache<T>> Children { get; set; } = [];

    public bool IsEmpty
        => Folder is null;
    public IEnumerable<IDynamicNode<T>> GetChildren()
        => Children.SelectMany(c => c.GetChildren());

    IDynamicCollection<T> IDynamicCache<T>.Folder => Folder;
}