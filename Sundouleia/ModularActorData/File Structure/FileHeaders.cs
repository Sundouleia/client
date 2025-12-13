using System.Security.Cryptography;
using System.Text.Json;

namespace Sundouleia.ModularActor;

// Corby is creating a new filetype and it is consuming
// a lot of brain power so sry if slow on responses for a bit.

public record OwnerSignature(byte[] Signature);

/// <summary>
///     Internal record representing the header of a SMAB file prior to it's encrypted contents.
/// </summary>
/// <param name="Version"> The version of the SMAB file format.</param>
/// <param name="Id"> The unique identifier for the file.</param>
/// <param name="Salt"> The salt used in key derivation.</param>
/// <param name="Key"> The hash of the encryption key.</param>
/// <param name="OwnerPubKey"> The public key of the file owner in SPKI format.</param>
/// <param name="Hashes"> What other outfits and items/itempacks are allowed to be used with this file.</param>
/// <param name="Signature"> The owner's signature over the header data for authenticity verification.</param>
public record SmabHeader(byte Version, Guid Id, byte[] Salt, byte[] Key, byte[] OwnerPubKey, string[] Hashes, OwnerSignature? Signature)
{
    public static readonly byte CurrentVersion = 1;

    internal static byte[] Serialize(SmabHeader h)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false };
        var wrapper = new
        {
            Version = h.Version,
            FileId = h.Id,
            Salt = Convert.ToBase64String(h.Salt),
            KeyHash = Convert.ToBase64String(h.Key),
            OwnerPubKey = Convert.ToBase64String(h.OwnerPubKey),
            AllowedHashes = h.Hashes,
            OwnerHeaderSignature = h.Signature == null ? null : Convert.ToBase64String(h.Signature.Signature)
        };
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(wrapper, opts);
    }

    internal static SmabHeader Deserialize(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = root.GetProperty("Version").GetByte();
        var fileId = root.GetProperty("FileId").GetGuid();
        var salt = Convert.FromBase64String(root.GetProperty("Salt").GetString() ?? throw new Exception("Invalid Salt in SMAB header."));
        var keyHash = Convert.FromBase64String(root.GetProperty("KeyHash").GetString() ?? throw new Exception("Invalid KeyHash in SMAB header."));
        var ownerPubKey = Convert.FromBase64String(root.GetProperty("OwnerPubKey").GetString() ?? throw new Exception("Invalid OwnerPubKey in SMAB header."));
        var allowedHashes = root.GetProperty("AllowedHashes").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
        OwnerSignature? signature = null;
        if (root.TryGetProperty("OwnerHeaderSignature", out var sigElement) && sigElement.ValueKind != JsonValueKind.Null)
        {
            var sigBytes = Convert.FromBase64String(sigElement.GetString() ?? throw new Exception("Invalid OwnerHeaderSignature in SMAB header."));
            signature = new OwnerSignature(sigBytes);
        }
        return new SmabHeader(version, fileId, salt, keyHash, ownerPubKey, allowedHashes, signature);
    }
}

internal record SmaoHeader(byte Version)
{
    public static readonly byte CurrentVersion = 1;
}

internal record SmaiHeader(byte Version)
{
    public static readonly byte CurrentVersion = 1;
}

internal record SmaipHeader(byte Version, string[] ItemHashes)
{
    public static readonly byte CurrentVersion = 1;
}

// Allowance Updates are .JSON encoded payloads that represent valid changes to an allowance list
// from a base file containing identical data. These updates require knowledge of the contents contained
// post-update to be valid, and will otherwise fail.

internal record UpdateTokenPayload(Guid FileId, string[] AddHashes, string[] RemoveHashes, long ExpiresUtcTicks, string Nonce);

internal record UpdateToken(UpdateTokenPayload Payload, OwnerSignature Signature);