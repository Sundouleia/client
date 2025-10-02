#nullable disable

namespace Sundouleia.ModFiles;

// This can be extremely oversimplified later, so just worry about it when we start processing file things.
public class FileCacheEntity
{
    public FileCacheEntity(string hash, string path, string lastModifiedDateTicks, long? size = null, long? compressedSize = null)
    {
        Size = size;
        CompressedSize = compressedSize;
        Hash = hash;
        PrefixedFilePath = path;
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    public long? CompressedSize { get; set; }
    public string CsvEntry => $"{Hash}{FileCacheManager.CSV_SPLIT}{PrefixedFilePath}{FileCacheManager.CSV_SPLIT}{LastModifiedDateTicks}|{Size ?? -1}|{CompressedSize ?? -1}";
    public string Hash { get; set; }
    public bool IsCacheEntry => PrefixedFilePath.StartsWith(FileCacheManager.CACHE_PREFIX, StringComparison.OrdinalIgnoreCase);
    public string LastModifiedDateTicks { get; set; }
    public string PrefixedFilePath { get; init; }
    public string ResolvedFilepath { get; private set; } = string.Empty;
    public long? Size { get; set; }

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}