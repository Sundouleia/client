using System.Security.Cryptography;

namespace Sundouleia.ModularActor;

public static class SmadCryptography
{
    public static byte[] Random(int len)
    {
        var b = new byte[len];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    // PBKDF2 fallback KDF (Migrate to BLAKE3 later.)
    public static byte[] DeriveKeyHash(string password, byte[] salt, int outLen = 32, int iterations = 150_000)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            outLen
        );
    }

    // Derive symmetric FileSecret from password (HMAC label)
    public static byte[] DeriveFileSecret(string password)
    {
        var key = Encoding.UTF8.GetBytes(password);
        using var h = new HMACSHA256(key);
        var secret = h.ComputeHash(Encoding.UTF8.GetBytes("smad-file-secret-v1"));
        Array.Clear(key, 0, key.Length);
        return secret;
    }

    // AES-GCM with optional AAD
    public static (byte[] cipher, byte[] nonce, byte[] tag) AesGcmEncrypt(byte[] plain, byte[] key, byte[]? aad = null)
    {
        var nonce = Random(12); // standard 12-byte nonce
        var tag = new byte[16]; // AES-GCM tag
        var cipher = new byte[plain.Length];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plain, cipher, tag, aad);

        return (cipher, nonce, tag);
    }

    // Decrypts and verifies AES-GCM with optional AAD; returns null on failure
    public static byte[]? AesGcmDecrypt(byte[] cipher, byte[] nonce, byte[] tag, byte[] key, byte[]? aad = null)
    {
        try
        {
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, tagSizeInBytes: 16);
            aes.Decrypt(nonce, cipher, tag, plain, aad);
            return plain;
        }
        catch (CryptographicException cex)
        {
            Svc.Logger.Warning($"AES-GCM decryption/authentication failed: {cex.Message}");
            return null;
        }
    }

    public static byte[] Sign(ECDsa priv, byte[] data) => priv.SignData(data, HashAlgorithmName.SHA256);
    public static bool Verify(ECDsa pub, byte[] data, byte[] sig) => pub.VerifyData(data, sig, HashAlgorithmName.SHA256);

    public static byte[] ExportPublic(ECDsa ec) => ec.ExportSubjectPublicKeyInfo();
    public static ECDsa ImportPublic(byte[] spki) 
    {
        var e = ECDsa.Create(); 
        e.ImportSubjectPublicKeyInfo(spki, out _); 
        return e; 
    }
}

public class ModularActorDataSecurity
{
    private readonly ILogger<ModularActorDataSecurity> _logger;

    public ModularActorDataSecurity(ILogger<ModularActorDataSecurity> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Logic that iterates a current test of the SMAD file format.
    /// </summary>
    //public void LogicTest(string testPassword, bool testOverrideExample = false)
    //{
    //    _logger.LogInformation("==== Demo: Encrypted base file + signed update tokens with AAD ====\n");

    //    // TODO: properly retrieve this file.
    //    var basePath = "base.smab";
    //    if (File.Exists(basePath))
    //        File.Delete(basePath);

    //    // Owner generates keypair
    //    using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    //    var ownerPrivPkcs8 = owner.ExportPkcs8PrivateKey(); // owner stores this privately
    //    var ownerPubSpki = owner.ExportSubjectPublicKeyInfo(); // will live in header

    //    // Owner creates encrypted base file containing "Rubber Kitty"
    //    _logger.LogInformation($"[Owner] Creating encrypted base file with plaintext: \"Sample File Data\" and password: ({testPassword})");

    //    var payload = Encoding.UTF8.GetBytes("Sample File Data");

    //    var salt = SmadCryptography.Random(16);
    //    var keyHash = SmadCryptography.DeriveKeyHash(testPassword, salt);
    //    var fileId = Guid.NewGuid();

    //    var initialAllowed = new string[0];
    //    var headerToSign = Combine(fileId.ToByteArray(), salt, keyHash, ownerPubSpki);
    //    var ownerHeaderSig = SmadCryptography.Sign(owner, headerToSign);

    //    var header = new SmabHeader(1, fileId, salt, keyHash, ownerPubSpki, initialAllowed, new OwnerSignature(ownerHeaderSig));

    //    // --- Use AllowedHashes as AAD ---
    //    var fileSecret = SmadCryptography.DeriveFileSecret(testPassword);
    //    var aad = Encoding.UTF8.GetBytes(string.Join(",", header.Hashes));
    //    var (cipher, nonce, tag) = SmadCryptography.AesGcmEncrypt(payload, fileSecret, aad);
    //    Array.Clear(fileSecret); // clear sensitive data

    //    // Write out the file to disk
    //    WriteSmadFile(basePath, header, cipher, nonce, tag);
    //    _logger.LogInformation($"Wrote encrypted base file: {basePath}\n");

    //    // --- Final loader check ---
    //    _logger.LogInformation("==== Final loader check ====");
    //    if (TryLoadAndValidate(basePath, testPassword, out var dec, out var effectiveAllowed))
    //    {
    //        _logger.LogInformation($"[Loader] Decrpyted: {Encoding.UTF8.GetString(dec)}");
    //        _logger.LogInformation($"[Loader] Effective allowed hashes: {string.Join(",", effectiveAllowed)}");
    //    }
    //    else
    //    {
    //        _logger.LogError("[Loader] Validation failed.");
    //    }
    //}

    // --- File format IO ---
    //private void WriteSmadFile(string path, SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag)
    //{
    //    var tmp = path + ".tmp";
    //    var headerJson = SerializeHeader(header);
    //    using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
    //    using var bw = new BinaryWriter(fs);
    //    bw.Write(Encoding.ASCII.GetBytes("SMAD"));
    //    bw.Write(header.Version);
    //    bw.Write(headerJson.Length);
    //    bw.Write(headerJson);
    //    bw.Write(nonce);
    //    bw.Write(tag);
    //    bw.Write(cipher.Length);
    //    bw.Write(cipher);
    //    bw.Flush();
    //    fs.Flush();
    //    fs.Close();
    //    File.Move(tmp, path, true);
    //}

    //private (SmabHeader header, byte[] cipher, byte[] nonce, byte[] tag) ReadSmadFile(string path)
    //{
    //    using var fs = File.OpenRead(path);
    //    using var br = new BinaryReader(fs);
    //    var magic = new string(br.ReadChars(4));
    //    if (magic != "SMAD") throw new InvalidDataException("Bad magic");
    //    var version = br.ReadByte();
    //    var headerLen = br.ReadInt32();
    //    var headerJson = br.ReadBytes(headerLen);
    //    var header = DeserializeHeader(headerJson);
    //    var nonce = br.ReadBytes(12);
    //    var tag = br.ReadBytes(16);
    //    var cipherLen = br.ReadInt32();
    //    var cipher = br.ReadBytes(cipherLen);
    //    return (header, cipher, nonce, tag);
    //}

    //private bool TryLoadAndValidate(string mcdfPath, string providedPassword, out byte[] decryptedPayload, out string[] effectiveAllowed)
    //{
    //    decryptedPayload = null!;
    //    effectiveAllowed = Array.Empty<string>();
    //    try
    //    {
    //        var (header, cipher, nonce, tag) = ReadSmadFile(mcdfPath);

    //        var candidate = SmadCryptography.DeriveKeyHash(providedPassword, header.Salt);
    //        if (!candidate.SequenceEqual(header.Key))
    //        {
    //            Console.WriteLine("[Load] Provided password fails KDF check.");
    //            return false;
    //        }

    //        var fileSecret = SmadCryptography.DeriveFileSecret(providedPassword);
    //        var aad = Encoding.UTF8.GetBytes(string.Join(",", header.Hashes));
    //        var plain = SmadCryptography.AesGcmDecrypt(cipher, nonce, tag, fileSecret, aad);
    //        Array.Clear(fileSecret);
    //        if (plain == null)
    //        {
    //            Console.WriteLine("[Load] Decryption failed / auth tag mismatch (AAD enforcement).");
    //            return false;
    //        }

    //        decryptedPayload = plain;
    //        effectiveAllowed = header.Hashes ?? Array.Empty<string>();
    //        return true;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine("[Load] Exception: " + ex.Message);
    //        return false;
    //    }
    //}

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
