using System.Runtime.InteropServices;
using Sundouleia.PlayerClient;

namespace Sundouleia.ModFiles;

// Can find references to internal handles on this from within OtterGui.Compression algorithms,
// which supports multiple formats of compression in addition to this one, if it ever gets updated.
public sealed class FileCompactor
{
    // Internal constants used for DeviceIoControl calls.
    public const uint FSCTL_DELETE_EXTERNAL_BACKING = 0x90314U;
    public const ulong WOF_PROVIDER_FILE = 2UL;
    private enum CompressionAlgorithm
    {
        NO_COMPRESSION = -2,
        LZNT1 = -1,
        XPRESS4K = 0,
        LZX = 1,
        XPRESS8K = 2,
        XPRESS16K = 3
    }

    // Struct instance used for WofSetFileDataLocation calls and cluster size caching.
    private readonly WOF_FILE_COMPRESSION_INFO_V1 _efInfo;
    private readonly Dictionary<string, int> _clusterSizes;

    private readonly ILogger<FileCompactor> _logger;
    private readonly MainConfig _config;
    public FileCompactor(ILogger<FileCompactor> logger, MainConfig config)
    {
        _logger = logger;
        _config = config;

        _clusterSizes = new(StringComparer.Ordinal);
        _efInfo = new WOF_FILE_COMPRESSION_INFO_V1
        {
            Algorithm = CompressionAlgorithm.XPRESS8K,
            Flags = 0
        };
    }

    // Tracks if the compactor is processing or not, and the progress string if it is.
    public bool MassCompactRunning { get; private set; } = false;
    public string Progress { get; private set; } = string.Empty;

    /// <summary>
    ///     Runs the file compressor over all files in the cache folder. <para />
    ///     This is a very resource intensive operation and is recommended a 
    ///     warning be added to its execution.
    /// </summary>
    public void CompactStorage()
    {
        MassCompactRunning = true;
        // Track progress variables.
        int currentFile = 1;
        var allFiles = Directory.EnumerateFiles(_config.Current.CacheFolder).ToList();
        int allFilesCount = allFiles.Count;
        // Iterate over all files and update the progress string accordingly.
        foreach (var file in allFiles)
        {
            Progress = $"{currentFile}/{allFilesCount}";
            CompactFile(file);
            currentFile++;
        }
        MassCompactRunning = false;
    }

    /// <summary>
    ///     Runs the file decompressor over all files in the cache folder. <para />
    ///     This is a very resource intensive operation and is recommended a 
    ///     warning be added to its execution.
    /// </summary>
    public void DecompactStorage()
    {
        MassCompactRunning = true;
        // Track progress variables.
        int currentFile = 1;
        var allFiles = Directory.EnumerateFiles(_config.Current.CacheFolder).ToList();
        int allFilesCount = allFiles.Count;
        // Iterate over all files and update the progress string accordingly.
        foreach (var file in allFiles)
        {
            Progress = $"{currentFile}/{allFilesCount}";
            DecompressFile(file);
            currentFile++;
        }
        MassCompactRunning = false;
    }


    /// <summary>
    ///     Helper method that obtains the file's current size on disk.
    /// </summary>
    public long GetFileSizeOnDisk(FileInfo fileInfo, bool? isNTFS = null)
    {
        if (Util.IsWine() || !string.Equals(new DriveInfo(fileInfo.Directory!.Root.FullName).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            return fileInfo.Length;

        // Otherwise the true size can vary and we need to calculate it.
        var clusterSize = GetClusterSize(fileInfo);
        // If the cluster size is invalid return the assumed length.
        if (clusterSize is -1)
            return fileInfo.Length;
        // Otherwise we can get the compressed file size.
        var lowOrderSize = GetCompressedFileSizeW(fileInfo.FullName, out uint highOrderSize);
        // Total compressed file size in bytes.
        var size = (long)highOrderSize << 32 | lowOrderSize;
        // Round up to nearest cluster size.
        return ((size + clusterSize - 1) / clusterSize) * clusterSize;
    }

    /// <summary>
    ///     Writes out all file byte data into the cache for the given FilePath, and compact if nessessary.
    /// </summary>
    public async Task WriteAllBytesAsync(string filePath, byte[] decompressedFile, CancellationToken token)
    {
        await File.WriteAllBytesAsync(filePath, decompressedFile, token).ConfigureAwait(false);
        // If wine or not configured to have a compact cache, then skip compacting.
        if (Util.IsWine() || !_config.Current.CompactCache)
            return;

        CompactFile(filePath);
    }

    /// <summary>
    ///    Compacts the file at the given path, if configured to do so and supported by the system.
    /// </summary>
    public void CompactFileSafe(string filePath)
    {
        // If wine or not configured to have a compact cache, then skip compacting.
        if (Util.IsWine() || !_config.Current.CompactCache)
            return;

        CompactFile(filePath);
    }

    /// <summary>
    ///     Compacts the file at the given path if not already compacted. <para />
    ///     Results depend on if the drive format uses 'New Technology File System' (NTFS).
    /// </summary>
    private void CompactFile(string filePath)
    {
        var fs = new DriveInfo(new FileInfo(filePath).Directory!.Root.FullName);
        if (!string.Equals(fs.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning($"Drive for file {filePath} is not NTFS");
            return;
        }

        // Grab the current file information from the defined path, current size, and respective cluster size.
        var fi = new FileInfo(filePath);
        var oldSize = fi.Length;
        var clusterSize = GetClusterSize(fi);
        // Ignore files smaller than the 8KB cluster size (Compacting these is irrelevant)
        if (oldSize < Math.Max(clusterSize, 8 * 1024))
        {
            _logger.LogDebug($"File {filePath} is smaller than cluster size ({clusterSize}), ignoring");
            return;
        }

        // Determine first if the file already is compacted prior to doing so. Skip the process if it was.
        if (!IsCompactedFile(filePath))
        {
            // XPRESS8K is one of many compression algorithms supported by windows for NTFS drives.
            // While used internally by windows for file creations, we can also make use of it to leverage
            // reduced file sizes on disk for our modded file cache, helping prevent bloat / overload overtime.
            _logger.LogDebug($"Compacting file to XPRESS8K: {filePath}");
            // Use WindowsOverlayFilter API to compress the format at file system level.
            WOFCompressFile(filePath);
            // Determine the new size on the disk after compression is applied, and log results.
            var newSize = GetFileSizeOnDisk(fi);
            _logger.LogDebug($"Compressed {filePath}: {{{oldSize}b}} => {{{newSize}b}}");
        }
        else
        {
            _logger.LogDebug($"File {filePath} already compressed");
        }
    }

    /// <summary>
    ///     Decompressed a compressed WoF file at a given path via kernel32.dll DeviceIoControl.
    /// </summary>
    private void DecompressFile(string path)
    {
        _logger.LogDebug($"Removing compression from {path}");
        try
        {
            // Use DeviceIoControl to remove the external backing (decompress) the file.
            using (var fs = new FileStream(path, FileMode.Open))
            {
                var hDevice = fs.SafeFileHandle.DangerousGetHandle();
                _ = DeviceIoControl(hDevice, FSCTL_DELETE_EXTERNAL_BACKING, nint.Zero, 0, nint.Zero, 0, out _, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error decompressing file {path}", path);
        }
    }

    /// <summary>
    ///     Obtain the cluster size of a given file.
    /// </summary>
    private int GetClusterSize(FileInfo fi)
    {
        if (!fi.Exists)
            return -1;

        var root = fi.Directory?.Root.FullName.ToLower() ?? string.Empty;
        if (string.IsNullOrEmpty(root))
            return -1;

        if (_clusterSizes.TryGetValue(root, out int value))
            return value;

        _logger.LogDebug($"Getting Cluster Size for {fi.FullName}, (Root: {root})");
        if (GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _) is 0)
            return -1;

        _clusterSizes[root] = (int)(sectorsPerCluster * bytesPerSector);
        _logger.LogDebug($"Determined Cluster Size for root {root}: {_clusterSizes[root]}");
        return _clusterSizes[root];
    }

    /// <summary>
    ///     Grab the files compression status via WofIsExternalFile, to check its compression algorithm.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    private static bool IsCompactedFile(string filePath)
    {
        uint buf = 8;
        _ = WofIsExternalFile(filePath, out int isExtFile, out uint _, out var info, ref buf);
        if (isExtFile is 0)
            return false;

        return info.Algorithm is CompressionAlgorithm.XPRESS8K;
    }

    /// <summary>
    ///     Within its own function scope, perform WOF Compression to the file at the given path. <para />
    ///     Allocation and freeing of unmanaged memory is handled within this function. <para />
    ///     Pray to god that this never changes because otherwise my knowledge 
    ///     on additional compression algorithms is limited.
    /// </summary>
    private void WOFCompressFile(string path)
    {
        var efInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_efInfo));
        Marshal.StructureToPtr(_efInfo, efInfoPtr, fDeleteOld: true);
        ulong length = (ulong)Marshal.SizeOf(_efInfo);
        try
        {
            // Open the file and grab its handle for WofSetFileDataLocation.
            using (var fs = new FileStream(path, FileMode.Open))
            {
                var hFile = fs.SafeFileHandle.DangerousGetHandle();
                if (fs.SafeFileHandle.IsInvalid)
                {
                    _logger.LogWarning($"Invalid file handle to {path}");
                }
                else
                {
                    // Set the file data location to the XPRESS8K compression algorithm.
                    var ret = WofSetFileDataLocation(hFile, WOF_PROVIDER_FILE, efInfoPtr, length);
                    if (!(ret == 0 || ret == unchecked((int)0x80070158)))
                        _logger.LogWarning("Failed to compact {file}: {ret}", path, ret.ToString("X"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error compacting file {path}", path);
        }
        finally
        {
            // Ensure that all unmanaged memory is freed.
            Marshal.FreeHGlobal(efInfoPtr);
        }
    }

    // DLL Access. All of the following is used for direct kernal32.dll access to have administrative file system control / access.
    // Useful in the calculation of file cluster sizes, location, and compression.
    // ---------------------------------------------------------------
    // Documentation references below:

    // https://learn.microsoft.com/en-us/windows/win32/api/wofapi/ns-wofapi-wof_file_compression_info_v1
    private struct WOF_FILE_COMPRESSION_INFO_V1
    {
        public CompressionAlgorithm Algorithm;
        public ulong Flags;
    }

    // https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-deviceiocontrol
    [DllImport("kernel32.dll")]
    private static extern int DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out IntPtr lpBytesReturned, out IntPtr lpOverlapped);

    // https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew
    [DllImport("kernel32.dll")]
    private static extern uint GetCompressedFileSizeW([In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName, [Out, MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

    // https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespacew
    [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
    private static extern int GetDiskFreeSpaceW([In, MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName,
           out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters,
           out uint lpTotalNumberOfClusters);

    // https://learn.microsoft.com/en-us/windows/win32/api/wofapi/nf-wofapi-wofisexternalfile
    [DllImport("WoFUtil.dll")]
    private static extern int WofIsExternalFile([MarshalAs(UnmanagedType.LPWStr)] string Filepath, out int IsExternalFile, out uint Provider, out WOF_FILE_COMPRESSION_INFO_V1 Info, ref uint BufferLength);

    // https://learn.microsoft.com/en-us/windows/win32/api/wofapi/nf-wofapi-wofsetfiledatalocation
    [DllImport("WofUtil.dll")]
    private static extern int WofSetFileDataLocation(IntPtr FileHandle, ulong Provider, IntPtr ExternalFileInfo, ulong Length);
}