//namespace Sundouleia.DrawSystem;
//public partial class DynamicDrawSystem<T> where T : class
//{
//    public sealed class FolderCollection(FolderCollection parent, FAI icon, string name, uint id) : IDynamicWriteFolder
//    {
//        internal const string RootLabel = "root";

//        // Need to include other things like border colors ext.
//        public FolderCollection Parent   { get; internal set; } = parent;
//        public string           Name     { get; private set;  } = name;
//        public string           FullPath { get; private set;  } = string.Empty;
//        public uint             ID       { get; internal set; } = id;
//        public FolderFlags      Flags    { get; private set;  } = FolderFlags.None;

//        // These might also be included, but also remember it is for data storage.
//        // So if it is better to put in the selector, do so.
//        public uint   NameColor     { get; internal set; } = uint.MaxValue;
//        public FAI    Icon          { get; private set;  } = icon;
//        public uint   IconColor     { get; internal set; } = uint.MaxValue;
//        public uint   BgColor       { get; internal set; } = uint.MinValue;
//        public uint   BorderColor   { get; internal set; } = uint.MaxValue;

//        public readonly List<ISortMethod<IDynamicFolder>> SortSteps = [];

//        /// <summary>
//        ///     All sub-folders of this folder collection. Can be other folder collections or folders.
//        /// </summary>
//        internal readonly List<IDynamicWriteFolder> Children = [];

//        // Helpers.
//        public int TotalChildren
//            => Children.Count;

//        public bool IsRoot
//            => ID == 0;

//        public bool IsOpen
//            => Flags.HasAny(FolderFlags.Expanded);

//        public bool ShowIfEmpty
//            => Flags.HasAny(FolderFlags.ShowIfEmpty);

//        void IDynamicWriteFolder.SetParent(FolderCollection parent)
//            => Parent = parent;

//        void IDynamicWriteFolder.SetName(string name, bool fix)
//            => Name = fix ? name.FixName() : name;

//        void IDynamicWriteFolder.SortChildren(NameComparer comparer)
//            => Children.Sort(comparer);

//        void IDynamicWriteFolder.UpdateFullPath()
//            => UpdateFullPath();

//        public void SetIsOpen(bool value)
//            => Flags = value ? Flags | FolderFlags.Expanded : Flags & ~FolderFlags.Expanded;

//        public void SetShowEmpty(bool value)
//            => Flags = value ? Flags | FolderFlags.ShowIfEmpty : Flags & ~FolderFlags.ShowIfEmpty;

//        public IEnumerable<FolderCollection> GetSubFolderGroups()
//            => Children.OfType<FolderCollection>();

//        public IEnumerable<Folder> GetSubFolders()
//            => Children.OfType<Folder>();

//        // Iterate through all direct children in sort order.
//        public IEnumerable<IDynamicFolder> GetChildren()
//            => GetSortedChildren();

//        public IEnumerable<IDynamicFolder> GetAllFolderDescendants()
//        {
//            return GetChildren().SelectMany(p =>
//            {
//                if (p is FolderCollection fc)
//                    return fc.GetAllFolderDescendants().Prepend(fc);
//                else if (p is Folder f)
//                    return [ f ];
//                else
//                    return Array.Empty<IDynamicFolder>();
//            });
//        }

//        // Iterate through all Descendants in sort order, not including the folder itself.
//        public IEnumerable<IDynamicEntity> GetAllDescendants()
//        {
//            return GetChildren().SelectMany(p =>
//            {
//                if (p is FolderCollection fc)
//                    return fc.GetAllDescendants().Prepend(fc);
//                else if (p is Folder f)
//                    return f.GetChildren().Cast<IDynamicEntity>().Prepend(f);
//                else
//                    return Array.Empty<IDynamicEntity>().Append(p);
//            });
//        }

//        public override string ToString()
//            => Name;

//        // Creates the root folder collection of the dynamic folder system.
//        internal static FolderCollection CreateRoot()
//            => new(null!, FAI.Folder, RootLabel, 0);

//        internal void UpdateFullPath()
//        {
//            // Ignore updating the path for root.
//            if (IsRoot)
//                return;
//            // construct the string builder and begin concatenation.
//            var sb = new StringBuilder();
//            // call recursive concatenation across ancestors.
//            IDynamicFolder.Concat(this, sb, "//");
//            // build the string and update it.
//            FullPath = sb.ToString();
//        }

//        // Sorter
//        private IEnumerable<IDynamicFolder> GetSortedChildren()
//        {
//            if (SortSteps.Count == 0)
//                return Children.OrderBy(l => l.Name); // default alphabetical

//            // Otherwise, assume sort builder order.
//            IOrderedEnumerable<IDynamicFolder>? ordered = null;

//            foreach (var step in SortSteps)
//            {
//                if (ordered == null)
//                    ordered = Children.OrderBy(c => step.FilterCheckFunc);
//                else
//                    ordered = ordered.ThenBy(c => step.FilterCheckFunc);
//            }

//            return ordered?.ToList() ?? Children.Cast<IDynamicFolder>();
//        }

//        /// <summary>
//        ///     Attempts to Add a sorting step to the folder's sorting process.
//        /// </summary>
//        public void AddStep(ISortMethod<IDynamicFolder> step)
//        {
//            if (SortSteps.Any(s => string.Equals(s.Name, step.Name, StringComparison.Ordinal)))
//                return; // duplicate, don't add

//            SortSteps.Add(step);
//        }

//        /// <summary>
//        ///     Initial Step Injection.
//        /// </summary>
//        public void AddSteps(IEnumerable<ISortMethod<IDynamicFolder>> steps)
//        {
//            foreach (var step in steps)
//            {
//                if (SortSteps.Any(s => string.Equals(s.Name, step.Name, StringComparison.Ordinal)))
//                    continue; // duplicate, don't add
//                SortSteps.Add(step);
//            }
//        }

//        public void RemoveStep(string name)
//        {
//            if (SortSteps.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)) is { } step)
//                SortSteps.Remove(step);
//        }

//        public void MoveStep(int fromIdx, int toIdx)
//        {
//            if (fromIdx < 0 || fromIdx >= SortSteps.Count || toIdx < 0 || toIdx >= SortSteps.Count)
//                return; // out of bounds
//            var step = SortSteps[fromIdx];

//            SortSteps.RemoveAt(fromIdx);
//            SortSteps.Insert(toIdx, step);
//        }

//        public void MoveSteps(int fromIdx, int length, int newStartIdx)
//        {
//            if (fromIdx < 0 || fromIdx + length > SortSteps.Count || newStartIdx < 0 || newStartIdx > SortSteps.Count)
//                return; // out of bounds

//            var movingSteps = SortSteps.GetRange(fromIdx, length);
//            SortSteps.RemoveRange(fromIdx, length);
//            // Adjust newStartIdx if it is after the removed range
//            if (newStartIdx > fromIdx)
//                newStartIdx -= length;
//            SortSteps.InsertRange(newStartIdx, movingSteps);
//        }

//        public void ClearSteps()
//            => SortSteps.Clear();
//    }
//}