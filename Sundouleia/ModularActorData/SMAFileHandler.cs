using K4os.Compression.LZ4.Legacy;
using Lumina.Data.Parsing.Scd;
using Penumbra.String.Classes;
using Sundouleia.Interop;
using Sundouleia.ModFiles;
using Sundouleia.PlayerClient;
using Sundouleia.Services;
using Sundouleia.Watchers;
using SundouleiaAPI.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using TerraFX.Interop.Windows;

namespace Sundouleia.ModularActor;


/// <summary>
///     Assists in how SMA Files are saved to and loaded from disk. <para />
///     Both services using these handles can necessary logic here. 
///     (Resolve circular dependencies as things go)
/// </summary> 
public class SMAFileHandler : IDisposable
{
    // A lot of the following below is a mess that is in need of organizing. Workflow process so far is great.
    private readonly ILogger<SMAFileHandler> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly GPoseActorHandler _handler;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly SMAManager _smaManager;
    private readonly ModdedStateManager _modStateManager;
    private readonly CharaObjectWatcher _watcher;

    private Progress<(string fileName, int percent)> _saveProgress = new();
    public SMAFileHandler(ILogger<SMAFileHandler> logger, 
        MainConfig mainConfig, ModularActorsConfig smaConfig,
        GPoseActorHandler handler, FileCacheManager cacheManager, 
        SMAFileCacheManager smaFileCache, SMAManager smaManager,
        ModdedStateManager modStateManager, CharaObjectWatcher watcher)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _handler = handler;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _smaManager = smaManager;
        _modStateManager = modStateManager;
        _watcher = watcher;

        _saveProgress.ProgressChanged += UpdateProgress;
    }

    public string CurrentFile   { get; private set; } = string.Empty;
    public int    ScannedFiles  { get; private set; } = 0;

    private void UpdateProgress(object? sender, (string, int) e)
    {
        CurrentFile = e.Item1;
        ScannedFiles = e.Item2;
    }

    public void Dispose()
    {
        _saveProgress.ProgressChanged -= UpdateProgress;
    }

    /// <summary>
    ///     Saves a new SMAB file to disk in the directory provided. <para />
    ///     Name, Description, and optionally a password can be provided.
    /// </summary>
    public async Task SaveNewSMABFile(OwnedObject actor, string filePath, string name, string description, string password, CancellationToken ct)
    {
        // Collect Modded State (Preferably from Resource Tree Allocation.
        var curState = await _modStateManager.CollectModdedState(ct);
        
        // Generate unique id to assign for this file.
        var fileId = Guid.NewGuid();
        
        // generate a random decryption fileKey. Does not need to be anything complicated.
        var fileKey = SmadCryptography.Random(32); // 256 bit key. (used to be salt previously)

        // Construct the base fileData to be stored in the file's header region.
        var actorBaseFileData = new BaseFileDataSummary();

        // Run compression over the base file data and all referenced file data, report save progress.
        //byte[] compressedData = await CompressActorBaseData(actorBaseFileData, _saveProgress, ct);
        //_logger.LogInformation($"SMAB Compressed Payload Size: {compressedData.Length} bytes.");

        //// Run an encryption over the entire file using the fileKey, then save the file to disk.
        //// TODO: Correct encryption format later.
        //// 
        //_logger.LogInformation("SMAB Payload Encrypted, writing to disk.");
        //// Finalize and write out the SMAB file to disk.
        //WriteActorBaseFile(filepath, header, cipher, nonce, tag);
        //_logger.LogInformation($"SMAB File Saved to Disk: {filepath}");
    }

    // Attempt to load an actor base file from a given filepath and password.
    // Returns if things worked out or not.
    public bool LoadActorBaseFile(string filePath, string password)
    {
        //// Obtain the file header and encrypted contents.
        //_logger.LogInformation($"Loading SMAB File from Disk: {filePath}");
        //// Attempt to load and decrypt the file.
        //if (!TryLoadBaseFile(filePath, password, out var fileHeader, out byte[] decryptedByteArray))
        //{
        //    _logger.LogError($"Failed to load .smab using password ({password}) for FilePath: {filePath}");
        //    return false;
        //}

        //_logger.LogInformation("SMAB File Decrypted Successfully, processing payload.");
        //// Decompress the decrypted byte array using LZ4.
        //var actorBaseData = ExtractActorBaseData(fileHeader!, decryptedByteArray);
        //_logger.LogInformation("SMAB Payload Processed Successfully, storing into handler.");
        //_smaManager.AddProcessedActorData(new ModularActorData(actorBaseData));
        return true;
    }


    ///// <summary>
    /////     Attempts to load in the specified SMAB file using the provided password. <para />
    /////     This will fail to load if the file was tampered, password is incorrect, or file is invalid.
    ///// </summary>
    ///// <param name="filePath"> The .smab file to load from. </param>
    ///// <param name="password"> The password to open the file with. </param>
    ///// <param name="header"> The loaded SMAB header data, if successful. </param>
    ///// <param name="decryptedBytes"> The decrypted payload bytes, if successful. </param>
    ///// <returns> If the file was successfully loaded and decrypted. </returns>
    ///// <exception cref="UnauthorizedAccessException"> Bad Password. </exception>
    ///// <exception cref="Exception"> Tampered or invalid file. </exception>
    //private bool TryLoadBaseFile(string filePath, string password, [NotNullWhen(true)] out SmabHeader? header, out byte[] decryptedBytes)
    //{
    //    header = null;
    //    decryptedBytes = Array.Empty<byte>();
    //    try
    //    {
    //        var (readHeader, cipher, nonce, tag) = ReadActorBaseFile(filePath);
    //        _logger.LogInformation($"SMAB Header Loaded: Version {readHeader.Version}, ID {readHeader.Id}");

    //        // Derive the keyhash using the provided password and salt.
    //        var derivedCandidate = SmadCryptography.DeriveKeyHash(password, readHeader.Salt);
    //        if (!derivedCandidate.SequenceEqual(readHeader.Key))
    //            throw new UnauthorizedAccessException("Incorrect Password for SMAB File Decryption.");

    //        _logger.LogInformation("Password Verified, proceeding with decryption.");
    //        // Make sure whatever calls this is internal or private to avoid reflection access.
    //        var fileSecret = SmadCryptography.DeriveFileSecret(password);
    //        var aad = Encoding.UTF8.GetBytes(string.Join(",", readHeader.Hashes));
    //        var decryptedPayload = SmadCryptography.AesGcmDecrypt(cipher, nonce, tag, fileSecret, aad);
    //        // Bomb the final secret with airstrikes.
    //        Array.Clear(fileSecret);
    //        // If nothing is in the payload, it failed.
    //        if (decryptedPayload is null)
    //            throw new Exception("[Load] Decryption failed / auth tag mismatch (AAD enforcement).");

    //        header = readHeader;
    //        decryptedBytes = decryptedPayload;
    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError($"Failed to load SMAB File: {ex}");
    //        return false;
    //    }
    //}

    //// Grabs the compressed byte data housing the ModularActorBaseFileData bytes, followed by the raw file bytes.
    //private async Task<byte[]> CompressActorBaseData(BaseFileDataSummary baseData, IProgress<(string, int)> progress, CancellationToken ct)
    //{
    //    // Force itself off the framework thread.
    //    while (Svc.Framework.IsInFrameworkUpdateThread && !ct.IsCancellationRequested)
    //        await Task.Delay(1).ConfigureAwait(false);

    //    // Process it through the memory stream
    //    using var ms = new MemoryStream();
    //    using var lz4 = new LZ4Stream(ms, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
    //    using var writer = new BinaryWriter(lz4);
    //    // write the baseFileData as a byte array.
    //    var baseDataByteArr = baseData.ToByteArray();
    //    writer.Write(baseDataByteArr.Length);
    //    writer.Write(baseDataByteArr);
    //    // Write each file's raw bytes.
    //    int currentFile = 0;
    //    foreach (var fileItem in baseData.Files)
    //    {
    //        progress.Report((fileItem.GamePaths.First(), currentFile));
    //        var file = _fileCache.GetFileCacheByHash(fileItem.FileHash)!;
    //        _logger.LogDebug($"Saving to SMAB: {fileItem.FileHash}:{file.ResolvedFilepath}");
    //        // Open the located file.
    //        var fsRead = File.OpenRead(file.ResolvedFilepath);
    //        await using (fsRead.ConfigureAwait(false))
    //        {
    //            // Run a binary reader over the files contents, appending it to the stream in high compression.
    //            using var br = new BinaryReader(fsRead);
    //            byte[] buffer = new byte[fileItem.Length];
    //            br.Read(buffer, 0, fileItem.Length);
    //            writer.Write(buffer);
    //        }
    //        currentFile++;
    //    }

    //    // Flush the writer.
    //    writer.Flush();
    //    await lz4.FlushAsync().ConfigureAwait(false);
    //    // return the compressed payload.
    //    return ms.ToArray();
    //}


    //private ActorBaseData ExtractActorBaseData(SmabHeader header, byte[] compressedBytes)
    //{
    //    using var ms = new MemoryStream(compressedBytes);
    //    using var lz4 = new LZ4Stream(ms, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
    //    using var reader = new BinaryReader(lz4);

    //    // Whatever the first Int32 we read in is, tells us the length of the ActorBaseFileData bytes.
    //    var fileDataByteLength = reader.ReadInt32();
    //    ActorBaseFileData baseData = ActorBaseFileData.FromByteArray(reader.ReadBytes(fileDataByteLength));

    //    var modFileDataLength = reader.ReadInt32();

    //    // Now we need to extract out the file data into the cached folder, or hold a ref to our sundouleia cache if present there.
    //    long totalRead = 0;
    //    var moddedPathDict = new Dictionary<string, string>(StringComparer.Ordinal);
    //    // Iterate first over each modded file.
    //    foreach (var fileData in baseData.Files)
    //    {
    //        // Get the file extension.
    //        var fileExt = fileData.GamePaths.First().Split(".")[^1];
    //        var hash = fileData.FileHash;
    //        var fileLength = fileData.Length; // Know how much to read.

    //        // If the file alreadyGetFileCacheByHash exists in the sundouleia cache, remap the link to that instead.
    //        if (_fileCache.GetFileCacheByHash(hash) is { } fileEntity)
    //        {
    //            // Set all the fileData's gamepaths to this resolved filepath instead.
    //            foreach (var gamepath in fileData.GamePaths)
    //            {
    //                moddedPathDict[gamepath] = fileEntity.ResolvedFilepath;
    //                _logger.LogTrace($"{gamepath} => {fileEntity.ResolvedFilepath} [{hash}] (sundouleia cache)");
    //            }
    //            // We still must consume the bytes from the compressed stream so the reader is positioned correctly.
    //            var buffer = reader.ReadBytes(fileLength);
    //            if (buffer.Length is 0)
    //                throw new EndOfStreamException("Unexpected EOF while skipping cached file data");
    //            totalRead += buffer.Length;
    //            _logger.LogTrace($"Skipped {SundouleiaEx.ByteToString(buffer.Length)} bytes for cached file {hash}");
    //            continue;
    //        }
    //        else
    //        {
    //            // Create a file in the SMACache.
    //            var fileName = _smaFileCache.GetCacheFilePath(hash, fileExt);
    //            // Open for writestream to write out the file data.
    //            using var fs = File.OpenWrite(fileName);
    //            using var wr = new BinaryWriter(fs);
    //            _logger.LogTrace($"Writing {SundouleiaEx.ByteToString(fileLength)} bytes into {fileName}");
    //            var buffer = reader.ReadBytes(fileLength);
    //            // write the buffer to the file then flush and close streams.
    //            wr.Write(buffer);
    //            wr.Flush();
    //            wr.Close();
    //            // If there was no buffer, throw an exception.
    //            if (buffer.Length is 0)
    //                throw new EndOfStreamException("Unexpected EOF");
    //            // Ensure that all these gamepaths for this file have the swap to this filepath.
    //            foreach (var gamepath in fileData.GamePaths)
    //            {
    //                moddedPathDict[gamepath] = fileName;
    //                _logger.LogTrace($"{gamepath} => {fileName} [{fileData.FileHash}] (SMA cache)");
    //            }
    //            // Inc the total read and report progress.
    //            totalRead += buffer.Length;
    //            _logger.LogTrace($"Read {SundouleiaEx.ByteToString(totalRead)}/{SundouleiaEx.ByteToString(modFileDataLength)} bytes");
    //        }
    //    }

    //    // Allow FileSwaps to take priority here.
    //    foreach (var entry in baseData.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
    //    {
    //        moddedPathDict[entry.Key] = entry.Value;
    //        _logger.LogTrace($"[Swap] {entry.Key} => {entry.Value}");
    //    }

    //    // Create a new ActorBaseData from these sources.
    //    return new ActorBaseData(header, baseData, moddedPathDict);
    //}

    //// Write the final compiled contents out to the desired path.
    //private void WriteActorBaseFile(string path, SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag)
    //{
    //    // construct a temporary placeholder path to write to.
    //    var tmp = path + ".tmp";
    //    // Serialize the header for byte writing.
    //    var headerJsonBytes = SmabHeader.Serialize(header);
    //    // Create the filestream and writer for the data to be wrote out to.
    //    using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
    //    using var bw = new BinaryWriter(fs);
    //    // Write the magic
    //    bw.Write(Encoding.ASCII.GetBytes("SMAB"));
    //    bw.Write(header.Version);
    //    bw.Write(headerJsonBytes.Length);
    //    bw.Write(headerJsonBytes);
    //    bw.Write(nonce);
    //    bw.Write(tag);
    //    _logger.LogInformation("DEBUG LINE 10");
    //    bw.Write(cipher.Length);
    //    bw.Write(cipher);
    //    _logger.LogInformation("DEBUG LINE 11");
    //    // Flush both streams, then close the filestream.
    //    bw.Flush();
    //    fs.Flush();
    //    fs.Close();
    //    // Move the temporary file data over to the desired file data, overwriting it.
    //    File.Move(tmp, path, true);
    //}

    //// Retrieve the SMAB file header and encrypted contents from disk.
    //private (SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag) ReadActorBaseFile(string path)
    //{
    //    using var fs = File.OpenRead(path);
    //    using var br = new BinaryReader(fs);
    //    // Read the magic, throw exception if not SMAB.
    //    var magic = new string(br.ReadChars(4));
    //    if (magic != "SMAB") throw new InvalidDataException("Bad File Magic.");
    //    // Read the header contents.
    //    var version = br.ReadByte();
    //    var headerLen = br.ReadInt32();
    //    var headerBytes = br.ReadBytes(headerLen);
    //    var header = SmabHeader.Deserialize(headerBytes);
    //    // Aquire the nonce, tag, and encrypted payload.
    //    var nonce = br.ReadBytes(12);
    //    var tag = br.ReadBytes(16);
    //    var cipherLen = br.ReadInt32();
    //    var cipher = br.ReadBytes(cipherLen);
    //    return (header, cipher, nonce, tag);
    //}

    ///// <summary>
    /////     Helper method to combine multiple byte arrays into one.
    ///// </summary>
    ///// <returns> The combined byte array. </returns>
    //private byte[] Combine(params byte[][] parts)
    //{
    //    var tot = parts.Sum(p => p?.Length ?? 0);
    //    var outb = new byte[tot];
    //    int pos = 0;
    //    foreach (var p in parts)
    //    {
    //        if (p == null) continue;
    //        Buffer.BlockCopy(p, 0, outb, pos, p.Length);
    //        pos += p.Length;
    //    }
    //    return outb;
    //}
}