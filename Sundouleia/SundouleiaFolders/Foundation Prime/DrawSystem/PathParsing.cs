namespace Sundouleia.DrawSystem;

// Helps with transferring from config written path hierarchy to dynamic file system structure.
public partial class DynamicDrawSystem<T> where T : class
{
    public enum SegmentType
    {
        FolderCollection,
        Folder,
        Leaf
    }

    public static List<(string Name, SegmentType Type)> ParseSegments(string path)
    {
        var segments = new List<(string Name, SegmentType Type)>();
        if (path.Length is 0)
            return segments;

        SegmentType lastType = SegmentType.FolderCollection; // Root is always FolderCollection

        while (path.Length > 0)
        {
            int idxSingle = path.IndexOf('/');
            bool isDouble = idxSingle >= 0 && idxSingle + 1 < path.Length && path[idxSingle + 1] == '/';
            int sepIndex = idxSingle;

            string segment;
            if (sepIndex < 0)
            {
                segment = path;
                path = string.Empty;
            }
            else
            {
                segment = path[..sepIndex];
                path = path[(sepIndex + (isDouble ? 2 : 1))..].TrimStart();
            }

            segment = segment.Trim();
            if (segment.Length is 0)
                continue;

            // Determine type based on previous segment
            SegmentType type = lastType switch
            {
                SegmentType.FolderCollection => isDouble ? SegmentType.FolderCollection : SegmentType.Folder,
                SegmentType.Folder => SegmentType.Folder,
                _ => SegmentType.FolderCollection
            };

            segments.Add((segment, type));
            lastType = type;

            if (isDouble && segments.Count >= 1)
                segments[^1] = (segments[^1].Name, SegmentType.FolderCollection);
        }

        // Mark last as Leaf if parent is Folder
        if (segments.Count >= 2)
        {
            var parent = segments[^2];
            var last = segments[^1];
            if (parent.Type == SegmentType.Folder && last.Type != SegmentType.FolderCollection)
                segments[^1] = (last.Name, SegmentType.Leaf);
        }

        return segments;
    }
}