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

namespace Sundouleia.ModularActorData;

// TODO, Organize
/// <summary>
///     Handles how SMAD, SMAB, SMAO, SMAI & SMAIP files are handled by Sundouleia. <para />
///     Ensures any files in use are monitored and know when to be removed afterwards.
/// </summary> 
public class ModularActorFileHandler
{
    private readonly ILogger<ModularActorFileHandler> _logger;
    private readonly MainConfig _mainConfig;
    private readonly ModularActorsConfig _smaConfig;
    private readonly ModularActorHandler _handler;
    private readonly FileCacheManager _fileCache;
    private readonly SMAFileCacheManager _smaFileCache;
    private readonly ModdedStateManager _modStateManager;
    private readonly CharaObjectWatcher _watcher;

    public ModularActorFileHandler(ILogger<ModularActorFileHandler> logger, 
        MainConfig mainConfig, ModularActorsConfig smaConfig,
        ModularActorHandler handler, FileCacheManager cacheManager, 
        SMAFileCacheManager smaFileCache, ModdedStateManager modStateManager, 
        CharaObjectWatcher watcher)
    {
        _logger = logger;
        _mainConfig = mainConfig;
        _smaConfig = smaConfig;
        _handler = handler;
        _fileCache = cacheManager;
        _smaFileCache = smaFileCache;
        _modStateManager = modStateManager;
        _watcher = watcher;
    }

    /// <summary>
    ///     Using the defined actor, a description, filepath, and password, 
    ///     construct the base file of an actor and save it to disk.
    /// </summary>
    /// <param name="actor"> Which of the ownedActors to save a base file of. </param>
    /// <param name="description"> A readable description on the file's purpose. </param>
    /// <param name="filepath"> Where the file will be saved to. </param>
    /// <param name="password"> What password is required to access the file. (For ActorBase only) </param>
    /// <returns> The PrivateKey used for decryption that should be stored locally for owner-only access. </returns>
    public async Task<byte[]> SaveActorBaseFile(OwnedObject actor, string description, string filepath, string password)
    {
        // Collect the current state of the actor data.
        var curState = await _modStateManager.CollectActorModdedState(actor, CancellationToken.None).ConfigureAwait(false);
        // obtain the base file data.
        var actorBaseFileData = new ActorBaseFileData(_fileCache, actor, curState, description);

        // Run Lz4 compression algorithm over the base data for packaging what will be encrypted.
        byte[] compressedData = await CompressActorBaseData(actorBaseFileData).ConfigureAwait(false);
        _logger.LogInformation($"SMAB Compressed Payload Size: {compressedData.Length} bytes.");

        // Now that we have obtained the compressed payload we can package up the SMAB Header
        using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ownerPubSpki = owner.ExportSubjectPublicKeyInfo(); // Public access key, SPKI format.
        var ownerPrivPkcs8 = owner.ExportPkcs8PrivateKey(); // Private key, PKCS8 format.

        _logger.LogDebug($"Owner Public Key (SPKI): [{ownerPubSpki.ToString()}]");

        // Generate the file's ID, salt, and key hash via modern cryptography standards.
        var fileId = Guid.NewGuid();
        var salt = SmadCryptography.Random(16);
        var keyHash = SmadCryptography.DeriveKeyHash(password, salt);

        // Unless we decide later in the future,
        // initialize the bases allowed hashes to an empty list.
        var allowedHashes = new string[0];

        // Sign the header and obtain the signature.
        var headerToSign = Combine(fileId.ToByteArray(), salt, keyHash, ownerPubSpki);
        var ownerSignature = new OwnerSignature(SmadCryptography.Sign(owner, headerToSign));

        _logger.LogDebug($"Owner Signature: [{ownerSignature.Signature.ToString()}]");

        // Construct the SMAB Header based off this information.
        var header = new SmabHeader(SmabHeader.CurrentVersion, fileId, salt, keyHash, ownerPubSpki, allowedHashes, ownerSignature);

        _logger.LogInformation($"SMAB Header Constructed: Version {header.Version}, ID {header.Id}");

        // Use the current allowed hashes as part of the AAD for the encryption to ensure tampering is prevented.
        var fileSecret = SmadCryptography.DeriveFileSecret(password);
        var aad = Encoding.UTF8.GetBytes(string.Join(",", header.Hashes));
        // Aquire the final cipher, nonce, and tag for the encrypted data, using the compressed payload, file secret, and AAD.
        var (cipher, nonce, tag) = SmadCryptography.AesGcmEncrypt(compressedData, fileSecret, aad);
        // Bomb the file secret with airstrikes.
        Array.Clear(fileSecret);
        
        _logger.LogInformation("SMAB Payload Encrypted, writing to disk.");
        // Finalize and write out the SMAB file to disk.
        WriteActorBaseFile(filepath, header, cipher, nonce, tag);
        _logger.LogInformation($"SMAB File Saved to Disk: {filepath}");

        // return the private key, and should probably return the public key too later.
        // (maybe return as raw byte array idk)
        return ownerPrivPkcs8;
    }

    // Attempt to load an actor base file from a given filepath and password.
    // Returns if things worked out or not.
    public bool LoadActorBaseFile(string filePath, string password)
    {
        // Obtain the file header and encrypted contents.
        _logger.LogInformation($"Loading SMAB File from Disk: {filePath}");
        // Attempt to load and decrypt the file.
        if (!TryLoadBaseFile(filePath, password, out var fileHeader, out byte[] decryptedByteArray))
        {
            _logger.LogError($"Failed to load .smab using password ({password}) for FilePath: {filePath}");
            return false;
        }

        _logger.LogInformation("SMAB File Decrypted Successfully, processing payload.");
        // Decompress the decrypted byte array using LZ4.
        var actorBaseData = ExtractActorBaseData(fileHeader!, decryptedByteArray);
        _logger.LogInformation("SMAB Payload Processed Successfully, storing into handler.");
        _handler.AddProcessedActorData(new ModularActorData(actorBaseData));
        return true;
    }


    /// <summary>
    ///     Attempts to load in the specified SMAB file using the provided password. <para />
    ///     This will fail to load if the file was tampered, password is incorrect, or file is invalid.
    /// </summary>
    /// <param name="filePath"> The .smab file to load from. </param>
    /// <param name="password"> The password to open the file with. </param>
    /// <param name="header"> The loaded SMAB header data, if successful. </param>
    /// <param name="decryptedBytes"> The decrypted payload bytes, if successful. </param>
    /// <returns> If the file was successfully loaded and decrypted. </returns>
    /// <exception cref="UnauthorizedAccessException"> Bad Password. </exception>
    /// <exception cref="Exception"> Tampered or invalid file. </exception>
    private bool TryLoadBaseFile(string filePath, string password, [NotNullWhen(true)] out SmabHeader? header, out byte[] decryptedBytes)
    {
        header = null;
        decryptedBytes = Array.Empty<byte>();
        try
        {
            var (readHeader, cipher, nonce, tag) = ReadActorBaseFile(filePath);
            _logger.LogInformation($"SMAB Header Loaded: Version {readHeader.Version}, ID {readHeader.Id}");

            // Derive the keyhash using the provided password and salt.
            var derivedCandidate = SmadCryptography.DeriveKeyHash(password, readHeader.Salt);
            if (!derivedCandidate.SequenceEqual(readHeader.Key))
                throw new UnauthorizedAccessException("Incorrect Password for SMAB File Decryption.");

            _logger.LogInformation("Password Verified, proceeding with decryption.");
            // Make sure whatever calls this is internal or private to avoid reflection access.
            var fileSecret = SmadCryptography.DeriveFileSecret(password);
            var aad = Encoding.UTF8.GetBytes(string.Join(",", readHeader.Hashes));
            var decryptedPayload = SmadCryptography.AesGcmDecrypt(cipher, nonce, tag, fileSecret, aad);
            // Bomb the final secret with airstrikes.
            Array.Clear(fileSecret);
            // If nothing is in the payload, it failed.
            if (decryptedPayload is null)
                throw new Exception("[Load] Decryption failed / auth tag mismatch (AAD enforcement).");

            header = readHeader;
            decryptedBytes = decryptedPayload;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load SMAB File: {ex}");
            return false;
        }
    }

    // Grabs the compressed byte data housing the ModularActorBaseFileData bytes, followed by the raw file bytes.
    private async Task<byte[]> CompressActorBaseData(ActorBaseFileData baseData)
    {
        // Process it through the memory stream
        using var ms = new MemoryStream();
        using var lz4 = new LZ4Stream(ms, LZ4StreamMode.Compress, LZ4StreamFlags.HighCompression);
        using var writer = new BinaryWriter(lz4);
        // write the baseFileData as a byte array.
        var baseDataByteArr = baseData.ToByteArray();
        writer.Write(baseDataByteArr.Length);
        writer.Write(baseDataByteArr);
        // Write each file's raw bytes.
        foreach (var fileItem in baseData.Files)
        {
            var file = _fileCache.GetFileCacheByHash(fileItem.FileHash)!;
            _logger.LogDebug($"Saving to SMAB: {fileItem.FileHash}:{file.ResolvedFilepath}");
            _logger.LogDebug($"\tAssociated GamePaths: {string.Join("\t", fileItem.GamePaths)}");
            // Open the located file.
            var fsRead = File.OpenRead(file.ResolvedFilepath);
            await using (fsRead.ConfigureAwait(false))
            {
                // Run a binary reader over the files contents, appending it to the stream in high compression.
                using var br = new BinaryReader(fsRead);
                byte[] buffer = new byte[fileItem.Length];
                br.Read(buffer, 0, fileItem.Length);
                writer.Write(buffer);
            }
        }

        // Flush the writer.
        writer.Flush();
        await lz4.FlushAsync().ConfigureAwait(false);
        // return the compressed payload.
        return ms.ToArray();
    }

    private ActorBaseData ExtractActorBaseData(SmabHeader header, byte[] compressedBytes)
    {
        using var ms = new MemoryStream(compressedBytes);
        using var lz4 = new LZ4Stream(ms, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
        using var reader = new BinaryReader(lz4);

        // Whatever the first Int32 we read in is, tells us the length of the ActorBaseFileData bytes.
        var fileDataByteLength = reader.ReadInt32();
        ActorBaseFileData baseData = ActorBaseFileData.FromByteArray(reader.ReadBytes(fileDataByteLength));

        var modFileDataLength = reader.ReadInt32();

        // Now we need to extract out the file data into the cached folder, or hold a ref to our sundouleia cache if present there.
        long totalRead = 0;
        var moddedPathDict = new Dictionary<string, string>(StringComparer.Ordinal);
        // Iterate first over each modded file.
        foreach (var fileData in baseData.Files)
        {
            // Get the file extension.
            var fileExt = fileData.GamePaths.First().Split(".")[^1];
            var hash = fileData.FileHash;
            var fileLength = fileData.Length; // Know how much to read.

            // If the file alreadyGetFileCacheByHash exists in the sundouleia cache, remap the link to that instead.
            if (_fileCache.GetFileCacheByHash(hash) is { } fileEntity)
            {
                // Set all the fileData's gamepaths to this resolved filepath instead.
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileEntity.ResolvedFilepath;
                    _logger.LogTrace($"{gamepath} => {fileEntity.ResolvedFilepath} [{hash}] (sundouleia cache)");
                }
                // We still must consume the bytes from the compressed stream so the reader is positioned correctly.
                var buffer = reader.ReadBytes(fileLength);
                if (buffer.Length is 0)
                    throw new EndOfStreamException("Unexpected EOF while skipping cached file data");
                totalRead += buffer.Length;
                _logger.LogTrace($"Skipped {SundouleiaEx.ByteToString(buffer.Length)} bytes for cached file {hash}");
                continue;
            }
            else
            {
                // Create a file in the SMACache.
                var fileName = _smaFileCache.GetCacheFilePath(hash, fileExt);
                // Open for writestream to write out the file data.
                using var fs = File.OpenWrite(fileName);
                using var wr = new BinaryWriter(fs);
                _logger.LogTrace($"Writing {SundouleiaEx.ByteToString(fileLength)} bytes into {fileName}");
                var buffer = reader.ReadBytes(fileLength);
                // write the buffer to the file then flush and close streams.
                wr.Write(buffer);
                wr.Flush();
                wr.Close();
                // If there was no buffer, throw an exception.
                if (buffer.Length is 0)
                    throw new EndOfStreamException("Unexpected EOF");
                // Ensure that all these gamepaths for this file have the swap to this filepath.
                foreach (var gamepath in fileData.GamePaths)
                {
                    moddedPathDict[gamepath] = fileName;
                    _logger.LogTrace($"{gamepath} => {fileName} [{fileData.FileHash}] (SMA cache)");
                }
                // Inc the total read and report progress.
                totalRead += buffer.Length;
                _logger.LogTrace($"Read {SundouleiaEx.ByteToString(totalRead)}/{SundouleiaEx.ByteToString(modFileDataLength)} bytes");
            }
        }

        // Allow FileSwaps to take priority here.
        foreach (var entry in baseData.FileSwaps.SelectMany(k => k.GamePaths, (k, p) => new KeyValuePair<string, string>(p, k.FileSwapPath)))
        {
            moddedPathDict[entry.Key] = entry.Value;
            _logger.LogTrace($"[Swap] {entry.Key} => {entry.Value}");
        }

        // Create a new ActorBaseData from these sources.
        return new ActorBaseData(header, baseData, moddedPathDict);
    }

    // Write the final compiled contents out to the desired path.
    private void WriteActorBaseFile(string path, SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag)
    {
        // construct a temporary placeholder path to write to.
        var tmp = path + ".tmp";
        // Serialize the header for byte writing.
        var headerJsonBytes = SmabHeader.Serialize(header);
        // Create the filestream and writer for the data to be wrote out to.
        using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);
        // Write the magic
        bw.Write(Encoding.ASCII.GetBytes("SMAB"));
        bw.Write(header.Version);
        bw.Write(headerJsonBytes.Length);
        bw.Write(headerJsonBytes);
        bw.Write(nonce);
        bw.Write(tag);
        bw.Write(cipher.Length);
        bw.Write(cipher);
        // Flush both streams, then close the filestream.
        bw.Flush();
        fs.Flush();
        fs.Close();
        // Move the temporary file data over to the desired file data, overwriting it.
        File.Move(tmp, path, true);
    }

    // Retrieve the SMAB file header and encrypted contents from disk.
    private (SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag) ReadActorBaseFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        // Read the magic, throw exception if not SMAB.
        var magic = new string(br.ReadChars(4));
        if (magic != "SMAB") throw new InvalidDataException("Bad File Magic.");
        // Read the header contents.
        var version = br.ReadByte();
        var headerLen = br.ReadInt32();
        var headerBytes = br.ReadBytes(headerLen);
        var header = SmabHeader.Deserialize(headerBytes);
        // Aquire the nonce, tag, and encrypted payload.
        var nonce = br.ReadBytes(12);
        var tag = br.ReadBytes(16);
        var cipherLen = br.ReadInt32();
        var cipher = br.ReadBytes(cipherLen);
        return (header, cipher, nonce, tag);
    }

    /// <summary>
    ///     Helper method to combine multiple byte arrays into one.
    /// </summary>
    /// <returns> The combined byte array. </returns>
    private byte[] Combine(params byte[][] parts)
    {
        var tot = parts.Sum(p => p?.Length ?? 0);
        var outb = new byte[tot];
        int pos = 0;
        foreach (var p in parts)
        {
            if (p == null) continue;
            Buffer.BlockCopy(p, 0, outb, pos, p.Length);
            pos += p.Length;
        }
        return outb;
    }
}