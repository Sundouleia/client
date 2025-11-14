
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

/// <summary>
///     Flags that determine a folder's display behavior.
/// </summary>
[Flags]
public enum FolderFlags : byte
{
    None = 0,
    Expanded = 1 << 0,
    ShowIfEmpty = 1 << 1,
}

/// <summary>
///     Public accessor for a folder collection inside a DynamicDrawSystem. <para />
///     Children can be either <see cref="IDynamicFolderCollection"/>s or <see cref="IDynamicFolder{T}"/>s.
/// </summary>
/// <remarks> A FolderCollection can be the Root folder if <see cref="IDynamicNode.Name"/> is string.Empty </remarks>
public interface IDynamicFolderCollection : IDynamicFolderNode
{
    /// <summary>
    ///     The FolderCollections and Folders contained by this collection, exposed for read-only access.
    /// </summary>
    public IReadOnlyList<IDynamicFolderNode> Children { get; }

    /// <summary>
    ///     The instructions to be processed by any drawer wishing 
    ///     to display the folder in a sorted manner.
    /// </summary>
    public IReadOnlyList<ISortMethod<IDynamicFolderNode>> Sorter { get; }
}

/// <summary>
///     Public accessor for a folder inside a DynamicDrawSystem. <para />
///     Children are always <see cref="IDynamicLeaf{T}"/>s.
/// </summary>
/// <typeparam name="T"> The data type contained within the leaves of this folder. </typeparam>
public interface IDynamicFolder<T> : IDynamicFolderNode where T : class
{
    /// <summary>
    ///     The Leaves contained by this folder, exposed for read-only access.
    /// </summary>
    public IReadOnlyList<IDynamicLeaf<T>> Children { get; }

    /// <summary>
    ///     The instructions to be processed by any drawer wishing 
    ///     to display the folder in a sorted manner.
    ///     (Maybe change this to let both FolderCollections and Folders share a common ISortMethod?)
    /// </summary>
    public IReadOnlyList<ISortMethod<T>> Sorter { get; }
}

/// <summary>
///     Public accessor for a folder node inside a DynamicDrawSystem. <para />
///     Can be either a <see cref="IDynamicFolderCollection"/> or a <see cref="IDynamicFolder{T}"/>. <para />
///     The Parent of a folder node must be a <see cref="IDynamicFolderCollection"/>.
/// </summary>
public interface IDynamicFolderNode : IDynamicNode
{
    /// <summary>
    ///     The parent folder of this folder.
    /// </summary>
    public IDynamicFolderCollection Parent { get; }

    /// <summary>
    ///     Associated Flags.
    /// </summary>
    public FolderFlags Flags { get; }

    /// <summary>
    ///     The color on the folder label when drawn.
    /// </summary>
    public uint NameColor { get; }

    /// <summary>
    ///     The FontAwesomeIcon 5 icon associated with this folder.
    /// </summary>
    public FAI Icon { get; }

    /// <summary>
    ///     The color of the icon when drawn.
    /// </summary>
    public uint IconColor { get; }

    /// <summary>
    ///     The background color of the folder when drawn.
    /// </summary>
    public uint BgColor { get; }

    /// <summary>
    ///     The border color of the folder when drawn.
    /// </summary>
    public uint BorderColor { get; }

    /// <summary>
    ///     The top gradient color used in the background fade effect behind the children when expanded.
    /// </summary>
    public uint GradientColor { get; }

    /// <summary>
    ///     How many child nodes are contained within the folder. (Does not include nested folder's children)
    /// </summary>
    public int TotalChildren { get; }

    /// <summary>
    ///     If the folder is expanded.
    /// </summary>
    public bool IsOpen { get; }

    /// <summary>
    ///     If this folder should render even when 0 children are present. (A helper for drawers)
    /// </summary>
    public bool ShowIfEmpty { get; }

    internal static bool Concat(IDynamicFolderNode path, StringBuilder sb, string separator)
    {
        if (path.IsRoot)
            return false;

        if (Concat(path.Parent, sb, separator))
            sb.Append(separator);
        sb.Append(path.Name);
        return true;
    }
}