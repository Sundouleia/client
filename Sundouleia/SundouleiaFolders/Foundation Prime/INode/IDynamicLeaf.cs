
using Dalamud.Interface;

namespace Sundouleia.DrawSystem;

/// <summary>
///     Public accessor for a leaf inside a DynamicDrawSystem. <para />
///     A Leaf can only exist as a child of a <see cref="IDynamicFolder{T}"/>.
/// </summary>
/// <typeparam name="T"> The data contained within this leaf. </typeparam>
public interface IDynamicLeaf<T> : IDynamicNode where T : class
{
    /// <summary>
    ///     The parent folder of this leaf.
    /// </summary>
    public IDynamicFolder<T> Parent { get; }

    /// <summary>
    ///     The data associated with this leaf.
    /// </summary>
    public T Data { get; }
}