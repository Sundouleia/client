using Lumina.Data.Files;

namespace Sundouleia.ModFiles;

internal sealed record FileDataEntry(string Hash, string FileType, List<string> GamePaths, List<string> FilePaths, long OriginalSize, long CompressedSize, long Triangles)
{
    public bool IsComputed => OriginalSize > 0 && CompressedSize > 0;
    public async Task ComputeSizes(FileCacheManager fileCacheManager, CancellationToken token)
    {
        var compressedSize = await fileCacheManager.GetCompressedFileData(Hash, token).ConfigureAwait(false);
        var normalSize = new FileInfo(FilePaths[0]).Length;
        var entries = fileCacheManager.GetAllFileCachesByHash(Hash, ignoreCacheEntries: true, validate: false);
        foreach (var entry in entries)
        {
            entry.Size = normalSize;
            entry.CompressedSize = compressedSize.Item2.LongLength;
        }
        OriginalSize = normalSize;
        CompressedSize = compressedSize.Item2.LongLength;
    }
    public long OriginalSize { get; private set; } = OriginalSize;
    public long CompressedSize { get; private set; } = CompressedSize;
    public long Triangles { get; private set; } = Triangles;

    public Lazy<string> Format = new(() =>
    {
        switch (FileType)
        {
            case "tex":
                {
                    try
                    {
                        using var stream = new FileStream(FilePaths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var reader = new BinaryReader(stream);
                        reader.BaseStream.Position = 4;
                        var format = (TexFile.TextureFormat)reader.ReadInt32();
                        return format.ToString();
                    }
                    catch
                    {
                        return "Unknown";
                    }
                }
            default:
                return string.Empty;
        }
    });
}