//namespace Sundouleia.DrawSystem;

//public partial class DynamicDrawSystem<T>
//{
//    /// <summary>
//    ///     A comparer that compares two file system nodes by their name only.
//    /// </summary>
//    internal readonly struct NameComparer(IComparer<ReadOnlySpan<char>> baseComparer) : IComparer<IDynamicEntity>
//    {
//        /// <summary> The base comparer used to compare the strings. </summary>
//        public IComparer<ReadOnlySpan<char>> BaseComparer
//            => baseComparer;

//        /// <inheritdoc/>
//        public int Compare(IDynamicEntity? x, IDynamicEntity? y)
//        {
//            if (ReferenceEquals(x, y))
//                return 0;
//            if (y is null)
//                return 1;
//            if (x is null)
//                return -1;

//            return baseComparer.Compare(x.Name, y.Name);
//        }
//    }

//    /// <summary>
//    ///     The default comparer used when no other comparer is specified for the FS.
//    /// </summary>
//    internal sealed class OrdinalSpanComparer : IComparer<ReadOnlySpan<char>>
//    {
//        /// <inheritdoc/>
//        public int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
//            => x.CompareTo(y, StringComparison.OrdinalIgnoreCase);
//    }

//    /// <summary> A cheap struct to avoid unnecessary allocations for comparison with nodes. </summary>
//    /// <param name="comparer"> The comparer to use. </param>
//    /// <param name="name"> The name to compare. </param>
//    internal readonly ref struct SearchNode(NameComparer comparer, ReadOnlySpan<char> name) : IComparable<IDynamicEntity>
//    {
//        private readonly IComparer<ReadOnlySpan<char>> _comparer = comparer.BaseComparer;
//        private readonly ReadOnlySpan<char> _name = name;

//        /// <inheritdoc/>
//        public int CompareTo(IDynamicEntity? other)
//        {
//            if (other is null)
//                return _comparer.Compare(_name, []);

//            return _comparer.Compare(_name, other.Name);
//        }
//    }
//}