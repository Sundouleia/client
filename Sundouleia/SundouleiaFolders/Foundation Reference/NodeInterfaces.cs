//using System.Collections.ObjectModel;

//namespace Sundouleia.DrawSystem;

///// <summary>
/////     Flags that determine a folder's display behavior.
///// </summary>
//[Flags]
//public enum FolderFlags : byte
//{
//    None = 0,
//    Expanded = 1 << 0,
//    ShowIfEmpty = 1 << 1,
//}

//public partial class DynamicDrawSystem<T>
//{
//    /// <summary>
//    ///     Contract for any DynamicFolder that can be modified.
//    /// </summary>
//    internal interface IDynamicWriteFolder : IDynamicFolder
//    {
//        /// <summary>
//        ///     Set the parent folder of this entity.
//        /// </summary>
//        public void SetParent(FolderCollection parent);

//        /// <summary>
//        ///     Update the name of this entity.
//        /// </summary>
//        public void SetName(string name, bool fix = true);

//        /// <summary>
//        ///     Updates the full path of the filesystem entity.
//        /// </summary>
//        public void UpdateFullPath();

//        /// <summary>
//        ///     Sorts the children of the folder based on their current sorting method. <para />
//        ///     For the base file system, this should only need to be done by name comparers. <para />
//        ///     Maybe update this overtime to integrate the comparer into the folder itself or something.
//        /// </summary>
//        public void SortChildren(NameComparer comparer);

//        /// <summary>
//        ///     Set if the folder should display its contents when empty or not.
//        /// </summary>
//        public void SetShowEmpty(bool value);

//        /// <summary>
//        ///     Set if the folder should be opened or closed.
//        /// </summary>
//        public void SetIsOpen(bool value);

//        // Maybe move the rest of these into the folder and folder collections if its too weird.
//        // (Type collection hell, unless we did ILeafSorterStep<T> : ILeafSorterStep but idk)
//        /// <summary>
//        ///     Bomb a step by its name.
//        /// </summary>
//        public void RemoveStep(string name);

//        /// <summary>
//        ///     Move a step from one index to another.
//        /// </summary>
//        public void MoveStep(int fromIdx, int toIdx);

//        /// <summary>
//        ///     Takes a series of indices and moves them to start at newStartIdx, preserving order.
//        /// </summary>
//        public void MoveSteps(int fromIdx, int length, int newStartIdx);

//        /// <summary>
//        ///     Bombs all sorting steps.
//        /// </summary>
//        public void ClearSteps();
//    }

//    /// <summary>
//    ///     Contract for any DynamicEntity that can be modified.
//    /// </summary>
//    internal interface IDynamicWriteLeaf : IDynamicLeaf
//    {
//        /// <summary>
//        ///     Set the parent folder of this entity.
//        /// </summary>
//        public void SetParent(Folder parent);

//        /// <summary>
//        ///     Update the name of this entity.
//        /// </summary>
//        public void SetName(string name, bool fix = true);

//        /// <summary>
//        ///     Updates the full path of the filesystem entity.
//        /// </summary>
//        public void UpdateFullPath();
//    }

//    /// <summary>
//    ///     Contract for everything a Folder or FolderCollection (or other inherited type) should have.
//    /// </summary>
//    public interface IDynamicFolder : IDynamicEntity
//    {
//        /// <summary>
//        ///     A FolderCollection must always be the parent of any IDrawFolder.
//        /// </summary>
//        public FolderCollection Parent { get; }

//        /// <summary>
//        ///     Associated Flags.
//        /// </summary>
//        public FolderFlags Flags { get; }

//        // Customizations.
//        public uint NameColor { get; }
//        public FAI Icon { get; }
//        public uint IconColor { get; }
//        public uint BgColor { get; }
//        public uint BorderColor { get; }

//        public bool IsRoot { get; }

//        /// <summary>
//        ///     Get if the folder is in expanded/open state.
//        /// </summary>
//        public bool IsOpen { get; }

//        /// <summary>
//        ///     If the folder should show its contents when it contains no children.
//        /// </summary>
//        public bool ShowIfEmpty { get; }

//        internal static bool Concat(IDynamicFolder path, StringBuilder sb, string separator)
//        {
//            if (path.IsRoot)
//                return false;

//            if (Concat(path.Parent, sb, separator))
//                sb.Append(separator);
//            sb.Append(path.Name);
//            return true;
//        }
//    }

//    /// <summary>
//    ///     What all Leaf Entities must have.
//    /// </summary>
//    public interface IDynamicLeaf : IDynamicEntity
//    {
//        /// <summary>
//        ///     Concrete-Defined parent must be a Folder for a Leaf entity
//        /// </summary>
//        public Folder Parent { get; }

//        public T Data { get; }
//    }

//    /// <summary>
//    ///     What anything in the dynamic folder system must have.
//    /// </summary>
//    public interface IDynamicEntity
//    {
//        /// <summary>
//        ///     Uniquely assigned ID for this entity. Useful for popup's, draw IDs, ext.
//        /// </summary>
//        public uint ID { get; }

//        /// <summary>
//        ///     The name of this item, usually for displays, or search matching.
//        /// </summary>
//        public string Name { get; }

//        /// <summary>
//        ///     The full path of this entity within the fileSystem.
//        /// </summary>
//        public string FullPath { get; }
//    }
//}