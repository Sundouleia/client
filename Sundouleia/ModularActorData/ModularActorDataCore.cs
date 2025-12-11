using System.Security.Cryptography;
using System.Text.Json;

namespace Sundouleia.ModularActorData;

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
    public void LogicTest(string testPassword, bool testOverrideExample = false)
    {
        _logger.LogInformation("==== Demo: Encrypted base file + signed update tokens with AAD ====\n");

        // TODO: properly retrieve this file.
        var basePath = "base.smab";
        if (File.Exists(basePath))
            File.Delete(basePath);

        // Owner generates keypair
        using var owner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ownerPrivPkcs8 = owner.ExportPkcs8PrivateKey(); // owner stores this privately
        var ownerPubSpki = owner.ExportSubjectPublicKeyInfo(); // will live in header

        // Owner creates encrypted base file containing "Rubber Kitty"
        _logger.LogInformation($"[Owner] Creating encrypted base file with plaintext: \"Sample File Data\" and password: ({testPassword})");

        var payload = Encoding.UTF8.GetBytes("Sample File Data");

        var salt = SmadCryptography.Random(16);
        var keyHash = SmadCryptography.DeriveKeyHash(testPassword, salt);
        var fileId = Guid.NewGuid();

        var initialAllowed = new string[0];
        var headerToSign = Combine(fileId.ToByteArray(), salt, keyHash, ownerPubSpki);
        var ownerHeaderSig = SmadCryptography.Sign(owner, headerToSign);

        var header = new SmadHeader(1, fileId, salt, keyHash, ownerPubSpki, initialAllowed, new SignedHeaderSignature(ownerHeaderSig));

        // --- Use AllowedHashes as AAD ---
        var fileSecret = SmadCryptography.DeriveFileSecret(testPassword);
        var aad = Encoding.UTF8.GetBytes(string.Join(",", header.AllowedHashes));
        var (cipher, nonce, tag) = SmadCryptography.AesGcmEncrypt(payload, fileSecret, aad);
        Array.Clear(fileSecret); // clear sensitive data

        // Write out the file to disk
        WriteSmadFile(basePath, header, cipher, nonce, tag);
        _logger.LogInformation($"Wrote encrypted base file: {basePath}\n");


        // --- Apply valid token ---
        var allowedHash = "sample-hashed-outfit-key";
        var tokenPayload = new UpdateTokenPayload(fileId, [ allowedHash ], Array.Empty<string>(), DateTimeOffset.UtcNow.AddMinutes(5).UtcTicks, Guid.NewGuid().ToString("N"));
        var tokenBytes = CanonicalizeTokenPayload(tokenPayload);
        var tokenSig = SmadCryptography.Sign(owner, tokenBytes);
        var token = new UpdateToken(tokenPayload, tokenSig);
        File.WriteAllBytes("valid_owned_sample_token.json", SerializeToken(token));

        _logger.LogInformation($"Applying valid token to add allowed hash: {allowedHash}");
        _logger.LogInformation("Applying valid token...");
        _logger.LogInformation("Result: " + (TryApplyTokenToFile(basePath, "valid_owned_sample_token.json") ? "ACCEPTED" : "REJECTED") + "\n");

        // --- Attempt forgery 1: sign with public-only key ---
        _logger.LogWarning("=== Forgery 1: attacker uses public key only ===");
        try
        {
            using var attacker = ECDsa.Create();
            attacker.ImportSubjectPublicKeyInfo(ownerPubSpki, out _);
            var fakeSig = attacker.SignData(tokenBytes, HashAlgorithmName.SHA256);
            _logger.LogError($"Attacker data Unexpectedly signed: {Convert.ToHexString(fakeSig)}");
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"[Expected] Correctly failed to sign with public key: {ex.Message}\n");
        }

        // --- Attempt forgery 2: attacker creates own keypair & signs token ---
        _logger.LogWarning("=== Forgery 2: attacker generates own keypair & signs malicious token ===");
        using var evil = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var malPayload = new UpdateTokenPayload(fileId, new[] { "evil-hash-666" }, Array.Empty<string>(), DateTimeOffset.UtcNow.AddMinutes(60).UtcTicks, Guid.NewGuid().ToString("N"));
        var malBytes = CanonicalizeTokenPayload(malPayload);
        var malSig = SmadCryptography.Sign(evil, malBytes);
        var malToken = new UpdateToken(malPayload, malSig);
        File.WriteAllBytes("forged_update_token.json", SerializeToken(malToken));

        _logger.LogInformation("Applying attacker forged token...");
        var result = TryApplyTokenToFile(basePath, "forged_update_token.json");
        _logger.LogInformation("Result: " + (result ? "ACCEPTED (BAD)" : "REJECTED (GOOD)") + "\n");


        // --- Attempt forgery 3: modify allowed hashes in legitimate token ---
        _logger.LogWarning("=== Forgery 3: tamper with valid token's allowed hashes ===");

        var originalToken = DeserializeToken(File.ReadAllBytes("valid_owned_sample_token.json"));
        var tamperedPayload = new UpdateTokenPayload(fileId, [ "tampered-hash" ], Array.Empty<string>(), originalToken.Payload.ExpiresUtcTicks, originalToken.Payload.Nonce);
        var tamperedToken = new UpdateToken(tamperedPayload, originalToken.OwnerSignature);
        File.WriteAllBytes("token_attacker_tampered.json", SerializeToken(tamperedToken));
        
        _logger.LogInformation("Applying tampered token...");
        var tamperResult = TryApplyTokenToFile(basePath, "token_attacker_tampered.json");
        _logger.LogInformation("Result: " + (tamperResult ? "ACCEPTED (BAD)" : "REJECTED (GOOD)") + "\n");

        // --- Attempt forgery 4: Successfully modify the file, however it becomes an invalid file ---
        if (testOverrideExample)
        {
            _logger.LogWarning("=== Forgery 4: Attacker directly tampers with AllowedHashes in header ===");
            var (origHeader, origCipher, origNonce, origTag) = ReadMcdfFile(basePath);

            _logger.LogInformation("Original AllowedHashes: " + string.Join(",", origHeader.AllowedHashes));
            // Attacker tampers with the header
            var hacked = origHeader.AllowedHashes.ToList();
            hacked.Add("evil-hash-direct-edit");
            var tamperedHeader = new SmadHeader(origHeader.Version, origHeader.FileId, origHeader.Salt, origHeader.KeyHash, origHeader.OwnerPubKey, hacked.ToArray(), origHeader.OwnerHeaderSignature);
            _logger.LogInformation("Writing tempered AllowedHashes into base.smab...");
            
            // Write modified header + original ciphertext
            WriteSmadFile(basePath, tamperedHeader, origCipher, origNonce, origTag);
            
            // Loader tries to decrypt
            _logger.LogInformation("Attempting to load file after tampering...");
            if (TryLoadAndValidate(basePath, testPassword, out var dec4, out var allowed4))
                _logger.LogError($"Result: ACCEPTED (BAD - should never happen), Decrypted: {Encoding.UTF8.GetString(dec4)}, AllowedHashes: {string.Join(",", allowed4)}");
            else
                _logger.LogInformation($"Result: REJECTED (GOOD - AAD detected tampering)");
        }


        // --- Final loader check ---
        _logger.LogInformation("==== Final loader check ====");
        if (TryLoadAndValidate(basePath, testPassword, out var dec, out var effectiveAllowed))
        {
            _logger.LogInformation($"[Loader] Decrpyted: {Encoding.UTF8.GetString(dec)}");
            _logger.LogInformation($"[Loader] Effective allowed hashes: {string.Join(",", effectiveAllowed)}");
        }
        else
        {
            _logger.LogError("[Loader] Validation failed.");
        }
    }

    // --- Serialization helpers ---
    private byte[] SerializeHeader(SmadHeader h)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        var wrapper = new
        {
            Version = h.Version,
            FileId = h.FileId,
            Salt = Convert.ToBase64String(h.Salt),
            KeyHash = Convert.ToBase64String(h.KeyHash),
            OwnerPubKey = Convert.ToBase64String(h.OwnerPubKey),
            AllowedHashes = h.AllowedHashes,
            OwnerHeaderSignature = h.OwnerHeaderSignature == null ? null : Convert.ToBase64String(h.OwnerHeaderSignature.Signature)
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(wrapper, opts);
    }

    private SmadHeader DeserializeHeader(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = root.GetProperty("Version").GetByte();
        var fileId = root.GetProperty("FileId").GetGuid();
        var salt = Convert.FromBase64String(root.GetProperty("Salt").GetString()!);
        var keyHash = Convert.FromBase64String(root.GetProperty("KeyHash").GetString()!);
        var ownerPub = Convert.FromBase64String(root.GetProperty("OwnerPubKey").GetString()!);
        var initialAllowed = root.GetProperty("AllowedHashes").EnumerateArray().Select(e => e.GetString()).ToArray()!;
        SignedHeaderSignature? sig = null;
        if (root.TryGetProperty("OwnerHeaderSignature", out var sElem) && sElem.ValueKind != JsonValueKind.Null)
        {
            var sbytes = Convert.FromBase64String(sElem.GetString()!);
            sig = new SignedHeaderSignature(sbytes);
        }
        return new SmadHeader(version, fileId, salt, keyHash, ownerPub, initialAllowed, sig);
    }

    private byte[] SerializeToken(UpdateToken t)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        var wrapper = new
        {
            Payload = new
            {
                FileId = t.Payload.FileId,
                AddHashes = t.Payload.AddHashes,
                RemoveHashes = t.Payload.RemoveHashes,
                ExpiresUtcTicks = t.Payload.ExpiresUtcTicks,
                Nonce = t.Payload.Nonce
            },
            OwnerSignature = Convert.ToBase64String(t.OwnerSignature)
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(wrapper, opts);
    }

    private UpdateToken DeserializeToken(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var p = root.GetProperty("Payload");
        var fileId = p.GetProperty("FileId").GetGuid();
        var adds = p.GetProperty("AddHashes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var removes = p.GetProperty("RemoveHashes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        var expires = p.GetProperty("ExpiresUtcTicks").GetInt64();
        var nonce = p.GetProperty("Nonce").GetString();
        var sig = Convert.FromBase64String(root.GetProperty("OwnerSignature").GetString()!);
        return new UpdateToken(new UpdateTokenPayload(fileId, adds, removes, expires, nonce), sig);
    }

    private byte[] CanonicalizeTokenPayload(UpdateTokenPayload p)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        bw.Write(p.FileId.ToByteArray());
        bw.Write(p.ExpiresUtcTicks);
        bw.Write(p.Nonce ?? "");
        bw.Write(p.AddHashes?.Length ?? 0);
        if (p.AddHashes != null)
            foreach (var a in p.AddHashes)
                bw.Write(a);

        bw.Write(p.RemoveHashes?.Length ?? 0);
        if (p.RemoveHashes != null)
            foreach (var r in p.RemoveHashes)
                bw.Write(r);

        bw.Flush();
        return ms.ToArray();
    }

    // --- File format IO ---
    private void WriteSmadFile(string path, SmadHeader header, byte[] cipher, byte[] nonce, byte[] tag)
    {
        var tmp = path + ".tmp";
        var headerJson = SerializeHeader(header);
        using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);
        bw.Write(Encoding.ASCII.GetBytes("SMAD"));
        bw.Write(header.Version);
        bw.Write(headerJson.Length);
        bw.Write(headerJson);
        bw.Write(nonce);
        bw.Write(tag);
        bw.Write(cipher.Length);
        bw.Write(cipher);
        bw.Flush();
        fs.Flush();
        fs.Close();
        File.Move(tmp, path, true);
    }

    private (SmadHeader header, byte[] cipher, byte[] nonce, byte[] tag) ReadMcdfFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        var magic = new string(br.ReadChars(4));
        if (magic != "SMAD") throw new InvalidDataException("Bad magic");
        var version = br.ReadByte();
        var headerLen = br.ReadInt32();
        var headerJson = br.ReadBytes(headerLen);
        var header = DeserializeHeader(headerJson);
        var nonce = br.ReadBytes(12);
        var tag = br.ReadBytes(16);
        var cipherLen = br.ReadInt32();
        var cipher = br.ReadBytes(cipherLen);
        return (header, cipher, nonce, tag);
    }

    private bool TryLoadAndValidate(string mcdfPath, string providedPassword, out byte[] decryptedPayload, out string[] effectiveAllowed)
    {
        decryptedPayload = null!;
        effectiveAllowed = Array.Empty<string>();
        try
        {
            var (header, cipher, nonce, tag) = ReadMcdfFile(mcdfPath);

            var candidate = SmadCryptography.DeriveKeyHash(providedPassword, header.Salt);
            if (!candidate.SequenceEqual(header.KeyHash))
            {
                Console.WriteLine("[Load] Provided password fails KDF check.");
                return false;
            }

            var fileSecret = SmadCryptography.DeriveFileSecret(providedPassword);
            var aad = Encoding.UTF8.GetBytes(string.Join(",", header.AllowedHashes));
            var plain = SmadCryptography.AesGcmDecrypt(cipher, nonce, tag, fileSecret, aad);
            Array.Clear(fileSecret);
            if (plain == null)
            {
                Console.WriteLine("[Load] Decryption failed / auth tag mismatch (AAD enforcement).");
                return false;
            }

            decryptedPayload = plain;
            effectiveAllowed = header.AllowedHashes ?? Array.Empty<string>();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Load] Exception: " + ex.Message);
            return false;
        }
    }

    private bool TryApplyTokenToFile(string mcdfPath, string tokenPath)
    {
        try
        {
            var tokenBytes = File.ReadAllBytes(tokenPath);
            var token = DeserializeToken(tokenBytes);
            var (header, cipher, nonce, tag) = ReadMcdfFile(mcdfPath);

            if (token.Payload.FileId != header.FileId) return false;
            if (token.Payload.ExpiresUtcTicks < DateTimeOffset.UtcNow.UtcTicks) return false;

            using var ownerPub = SmadCryptography.ImportPublic(header.OwnerPubKey);
            var data = CanonicalizeTokenPayload(token.Payload);
            if (!SmadCryptography.Verify(ownerPub, data, token.OwnerSignature)) return false;

            var allAllowed = header.AllowedHashes.ToList();
            foreach (var a in token.Payload.AddHashes) if (!allAllowed.Contains(a)) allAllowed.Add(a);
            foreach (var r in token.Payload.RemoveHashes) if (allAllowed.Contains(r)) allAllowed.Remove(r);

            var newHeader = new SmadHeader(header.Version, header.FileId, header.Salt, header.KeyHash, header.OwnerPubKey, allAllowed.ToArray(), header.OwnerHeaderSignature);

            // --- re-encrypt using new AAD ---
            var fileSecret = SmadCryptography.DeriveFileSecret("base-password-123"); // for demo, we hardcode; in prod, pass correct password
            var newAad = Encoding.UTF8.GetBytes(string.Join(",", newHeader.AllowedHashes));
            var newCipherPlain = SmadCryptography.AesGcmDecrypt(cipher, nonce, tag, fileSecret, Encoding.UTF8.GetBytes(string.Join(",", header.AllowedHashes)))!;
            var (newCipher, newNonce, newTag) = SmadCryptography.AesGcmEncrypt(newCipherPlain, fileSecret, newAad);
            Array.Clear(fileSecret);

            WriteSmadFile(mcdfPath, newHeader, newCipher, newNonce, newTag);
            return true;
        }
        catch { return false; }
    }

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
