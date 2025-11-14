//using System.Collections.ObjectModel;
//using System.Linq;

//namespace Sundouleia.DrawSystem;

//public partial class DynamicDrawSystem<T> where T : class
//{

//    // Maybe add a T data for the folder info, or add it as a configurable
//    // record / struct or sub-class for additional configuration.
//    public class Folder : IDynamicWriteFolder
//    {
//        public FolderCollection Parent   { get; internal set; }
//        public string           Name     { get; private set;  }
//        public string           FullPath { get; private set;  }
//        public uint             ID       { get; internal set; }
//        public FolderFlags      Flags    { get; private set;  } = FolderFlags.None;

//        public Folder(FolderCollection parent, FAI icon, string name, uint id)
//        {
//            Parent = parent;
//            Name = name;
//            ID = id;
//        }

//        // could add overrides for preset flags and other customizations.

//        // These might also be included, but also remember it is for data storage.
//        // So if it is better to put in the selector, do so.
//        public uint   NameColor     { get; internal set; } = uint.MaxValue;
//        public FAI    Icon          { get; private set;  }
//        public uint   IconColor     { get; internal set; } = uint.MaxValue;
//        public uint   BgColor       { get; internal set; } = uint.MinValue;
//        public uint   BorderColor   { get; internal set; } = uint.MaxValue;

//        public readonly List<ISortMethod<T>> Steps = [];

//        internal readonly List<Leaf> Children = [];

//        // Helpers.
//        public int TotalChildren
//            => Children.Count;

//        public bool IsRoot
//            => false;

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

//        // Iterate through all direct children in sort order.
//        // We do not ever need to perform 'GetAllDescendants' as folders ONLY contain child objects.
//        public IEnumerable<IDynamicLeaf> GetChildren()
//        {
//            if (Steps.Count == 0)
//                return Children.OrderBy(l => l.Name).ToList(); // default alphabetical

//            // Otherwise, assume sort builder order.
//            IOrderedEnumerable<Leaf>? ordered = null;

//            foreach (var step in Steps)
//            {
//                if (ordered == null)
//                    ordered = Children.OrderBy(c => step.FilterCheckFunc(c.Data));
//                else
//                    ordered = ordered.ThenBy(c => step.FilterCheckFunc(c.Data));
//            }

//            return ordered?.ToList() ?? Children.ToList();
//        }

//        public override string ToString()
//            => Name;

//        internal void UpdateFullPath()
//        {
//            // construct the string builder and begin concatenation.
//            var sb = new StringBuilder();
//            // call recursive concatenation across ancestors.
//            IDynamicFolder.Concat(this, sb, "/");
//            // build the string and update it.
//            FullPath = sb.ToString();
//        }

//        public void AddStep(ISortMethod<T> step)
//        {
//            if (Steps.Any(s => string.Equals(s.Name, step.Name, StringComparison.Ordinal)))
//                return; // duplicate, don't add

//            Steps.Add(step);
//        }

//        public void AddSteps(IEnumerable<ISortMethod<T>> steps)
//        {
//            foreach (var step in steps)
//            {
//                if (Steps.Any(s => string.Equals(s.Name, step.Name, StringComparison.Ordinal)))
//                    continue; // duplicate, don't add
//                Steps.Add(step);
//            }
//        }

//        public void RemoveStep(string name)
//        {
//            if (Steps.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)) is { } step)
//                Steps.Remove(step);;
//        }

//        public void MoveStep(int fromIdx, int toIdx)
//        {
//            if (fromIdx < 0 || fromIdx >= Steps.Count || toIdx < 0 || toIdx >= Steps.Count)
//                return; // out of bounds
//            var step = Steps[fromIdx];
            
//            Steps.RemoveAt(fromIdx);
//            Steps.Insert(toIdx, step);
//        }

//        public void MoveSteps(int fromIdx, int length, int newStartIdx)
//        {
//            if (fromIdx < 0 || fromIdx + length > Steps.Count || newStartIdx < 0 || newStartIdx > Steps.Count)
//                return; // out of bounds

//            var movingSteps = Steps.GetRange(fromIdx, length);
//            Steps.RemoveRange(fromIdx, length);
//            // Adjust newStartIdx if it is after the removed range
//            if (newStartIdx > fromIdx)
//                newStartIdx -= length;
//            Steps.InsertRange(newStartIdx, movingSteps);
//        }

//        public void ClearSteps()
//            => Steps.Clear();
//    }
//}