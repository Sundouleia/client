//using Dalamud.Bindings.ImGui;

//namespace Sundouleia.DrawSystem;
//public partial class DynamicDrawSystem<T> where T : class
//{
//    /// <summary>
//    ///     A list item entity contained within a folder. <para />
//    ///     FolderCollections cannot contain these, and a leaf must always belong to a folder.
//    /// </summary>
//    public sealed class Leaf : IDynamicWriteLeaf
//    {
//        /// <summary>
//        ///     The data associated with this leaf of the DynamicSystem's type <typeparamref name="T"/>
//        /// </summary>
//        public T Data { get; }
//        public Folder Parent { get; internal set; }
//        public string Name { get; private set; } = string.Empty;
//        // If these serve no purpose for selection, remove them (for leaves)
//        public string FullPath { get; private set; } = string.Empty;
//        public uint ID { get; }

//        internal Leaf(Folder parent, string name, T data, uint id)
//        {
//            Parent = parent;
//            Data = data;
//            SetName(name);
//            ID = id;
//            UpdateFullPath();
//        }

//        public override string ToString()
//            => Name;

//        // Updates the linked parent folder for this leaf item.
//        void IDynamicWriteLeaf.SetParent(Folder parent)
//            => Parent = parent;

//        // Updates the name of this leaf item.
//        void IDynamicWriteLeaf.SetName(string name, bool fix)
//            => SetName(name, fix);

//        void IDynamicWriteLeaf.UpdateFullPath()
//            => UpdateFullPath();

//        // Updates the name of this leaf item.
//        internal void SetName(string name, bool fix = true)
//            => Name = fix ? name.FixName() : name;

//        internal void UpdateFullPath()
//            => FullPath = $"{Parent.FullPath}/{Name}";
//    }
//}